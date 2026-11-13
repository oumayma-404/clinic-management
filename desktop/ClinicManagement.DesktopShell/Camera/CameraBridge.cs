using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// The camera half of <c>window.__clinicShell</c> (<c>features/camera-photo-import</c>, phase 2, shell 1.8): the page's
/// <c>camera-*</c> messages in, batches of photos (as read-only file handles) and kept originals out.
///
/// <para>⚠️ <b>A clinic page only</b> — the cloud's own, or the PC de secours's (<c>MainWindow.IsClinicPage</c>), so an
/// import during a cut lands where the cabinet is working. Anything else is ignored, never answered.</para>
///
/// <para>⚠️ <b>Handles, never bytes.</b> <c>CreateWebFileSystemFileHandle</c> gives the page a <c>File</c> it reads
/// itself, so a 30 Mo photo never crosses the bridge as base64, and <c>ReadOnly</c> means the page cannot write to the
/// copy it was handed.</para>
///
/// <para>⚠️ <b>A batch is a message, not a reply.</b> File handles can only travel with
/// <c>PostWebMessageAsJson</c>, so « Importer » answers a count while the photos arrive through the listener, parked in
/// a queue when the page is not ready yet — the coffre's single slot would lose a second batch.</para>
/// </summary>
public sealed class CameraBridge : IDisposable
{
    public const string Prefix = "camera-";

    /// <summary>One copy to the archive disk this long after the last original was kept, not one per photo.</summary>
    private static readonly TimeSpan CopyAfterKeep = TimeSpan.FromSeconds(30);

    /// <summary>Windows announces one card several times; one notice is enough.</summary>
    private static readonly TimeSpan ArrivalQuiet = TimeSpan.FromSeconds(20);

    private readonly Window _window;
    private readonly Func<CoreWebView2?> _core;
    private readonly Func<string?, bool> _isClinicPage;
    private readonly Func<string, object?, Task> _deliver;
    private readonly CameraImportService _service;
    private readonly DeviceArrivalWatcher _watcher;
    private readonly DispatcherTimer _copyTimer;
    private readonly Dictionary<string, DateTime> _recentArrivals = new();

    private bool _sessionStarted;
    private int _copyRunning;
    private OriginalsCopyOutcome? _lastCopy;
    private DateTime? _lastCopyAtUtc;

    public CameraBridge(
        Window window,
        Func<CoreWebView2?> core,
        Func<string?, bool> isClinicPage,
        Func<string, object?, Task> deliver,
        CameraImportService? service = null)
    {
        _window = window;
        _core = core;
        _isClinicPage = isClinicPage;
        _deliver = deliver;
        _service = service ?? CameraImportService.CreateDefault();
        _watcher = new DeviceArrivalWatcher(window.Dispatcher);
        _watcher.Arrived += OnSourceArrived;

        _copyTimer = new DispatcherTimer { Interval = CopyAfterKeep };
        _copyTimer.Tick += (_, _) =>
        {
            _copyTimer.Stop();
            _ = RunOriginalsCopyAsync();
        };
    }

    /// <summary>
    /// After every successful clinic page load: the watcher when the switch is on, the originals the server has not
    /// written down yet (a page reloaded before it could), and — once per session — housekeeping and a copy to the
    /// archive disk.
    /// </summary>
    public void OnClinicPageLoaded()
    {
        // ⚠️ On the navigation path: an exception here would take the whole window down with an unhandled
        // NavigationCompleted, over a feature most cabinets never switch on. A failure costs the camera, nothing else.
        try
        {
            if (CameraSettingsStore.Load().AutoImport)
            {
                _watcher.Start(_window);
            }

            foreach (var notice in _service.UnrecordedOriginals())
            {
                PostOriginalKept(notice);
            }

            if (_sessionStarted)
            {
                return;
            }

            _sessionStarted = true;
            _ = Task.Run(_service.Housekeep);
            _ = RunOriginalsCopyAsync();
        }
        catch
        {
            // The next page load tries again.
        }
    }

    /// <summary>Handles a <c>camera-*</c> message. True when it was one (answered or deliberately ignored).</summary>
    public bool TryHandle(string message, string? source)
    {
        if (!message.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_isClinicPage(source))
        {
            return true;
        }

        var body = message[Prefix.Length..];
        var separator = body.IndexOf(':');
        var verb = separator < 0 ? body : body[..separator];
        var rest = separator < 0 ? string.Empty : body[(separator + 1)..];

        try
        {
            Dispatch(verb, rest);
        }
        catch
        {
            // A WebMessageReceived handler that throws ends the process; a refused photo answer is offered again.
        }

        return true;
    }

    private void Dispatch(string verb, string rest)
    {
        switch (verb)
        {
            case "ack":
                if (CameraAck.TryParse(rest) is { } ack)
                {
                    Acknowledge(ack);
                }

                break;
            case "recorded":
                if (CameraMessages.FileIdOf(rest) is { } recorded)
                {
                    _service.MarkOriginalRecorded(recorded);
                }

                break;
            case "ignore":
                if (CameraMessages.SourceKeyOf(rest) is { } ignored)
                {
                    var settings = CameraSettingsStore.Load();
                    if (!settings.IgnoredSources.Contains(ignored))
                    {
                        settings.IgnoredSources.Add(ignored);
                        CameraSettingsStore.TrySave(settings);
                    }
                }

                break;
            default:
                if (CameraMessages.TrySplitRequest(rest, out var requestId, out var json))
                {
                    _ = AnswerAsync(verb, requestId, json);
                }

                break;
        }
    }

    /// <summary>A click on « Importer » in a Windows notification: the window comes forward on the import screen.</summary>
    public async void OpenFromNotification(string sourceKey)
    {
        ShellNotifications.BringForward(_window);
        var key = JsonSerializer.Serialize(sourceKey);

        // After a restart the page is still loading: give it up to thirty seconds to register its handler.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var core = _core();
                if (core != null && _isClinicPage(core.Source))
                {
                    var opened = await core.ExecuteScriptAsync(
                        $"typeof window.__clinicShellOpenCameraImport === 'function' ? (window.__clinicShellOpenCameraImport({key}), true) : false");
                    if (opened == "true")
                    {
                        return;
                    }
                }
            }
            catch
            {
                // The page is navigating; try again.
            }

            await Task.Delay(1000);
        }
    }

    public void Dispose()
    {
        _copyTimer.Stop();
        _watcher.Dispose();
    }

    private async Task AnswerAsync(string verb, string requestId, string? json)
    {
        try
        {
            object? answer = verb switch
            {
                "status" => Status(),
                "auto" => SetAutoImport(CameraMessages.OnOf(json)),
                "scan" => await ScanAsync(CameraScanRequest.Parse(json)),
                "open" => OpenOriginal(CameraMessages.FileIdOf(json)),
                "held" => CameraMessages.FileIdsOf(json)
                    .Where(_service.Originals.Holds)
                    .Select(id => id.ToString("D"))
                    .ToList(),
                "copy" => await RunOriginalsCopyAsync(),
                _ => null,
            };

            await _deliver(requestId, answer);
        }
        catch
        {
            // The page's own timeout settles the promise.
        }
    }

    private object Status()
    {
        var settings = CameraSettingsStore.Load();
        var (files, bytes) = _service.Originals.Totals();
        return new
        {
            autoImport = settings.AutoImport,
            pending = _service.PendingCount(),
            heldBy = Environment.MachineName,
            originals = new { files, bytes, freeBytes = _service.Originals.FreeBytes() },
            copy = _lastCopy is { } last
                ? new { complete = last.Complete, sentence = last.Sentence, atUtc = _lastCopyAtUtc }
                : null,
        };
    }

    private object SetAutoImport(bool? on)
    {
        if (on is { } value)
        {
            var settings = CameraSettingsStore.Load();
            settings.AutoImport = value;
            CameraSettingsStore.TrySave(settings);

            if (value)
            {
                _watcher.Start(_window);
            }
            else
            {
                _watcher.Stop();
            }
        }

        return Status();
    }

    private async Task<object> ScanAsync(CameraScanRequest request)
    {
        var sources = await Task.Run(CameraSources.FindConnectedAsync);
        if (request.SourceKey is { } wanted)
        {
            // A notice names one device; if it has gone, every connected one is the next best answer.
            var named = sources.Where(s => s.SourceKey == wanted).ToList();
            if (named.Count > 0)
            {
                sources = named;
            }
        }

        var batch = await Task.Run(() => _service.BuildBatchAsync(sources, request.IncludeOlder));
        var posted = batch.Items.Count > 0 ? PostBatch(batch) : 0;

        string? sentence = null;
        if (posted == 0)
        {
            sentence = sources.Count == 0
                ? "Aucun appareil photo ni carte n'est branché. Branchez l'appareil (ou insérez sa carte), puis réessayez."
                : batch.Older > 0
                    ? $"Aucune nouvelle photo sur {batch.Label}. {batch.Older} plus ancienne(s) y reste(nt)."
                    : $"Aucune nouvelle photo sur {batch.Label}.";

            if (batch.Failed > 0)
            {
                sentence = $"{batch.Failed} photo(s) n'ont pas pu être copiées depuis {batch.Label}. "
                           + "Vérifiez la place libre sur ce PC, puis réessayez.";
            }
        }

        return new { delivered = posted, older = batch.Older, failed = batch.Failed, sentence };
    }

    /// <summary>Hands one batch to the page with a read-only handle per photo. Returns how many went.</summary>
    private int PostBatch(CameraBatch batch)
    {
        var core = _core();
        if (core == null || !_isClinicPage(core.Source))
        {
            return 0;
        }

        var handles = new List<object>();
        var items = new List<object>();
        foreach (var item in batch.Items)
        {
            try
            {
                handles.Add(core.Environment.CreateWebFileSystemFileHandle(
                    item.Path, CoreWebView2FileSystemHandlePermission.ReadOnly));
                items.Add(new { itemId = item.ItemId, name = item.Name });
            }
            catch
            {
                // One copy that vanished must not shift every handle after it onto the wrong item.
            }
        }

        if (items.Count == 0)
        {
            return 0;
        }

        var payload = JsonSerializer.Serialize(new
        {
            kind = "camera-batch",
            batchId = batch.BatchId,
            label = batch.Label,
            older = batch.Older,
            failed = batch.Failed,
            skipped = new { raw = batch.Raw, heic = batch.Heic, video = batch.Video, other = batch.Other },
            items,
        });

        try
        {
            core.PostWebMessageAsJson(payload, handles);
            return items.Count;
        }
        catch
        {
            return 0;
        }
    }

    private void Acknowledge(CameraAck ack)
    {
        var result = _service.Acknowledge(ack);
        if (result.Kept is { } kept)
        {
            PostOriginalKept(kept);
            _copyTimer.Stop();
            _copyTimer.Start();
        }
        else if (result.Refusal is { } refusal && ack.FileId is { } fileId)
        {
            Post(new { kind = "camera-original-refused", fileId = fileId.ToString("D"), sentence = refusal });
        }
    }

    private void PostOriginalKept(KeptOriginalNotice notice) =>
        Post(new
        {
            kind = "camera-original-kept",
            fileId = notice.FileId.ToString("D"),
            patientId = notice.PatientId.ToString("D"),
            sha256 = notice.Sha256,
            sizeBytes = notice.SizeBytes,
            heldBy = notice.HeldBy,
        });

    private bool OpenOriginal(Guid? fileId)
    {
        if (fileId is not { } id || !_service.Originals.Holds(id))
        {
            return false;
        }

        try
        {
            // The path is the store's own, built from a parsed Guid — nothing the page wrote reaches the shell.
            Process.Start(new ProcessStartInfo(_service.Originals.PathFor(id)) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the kept originals to the archive disk and, when the copy is complete, hands the figures to the page to
    /// report. Only on a PC that keeps originals at all: one that never did has nothing to say to the server.
    /// </summary>
    private async Task<object?> RunOriginalsCopyAsync()
    {
        try
        {
            return await CopyOriginalsAsync();
        }
        catch
        {
            // Fire-and-forget from a page load: an unobserved failure must not surface anywhere but the next run.
            return new { complete = false, sentence = "La copie des photos originales n'a pas pu être lancée.", files = 0, bytes = 0L };
        }
    }

    private async Task<object?> CopyOriginalsAsync()
    {
        if (!System.IO.Directory.Exists(_service.Originals.Root))
        {
            return new { complete = true, sentence = "Aucune photo originale n'est gardée sur ce PC.", files = 0, bytes = 0L };
        }

        if (Interlocked.Exchange(ref _copyRunning, 1) == 1)
        {
            return new { complete = false, sentence = "Une copie est déjà en cours.", files = 0, bytes = 0L };
        }

        try
        {
            var folder = ArchiveCopySettingsStore.Load().Folder;
            var outcome = await Task.Run(() => OriginalsCopyService.CopyNowAsync(_service.Originals, folder));
            _lastCopy = outcome;
            _lastCopyAtUtc = DateTime.UtcNow;

            if (outcome.Complete)
            {
                Post(new
                {
                    kind = "originals-copy",
                    heldBy = outcome.HeldBy,
                    files = outcome.Files,
                    bytes = outcome.Bytes,
                });
            }

            return new { complete = outcome.Complete, sentence = outcome.Sentence, files = outcome.Files, bytes = outcome.Bytes };
        }
        finally
        {
            Interlocked.Exchange(ref _copyRunning, 0);
        }
    }

    private void OnSourceArrived(ICameraSource source)
    {
        try
        {
            NoticeArrival(source);
        }
        catch
        {
            // A device plugged in must never be why the app closes.
        }
    }

    private void NoticeArrival(ICameraSource source)
    {
        var settings = CameraSettingsStore.Load();
        if (!settings.AutoImport || settings.IgnoredSources.Contains(source.SourceKey))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_recentArrivals.TryGetValue(source.SourceKey, out var last) && now - last < ArrivalQuiet)
        {
            return;
        }

        _recentArrivals[source.SourceKey] = now;

        var label = source.Label.Length > 80 ? source.Label[..80] : source.Label;
        Post(new { kind = "camera-detected", sourceKey = source.SourceKey, label });

        if (!_window.IsActive)
        {
            ShellNotifications.ShowCameraDetected(_window, label, source.SourceKey);
        }
    }

    private void Post(object payload)
    {
        try
        {
            var core = _core();
            if (core != null && _isClinicPage(core.Source))
            {
                core.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            }
        }
        catch
        {
            // The page is navigating; the next load re-sends what matters (unrecorded originals).
        }
    }
}
