using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One arrival/departure pair of a secretary, on the cabinet-local day of its arrival. What the borne recorded is
/// kept as it was; the hours that count may be corrected by an admin, with a motif. No hour is ever invented: a
/// missing departure stays null.
/// </summary>
public class AttendanceEntry : AggregateRoot<Guid>
{
    public const int MaxReasonLength = 500;

    public Guid ClinicId { get; private set; }
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The cabinet-local day of the arrival — the line belongs to it.</summary>
    public DateOnly LocalDay { get; private set; }

    /// <summary>The borne that recorded it — not a foreign key: a borne can be removed, the line stays.</summary>
    public Guid? TerminalId { get; private set; }

    /// <summary>The borne's name when it punched, kept as written.</summary>
    public string? TerminalName { get; private set; }

    public DateTime? PunchedArrivalUtc { get; private set; }
    public DateTime? PunchedDepartureUtc { get; private set; }

    public DateTime ArrivalUtc { get; private set; }
    public DateTime? DepartureUtc { get; private set; }
    public AttendanceDepartureSource? DepartureSource { get; private set; }

    public string? CorrectedByUserId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public DateTime? CorrectedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public bool IsOpen => DepartureUtc is null;
    public bool IsCorrected => CorrectedAt is not null;

    private AttendanceEntry() { } // For EF Core

    /// <summary>An arrival at the borne: opens the line.</summary>
    public static AttendanceEntry Arrive(
        Guid id, Guid clinicId, string userId, DateOnly localDay, Guid terminalId, string terminalName, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        return new AttendanceEntry
        {
            Id = id,
            ClinicId = clinicId,
            UserId = userId,
            LocalDay = localDay,
            TerminalId = terminalId,
            TerminalName = terminalName,
            PunchedArrivalUtc = nowUtc,
            ArrivalUtc = nowUtc,
            CreatedAt = nowUtc,
        };
    }

    /// <summary>« Ajouter un pointage »: a day the borne never saw, entered by an admin with a motif.</summary>
    public static AttendanceEntry RecordByAdmin(
        Guid id, Guid clinicId, string userId, DateOnly localDay, DateTime arrivalUtc, DateTime? departureUtc,
        string reason, string byUserId, DateTime nowUtc)
    {
        if (CorrectionRefusalFor(arrivalUtc, departureUtc, reason) is { } refusal)
            throw new ArgumentException(refusal);

        return new AttendanceEntry
        {
            Id = id,
            ClinicId = clinicId,
            UserId = userId,
            LocalDay = localDay,
            ArrivalUtc = arrivalUtc,
            DepartureUtc = departureUtc,
            DepartureSource = departureUtc is null ? null : AttendanceDepartureSource.CorrectedByAdmin,
            CorrectedByUserId = byUserId,
            CorrectionReason = reason.Trim(),
            CorrectedAt = nowUtc,
            CreatedAt = nowUtc,
        };
    }

    /// <summary>The French refusal for an admin's hours and motif, or null when they are valid.</summary>
    public static string? CorrectionRefusalFor(DateTime arrivalUtc, DateTime? departureUtc, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "Indiquez le motif de la correction.";
        if (reason.Trim().Length > MaxReasonLength)
            return $"Le motif ne peut pas dépasser {MaxReasonLength} caractères.";
        if (departureUtc is { } departure && departure <= arrivalUtc)
            return "Le départ doit être après l'arrivée.";
        return null;
    }

    private static bool SameMinute(DateTime a, DateTime b) =>
        a.Ticks / TimeSpan.TicksPerMinute == b.Ticks / TimeSpan.TicksPerMinute;

    /// <summary>A departure at the borne. Only a line opened the same cabinet day can be closed by a punch.</summary>
    public void Depart(DateTime nowUtc, DateOnly localToday)
    {
        if (!IsOpen)
            throw new InvalidOperationException("Ce pointage a déjà un départ.");
        if (LocalDay != localToday)
            throw new InvalidOperationException("Un pointage d'un autre jour ne se ferme pas à la borne.");

        PunchedDepartureUtc = nowUtc;
        DepartureUtc = nowUtc;
        DepartureSource = AttendanceDepartureSource.Borne;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// « Corriger »: the hours that count change, what the borne recorded does not. The form speaks in minutes, so an
    /// hour given back unchanged keeps its stored seconds — and a departure on the borne's minute stays the borne's.
    /// </summary>
    public void Correct(DateTime arrivalUtc, DateTime? departureUtc, string reason, string byUserId, DateTime nowUtc)
    {
        if (CorrectionRefusalFor(arrivalUtc, departureUtc, reason) is { } refusal)
            throw new ArgumentException(refusal);

        ArrivalUtc = SameMinute(arrivalUtc, ArrivalUtc) ? ArrivalUtc : arrivalUtc;
        DepartureUtc = departureUtc is { } departure
            ? DepartureUtc is { } stored && SameMinute(departure, stored) ? stored
                : PunchedDepartureUtc is { } punched && SameMinute(departure, punched) ? punched
                : departure
            : null;
        DepartureSource = DepartureUtc is null
            ? null
            : DepartureUtc == PunchedDepartureUtc ? AttendanceDepartureSource.Borne : AttendanceDepartureSource.CorrectedByAdmin;
        CorrectedByUserId = byUserId;
        CorrectionReason = reason.Trim();
        CorrectedAt = nowUtc;
        UpdatedAt = nowUtc;
    }
}
