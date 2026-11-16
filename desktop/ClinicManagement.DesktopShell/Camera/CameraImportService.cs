using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>One photo handed to the page: the id it answers by, the camera's own name, and the copy on this PC.</summary>
public sealed record CameraBatchItem(string ItemId, string Name, string Path);

/// <summary>
/// What one « Importer » hands the page. <see cref="Older"/> is what stayed on the source, outside the window;
/// <see cref="Failed"/> what could not be copied off it.
/// </summary>
public sealed record CameraBatch(
    string BatchId,
    string Label,
    IReadOnlyList<CameraBatchItem> Items,
    int Older,
    int Raw,
    int Heic,
    int Video,
    int Other,
    int Failed);

/// <summary>An original this PC kept, as the page tells the server about it.</summary>
public sealed record KeptOriginalNotice(Guid FileId, Guid PatientId, string Sha256, long SizeBytes, string HeldBy);

/// <summary>What answering one photo did: an original kept (to report), or a sentence saying why not.</summary>
public sealed record CameraAckResult(bool Known, KeptOriginalNotice? Kept, string? Refusal);

/// <summary>How far one « Importer » has gone: photos copied onto this PC, of those it plans to copy so far.</summary>
public readonly record struct CameraScanProgress(int Copied, int Planned);

/// <summary>
/// The Windows app's half of the camera import (<c>features/camera-photo-import</c>, phase 2), with no window and no
/// WebView in it — so the whole flow, card to kept original, runs in a test.
///
/// <para>⚠️ <b>The page makes every server call.</b> This service copies, remembers and keeps; what it learns that the
/// server must know (an original kept, a copy made) it hands back for the page to send.</para>
/// </summary>
public sealed class CameraImportService
{
    /// <summary>Page loads that re-send one kept original the server will not write down — then it stops.</summary>
    public const int MaxAnnouncements = 20;

    /// <summary>Re-sent per page load, well under the page's 50-message queue, so a new batch always has room.</summary>
    public const int MaxAnnouncedPerLoad = 20;

    private readonly CameraJournal _journal;
    private readonly PhotoStaging _staging;
    private readonly OriginalsStore _originals;
    private readonly Func<DateTime> _nowUtc;
    private readonly SemaphoreSlim _batchGate = new(1, 1);

    public CameraImportService(
        CameraJournal journal, PhotoStaging staging, OriginalsStore originals, Func<DateTime>? nowUtc = null)
    {
        _journal = journal;
        _staging = staging;
        _originals = originals;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public static CameraImportService CreateDefault() =>
        new(
            new CameraJournal(CameraJournal.DefaultPath),
            new PhotoStaging(PhotoStaging.DefaultRoot),
            new OriginalsStore(OriginalsStore.DefaultRoot));

    public OriginalsStore Originals => _originals;

    /// <summary>
    /// Lists each source, keeps what the window says is new, copies it onto this PC and records it as handed over;
    /// adds the photos still waiting from an earlier batch. One batch at a time — a second press waits for the first.
    /// </summary>
    public async Task<CameraBatch> BuildBatchAsync(
        IReadOnlyList<ICameraSource> sources,
        bool includeOlder,
        IProgress<CameraScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _batchGate.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = _nowUtc();
            var todayLocal = nowUtc.ToLocalTime().Date;
            var items = new List<CameraBatchItem>();
            var seen = new HashSet<string>();
            int older = 0, raw = 0, heic = 0, video = 0, other = 0, failed = 0, planned = 0, copied = 0;

            foreach (var source in sources)
            {
                IReadOnlyList<CameraItem> listing;
                try
                {
                    listing = await source.ListAsync(cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // A camera switched off mid-listing: the other sources still count.
                    continue;
                }

                var plan = CameraBatchPlanner.Plan(
                    listing,
                    item => _journal.StateOf(source.SourceKey, item),
                    _journal.NewestDoneLocal(source.SourceKey),
                    todayLocal,
                    includeOlder);

                older += plan.Older;
                raw += plan.Raw;
                heic += plan.Heic;
                video += plan.Video;
                other += plan.Other;
                planned += plan.Deliver.Count;
                progress?.Report(new CameraScanProgress(copied, planned));

                foreach (var item in plan.Deliver)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Before each photo: a slow cable must keep showing it is alive, or the page gives up on it.
                    progress?.Report(new CameraScanProgress(copied, planned));
                    copied++;

                    var itemId = CameraJournal.ItemIdOf(CameraJournal.KeyOf(source.SourceKey, item));
                    var known = _journal.ByItemId(itemId);

                    string path;
                    string sha256;
                    if (known?.StagedPath is { } stagedPath && known.Sha256 is { } knownHash
                        && File.Exists(stagedPath) && new FileInfo(stagedPath).Length == item.Size)
                    {
                        // Copied off the card yesterday and never filed: the copy is still good.
                        path = stagedPath;
                        sha256 = knownHash;
                    }
                    else
                    {
                        try
                        {
                            var staged = await _staging.StageAsync(source, item, itemId, cancellationToken);
                            path = staged.Path;
                            sha256 = staged.Sha256;
                        }
                        catch (Exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            failed++;
                            continue;
                        }
                    }

                    _journal.Record(source.SourceKey, item, CameraItemState.Delivered, path, sha256, nowUtc);
                    items.Add(new CameraBatchItem(itemId, item.Name, path));
                    seen.Add(itemId);
                }
            }

            // « En attente » — handed over before and never answered (a tab closed mid-batch), the source long gone.
            foreach (var entry in _journal.Pending())
            {
                if (seen.Contains(entry.ItemId) || entry.StagedPath is not { } stagedPath || !File.Exists(stagedPath))
                {
                    continue;
                }

                // The page's answers are handled on another thread now: one that landed since must not be undone.
                _journal.Update(
                    entry.ItemId,
                    e =>
                    {
                        if (!CameraBatchPlanner.IsDone(e.State))
                        {
                            e.State = CameraItemState.Delivered;
                        }
                    },
                    nowUtc);
                items.Add(new CameraBatchItem(entry.ItemId, entry.Name, stagedPath));
                seen.Add(entry.ItemId);
            }

            _journal.Save();

            var label = sources.Count == 0
                ? "Photos en attente"
                : string.Join(" · ", sources.Select(s => s.Label).Distinct());
            return new CameraBatch(Guid.NewGuid().ToString("N")[..12], label, items, older, raw, heic, video, other, failed);
        }
        finally
        {
            _batchGate.Release();
        }
    }

    /// <summary>
    /// The page's answer about one photo. A filed photo whose cloud copy is reduced has its original kept here; every
    /// other outcome drops the copy on this PC, except a failure, which keeps it to be offered again.
    /// </summary>
    public CameraAckResult Acknowledge(CameraAck ack)
    {
        var entry = _journal.ByItemId(ack.ItemId);
        if (entry is null)
        {
            return new CameraAckResult(false, null, null);
        }

        var nowUtc = _nowUtc();
        KeptOriginalNotice? kept = null;
        string? refusal = null;

        switch (ack.Outcome)
        {
            case CameraAck.Filed:
                var keepIt = ack.KeepOriginal && entry.StagedPath is not null && entry.Sha256 is not null
                             && File.Exists(entry.StagedPath);
                if (keepIt)
                {
                    var outcome = _originals.Keep(
                        entry.StagedPath!, entry.Sha256!, ack.FileId!.Value, ack.PatientName, entry.TakenLocal, entry.Name);
                    if (outcome.Original is { } original)
                    {
                        kept = new KeptOriginalNotice(
                            original.FileId, ack.PatientId!.Value, original.Sha256, original.SizeBytes, Environment.MachineName);
                    }
                    else
                    {
                        refusal = outcome.Refusal;
                    }
                }

                _staging.Discard(entry.StagedPath);
                _journal.Update(
                    ack.ItemId,
                    e =>
                    {
                        e.State = CameraItemState.Filed;
                        e.FileId = ack.FileId;
                        e.PatientId = ack.PatientId;
                        e.StagedPath = null;
                        e.OriginalKept = e.OriginalKept || kept != null;
                    },
                    nowUtc);
                break;

            case CameraAck.Already:
            case CameraAck.Skipped:
                _staging.Discard(entry.StagedPath);
                _journal.Update(
                    ack.ItemId,
                    e =>
                    {
                        e.State = ack.Outcome == CameraAck.Already
                            ? CameraItemState.AlreadyImported
                            : CameraItemState.Skipped;
                        e.StagedPath = null;
                    },
                    nowUtc);
                break;

            default:
                _journal.Update(ack.ItemId, e => e.State = CameraItemState.Failed, nowUtc);
                break;
        }

        _journal.SaveSoon();
        return new CameraAckResult(true, kept, refusal);
    }

    /// <summary>The server wrote it down: stop offering it on every page load.</summary>
    public void MarkOriginalRecorded(Guid fileId)
    {
        var nowUtc = _nowUtc();
        foreach (var entry in _journal.UnrecordedOriginals().Where(e => e.FileId == fileId))
        {
            _journal.Update(entry.ItemId, e => e.OriginalRecorded = true, nowUtc);
        }

        _journal.SaveSoon();
    }

    /// <summary>
    /// Originals kept here that the server has not written down, to re-send on this page load — and counted as sent.
    ///
    /// <para>⚠️ <b>Bounded twice.</b> One the server keeps refusing (a file deleted meanwhile) stops after
    /// <see cref="MaxAnnouncements"/> loads — the entry stays, the original stays — and one load re-sends at most
    /// <see cref="MaxAnnouncedPerLoad"/>, the least-sent first: the page parks what arrives before it listens in a
    /// queue of 50, and a new batch or a copy report must never be the message that does not fit.</para>
    /// </summary>
    public IReadOnlyList<KeptOriginalNotice> OriginalsToAnnounce()
    {
        var nowUtc = _nowUtc();
        var notices = new List<KeptOriginalNotice>();
        var due = _journal.UnrecordedOriginals()
            .Where(e => e.Announced < MaxAnnouncements && e.Sha256 != null)
            .OrderBy(e => e.Announced)
            .ThenByDescending(e => e.UpdatedUtc);

        foreach (var entry in due)
        {
            var path = _originals.PathFor(entry.FileId!.Value);
            if (!File.Exists(path))
            {
                continue;
            }

            notices.Add(new KeptOriginalNotice(
                entry.FileId.Value, entry.PatientId!.Value, entry.Sha256!, new FileInfo(path).Length, Environment.MachineName));
            _journal.Update(entry.ItemId, e => e.Announced++, nowUtc);

            if (notices.Count == MaxAnnouncedPerLoad)
            {
                break;
            }
        }

        if (notices.Count > 0)
        {
            _journal.SaveSoon();
        }

        return notices;
    }

    /// <summary>Writes what is held back — the journal's last answers, an index line — before the app closes.</summary>
    public void Flush()
    {
        _journal.Flush();
        _originals.FlushIndex();
    }

    public int PendingCount() =>
        _journal.Pending().Count(e => e.StagedPath is { } path && File.Exists(path));

    /// <summary>Forgets what no longer matters and deletes the copies nobody is waiting for. Never throws.</summary>
    public void Housekeep()
    {
        try
        {
            var nowUtc = _nowUtc();
            foreach (var removed in _journal.Prune(nowUtc))
            {
                _staging.Discard(removed.StagedPath);
            }

            var live = _journal.Pending().Select(e => e.ItemId).ToHashSet();
            _staging.Sweep(live, nowUtc);
            _journal.Save();
        }
        catch
        {
            // Housekeeping again tomorrow.
        }
    }
}
