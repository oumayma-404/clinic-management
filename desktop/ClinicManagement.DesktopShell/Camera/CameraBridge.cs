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
///
/// <para>⚠️ <b>The page's answers are handled off the window's thread, one at a time</b> (<see cref="Enqueue"/>): each
/// one moves files and writes the journal, and a card of 500 photos already on the server is 500 answers in a second —
/// on the window's thread that was a window « Ne répond pas ».</para>
/// </summary>
public sealed class CameraBridge : IDisposable
{
    public const string Prefix = "camera-";

    /// <summary>One copy to the archive disk this long after the last original was kept, not one per photo.</summary>
    private static readonly TimeSpan CopyAfterKeep = TimeSpan.FromSeconds(30);

    /// <summary>Windows announces one card several times; one notice is enough.</summary>
    private static readonly TimeSpan ArrivalQuiet = TimeSpan.FromSeconds(20);

    /// <summary>« Importer » says it is alive at most this often — enough to keep the page from giving up on it.</summary>
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(1);

    /// <summary>How long closing the window waits for the page's last answers before writing the journal.</summary>
    private static readonly TimeSpan DrainOnClose = TimeSpan.FromSeconds(3);

    private readonly Window _window;
    private readonly Func<CoreWebView2?> _core;
    private readonly Func<string?, bool> _isClinicPage;
    private readonly Func<string, object?, Task> _deliver;
    private readonly CameraImportService _service;
    private readonly DeviceArrivalWatcher _watcher;
    private readonly DispatcherTimer _copyTimer;
    private readonly Dictionary<string, DateTime> _recentArrivals = new();
    private readonly object _workGate = new();

    private Task _work = Task.CompletedTask;
    private bool _sessionStarted;
    private int _copyRunning;
    private OriginalsCopyOutcome? _lastCopy;
    private DateTime? _lastCopyAtUtc;
    private DateTime _lastProgressUtc;

    /// <summary>The devices the batch on screen came from — what « plus anciennes » may read again.</summary>
    private IReadOnlyCollection<string> _lastLook = Array.Empty<string>();

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

        // Velopack's « Installer et redémarrer » exits the process without closing the window.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
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

            Enqueue(() =>
            {
                foreach (var notice in _service.OriginalsToAnnounce())
                {
                    OnWindow(() => PostOriginalKept(notice));
                }
            });

            if (_sessionStarted)
            {
                return;
            }

            _sessionStarted = true;
            Enqueue(_service.Housekeep);
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
                    Enqueue(() => Acknowledge(ack));
                }

                break;
            case "recorded":
                if (CameraMessages.FileIdOf(rest) is { } recorded)
                {
                    Enqueue(() => _service.MarkOriginalRecorded(recorded));
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
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _copyTimer.Stop();
        _watcher.Dispose();

        // The answers still queued, then the journal they changed — saved per burst, so it may hold the last second.
        Task work;
        lock (_workGate)
        {
            work = _work;
        }

        try
        {
            work.Wait(DrainOnClose);
        }
        catch
        {
            // Every queued step swallows its own failure; nothing here is worth keeping the window open for.
        }

        Flush();
    }

    private void OnProcessExit(object? sender, EventArgs e) => Flush();

    private void Flush()
    {
        try
        {
            _service.Flush();
        }
        catch
        {
            // An unsaved answer costs a photo offered again, nothing more.
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> off the window's thread, after everything queued before it — the page's answers
    /// change the same journal entries, in the order it sent them.
    /// </summary>
    private void Enqueue(Action work)
    {
        lock (_workGate)
        {
            _work = _work.ContinueWith(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch
                    {
                        // One answer that fails must not stop the ones behind it.
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    /// <summary>Back on the window's thread — where the WebView lives — for what the page must hear.</summary>
    private void OnWindow(Action action)
    {
        try
        {
            _window.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    action();
                }
                catch
                {
                    // The page is navigating; the next load re-sends what matters.
                }
            });
        }
        catch
        {
            // The window is closing.
        }
    }

    private async Task AnswerAsync(string verb, string requestId, string? json)
    {
        try
        {
            // ⚠️ Disk work (the originals' totals walk every file kept) runs off the window's thread.
            object? answer = verb switch
            {
                "status" => await Task.Run(Status),
                "auto" => await SetAutoImportAsync(CameraMessages.OnOf(json)),
                "scan" => await ScanAsync(requestId, CameraScanRequest.Parse(json)),
                "open" => OpenOriginal(CameraMessages.FileIdOf(json)),
                "held" => await Task.Run(() => CameraMessages.FileIdsOf(json)
                    .Where(_service.Originals.Holds)
                    .Select(id => id.ToString("D"))
                    .ToList()),
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

    private async Task<object> SetAutoImportAsync(bool? on)
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

        return await Task.Run(Status);
    }

    private async Task<object> ScanAsync(string requestId, CameraScanRequest request)
    {
        var connected = await Task.Run(CameraSources.FindConnectedAsync);
        var choice = request.Choose(connected, CameraSettingsStore.Load().IgnoredSources, _lastLook);
        if (choice.Refusal is { } refusal)
        {
            return new { delivered = 0, older = 0, failed = 0, sentence = refusal };
        }

        // Made here, on the window's thread, so each report comes back to it.
        var progress = new Progress<CameraScanProgress>(p => PostProgress(requestId, p));
        var batch = await Task.Run(() => _service.BuildBatchAsync(choice.Sources, request.IncludeOlder, progress));
        var posted = batch.Items.Count > 0 ? PostBatch(batch) : 0;
        if (posted > 0)
        {
            _lastLook = choice.Sources.Select(s => s.SourceKey).ToList();
        }

        string? sentence = null;
        if (posted == 0)
        {
            sentence = choice.Sources.Count > 0
                ? batch.Older > 0
                    ? $"Aucune nouvelle photo sur {batch.Label}. {batch.Older} plus ancienne(s) y reste(nt)."
                    : $"Aucune nouvelle photo sur {batch.Label}."
                : choice.Ignored > 0
                    ? $"Aucun appareil photo à importer n'est branché : {choice.Ignored} appareil(s) ignoré(s) sur ce PC "
                      + "(« Ignorer cet appareil ») n'ont pas été lus."
                    : "Aucun appareil photo ni carte n'est branché. Branchez l'appareil (ou insérez sa carte), puis réessayez.";

            if (batch.Failed > 0)
            {
                sentence = $"{batch.Failed} photo(s) n'ont pas pu être copiées depuis {batch.Label}. "
                           + "Vérifiez la place libre sur ce PC, puis réessayez.";
            }
        }

        return new { delivered = posted, older = batch.Older, failed = batch.Failed, sentence };
    }

    /// <summary>
    /// <c>camera-progress</c> — re-arms the page's wait on this « Importer » (the injected script's), so a slow cable
    /// is never told « l'appareil ne répond plus » while it is still copying. Never queued by the page.
    /// </summary>
    private void PostProgress(string requestId, CameraScanProgress progress)
    {
        var now = DateTime.UtcNow;
        if (now - _lastProgressUtc < ProgressEvery)
        {
            return;
        }

        _lastProgressUtc = now;
        Post(new { kind = "camera-progress", requestId, copied = progress.Copied, total = progress.Planned });
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

    /// <summary>On the queue (<see cref="Enqueue"/>); what the page must hear goes back to the window's thread.</summary>
    private void Acknowledge(CameraAck ack)
    {
        var result = _service.Acknowledge(ack);
        if (result.Kept is { } kept)
        {
            OnWindow(() =>
            {
                PostOriginalKept(kept);
                _copyTimer.Stop();
                _copyTimer.Start();
            });
        }
        else if (result.Refusal is { } refusal && ack.FileId is { } fileId)
        {
            OnWindow(() => Post(new { kind = "camera-original-refused", fileId = fileId.ToString("D"), sentence = refusal }));
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
            var settings = ArchiveCopySettingsStore.Load();
            var folder = settings.Folder;
            var volumes = settings.FolderVolumes;
            var outcome = await Task.Run(() => OriginalsCopyService.CopyNowAsync(
                _service.Originals, folder, diskRefusal: f => ArchiveDisk.Refusal(f, volumes)));
            _lastCopy = outcome;
            _lastCopyAtUtc = DateTime.UtcNow;

            if (outcome.Complete && volumes.Count == 0)
            {
                // Settings from before 1.8 name no disk: the one this copy just landed on becomes the archive disk.
                _ = Task.Run(() =>
                {
                    if (ArchiveDisk.IdentityOf(folder) is { } volume)
                    {
                        ArchiveCopySettingsStore.RememberFolderVolume(folder, volume);
                    }
                });
            }

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
