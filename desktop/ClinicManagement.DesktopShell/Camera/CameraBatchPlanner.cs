using System;
using System.Collections.Generic;
using System.Linq;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>Where one item stands in the journal.</summary>
public enum CameraItemState
{
    /// <summary>Copied onto this PC, not yet handed to the page.</summary>
    Staged = 0,

    /// <summary>Handed to the page; nothing answered yet.</summary>
    Delivered = 1,

    /// <summary>Filed in a patient's folder.</summary>
    Filed = 2,

    /// <summary>The server already had it (another PC, the browser path).</summary>
    AlreadyImported = 3,

    /// <summary>The dentist chose not to file it.</summary>
    Skipped = 4,

    /// <summary>Filing failed; offered again next time.</summary>
    Failed = 5,
}

/// <summary>What one look at a source decided.</summary>
public sealed record CameraBatchPlan(
    IReadOnlyList<CameraItem> Deliver,
    int Older,
    int Done,
    int Raw,
    int Heic,
    int Video,
    int Other);

/// <summary>
/// Which of a source's files to copy onto this PC and hand to the page (<c>features/camera-photo-import</c>,
/// phase 2) — pure, so the rule can be read and tested without a camera.
///
/// <para>⚠️ <b>A window, because copying is not free.</b> A camera on a cable has no paths: every photo offered has
/// to be copied off it first, and a card that has never been emptied holds months of them. So a source never seen
/// offers its last <see cref="FirstImportDays"/> days, and a known one everything from the day before the newest
/// photo already filed from it — the page's own window (« depuis le dernier import », 12 h of overlap) is always
/// inside this one. What falls outside is counted, and « Voir les plus anciennes » asks for it.</para>
///
/// <para>⚠️ <b>A photo the journal knows as filed, already on the server or set aside is never offered again</b>,
/// while one handed over and never answered (the tab closed mid-batch) is — that is « en attente ».</para>
/// </summary>
public static class CameraBatchPlanner
{
    public const int FirstImportDays = 7;

    /// <summary>One batch at most — a first import of a full card is asked for in « plus anciennes » steps.</summary>
    public const int MaxDelivered = 1000;

    public static bool IsDone(CameraItemState state) =>
        state is CameraItemState.Filed or CameraItemState.AlreadyImported or CameraItemState.Skipped;

    public static DateTime WindowStart(DateTime? newestDoneLocal, DateTime todayLocal) =>
        newestDoneLocal is { } newest
            ? newest.Date.AddDays(-1)
            : todayLocal.Date.AddDays(-FirstImportDays);

    public static CameraBatchPlan Plan(
        IEnumerable<CameraItem> items,
        Func<CameraItem, CameraItemState?> stateOf,
        DateTime? newestDoneLocal,
        DateTime todayLocal,
        bool includeOlder)
    {
        var start = WindowStart(newestDoneLocal, todayLocal);
        var deliver = new List<CameraItem>();
        int older = 0, done = 0, raw = 0, heic = 0, video = 0, other = 0;

        foreach (var item in items)
        {
            var inWindow = includeOlder || item.TakenLocal is null || item.TakenLocal >= start;

            if (item.Kind != CameraFileKind.Jpeg)
            {
                // Counted only inside the window: « 2 000 RAW ignorés » on every insert of an old card is noise.
                if (!inWindow)
                {
                    continue;
                }

                switch (item.Kind)
                {
                    case CameraFileKind.Raw: raw++; break;
                    case CameraFileKind.Heic: heic++; break;
                    case CameraFileKind.Video: video++; break;
                    default: other++; break;
                }

                continue;
            }

            if (stateOf(item) is { } state && IsDone(state))
            {
                done++;
                continue;
            }

            if (!inWindow)
            {
                older++;
                continue;
            }

            deliver.Add(item);
        }

        var ordered = deliver
            .OrderBy(i => i.TakenLocal ?? DateTime.MaxValue)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The newest win: what a dentist plugs a camera in for is today's work, never the oldest month on the card.
        if (ordered.Count > MaxDelivered)
        {
            older += ordered.Count - MaxDelivered;
            ordered = ordered.Skip(ordered.Count - MaxDelivered).ToList();
        }

        return new CameraBatchPlan(ordered, older, done, raw, heic, video, other);
    }
}
