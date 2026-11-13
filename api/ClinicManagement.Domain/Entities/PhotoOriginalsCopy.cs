using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// What one clinic PC last said about copying the full-size camera originals it keeps to the cabinet's archive disk
/// (<c>features/camera-photo-import</c>, storage « deux maisons »).
///
/// <para>⚠️ <b>One row per PC, not one per cabinet.</b> The originals live on whichever PC the camera was plugged
/// into — <see cref="PatientFile.OriginalHeldBy"/> names it — so « are the originals safe? » is a question about each
/// of those PCs. A cabinet-wide stamp would let the reception PC's copy silence the alert for the surgery PC's photos.
/// </para>
///
/// <para>⚠️ <b>The server records when it was TOLD</b>, never the PC's own clock — the coffre report's reason: a PC
/// with a wrong date could park the stamp in the future and silence the alert for ever.</para>
/// </summary>
public class PhotoOriginalsCopy : Entity<Guid>
{
    /// <summary>Originals with no complete copy for this long raise one bell notification. The coffre waits 30 days;
    /// a daily photo habit cannot.</summary>
    public const int StaleAfterDays = 7;

    public Guid ClinicId { get; private set; }

    /// <summary>The PC's name, as <see cref="PatientFile.OriginalHeldBy"/> spells it.</summary>
    public string HeldBy { get; private set; } = string.Empty;

    /// <summary>When a report last covered every original on record for this PC. Null until one did.</summary>
    public DateTime? LastCompleteCopyAtUtc { get; private set; }

    public DateTime LastReportAtUtc { get; private set; }

    public int LastReportedFiles { get; private set; }

    public long LastReportedBytes { get; private set; }

    private PhotoOriginalsCopy() { } // For EF Core

    public static PhotoOriginalsCopy Start(Guid id, Guid clinicId, string heldBy)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        }

        var name = heldBy?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > PatientFile.MaxOriginalHolderLength)
        {
            throw new ArgumentException("Le nom du poste est invalide.", nameof(heldBy));
        }

        return new PhotoOriginalsCopy { Id = id, ClinicId = clinicId, HeldBy = name };
    }

    /// <summary>
    /// Records a report. Only a <paramref name="complete"/> one advances <see cref="LastCompleteCopyAtUtc"/> — a copy
    /// that fell short is remembered (the PC did copy something) and leaves the alert standing.
    /// </summary>
    public void RecordReport(int files, long bytes, bool complete, DateTime nowUtc)
    {
        LastReportAtUtc = nowUtc;
        LastReportedFiles = Math.Max(0, files);
        LastReportedBytes = Math.Max(0, bytes);
        if (complete && (LastCompleteCopyAtUtc is null || nowUtc > LastCompleteCopyAtUtc))
        {
            LastCompleteCopyAtUtc = nowUtc;
        }
    }
}
