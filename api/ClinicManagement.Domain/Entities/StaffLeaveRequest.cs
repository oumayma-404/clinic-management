using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A secretary's congé: clinic-local days, <see cref="StartDate"/> to <see cref="EndDate"/> inclusive, asked for by
/// her and decided by an admin — or recorded directly by an admin, already accepted.
/// </summary>
public class StaffLeaveRequest : AggregateRoot<Guid>
{
    public const int MaxDays = 366;
    public const int MaxReasonLength = 500;

    private static readonly IReadOnlyDictionary<StaffLeaveStatus, StaffLeaveStatus[]> AllowedTransitions =
        new Dictionary<StaffLeaveStatus, StaffLeaveStatus[]>
        {
            [StaffLeaveStatus.Pending] = [StaffLeaveStatus.Approved, StaffLeaveStatus.Rejected, StaffLeaveStatus.Cancelled],
            [StaffLeaveStatus.Approved] = [StaffLeaveStatus.Cancelled],
            [StaffLeaveStatus.Rejected] = [],
            [StaffLeaveStatus.Cancelled] = [],
        };

    public Guid ClinicId { get; private set; }

    /// <summary>The account the congé belongs to (<see cref="User.Id"/>).</summary>
    public string UserId { get; private set; } = string.Empty;

    public StaffLeaveType Type { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>Half a day; only ever true on a one-day congé.</summary>
    public bool HalfDay { get; private set; }

    public string Reason { get; private set; } = string.Empty;
    public StaffLeaveStatus Status { get; private set; }

    /// <summary>The cabinet's open days in the range when it was asked for — a snapshot, never recomputed.</summary>
    public decimal RequestedDays { get; private set; }

    /// <summary>What the admin accepted; only this number counts against the allowance.</summary>
    public decimal? CountedDays { get; private set; }

    public string? DecidedByUserId { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancelledByUserId { get; private set; }

    /// <summary>Recorded by an admin (« Ajouter une absence ») rather than asked for.</summary>
    public bool EnteredByAdmin { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>A request that still holds its days: waiting, or accepted.</summary>
    public bool HoldsItsDays => Status is StaffLeaveStatus.Pending or StaffLeaveStatus.Approved;

    private StaffLeaveRequest() { } // For EF Core

    /// <summary>A request the secretary sends, waiting for a decision.</summary>
    public StaffLeaveRequest(
        Guid id, Guid clinicId, string userId, StaffLeaveType type, DateOnly startDate, DateOnly endDate,
        bool halfDay, string reason, decimal requestedDays)
    {
        if (RefusalFor(startDate, endDate, halfDay, reason) is { } refusal)
            throw new ArgumentException(refusal);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Le compte du congé est requis.", nameof(userId));
        if (requestedDays < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedDays));

        Id = id;
        ClinicId = clinicId;
        UserId = userId;
        Type = type;
        StartDate = startDate;
        EndDate = endDate;
        HalfDay = halfDay;
        Reason = reason.Trim();
        RequestedDays = requestedDays;
        Status = StaffLeaveStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>« Ajouter une absence »: an admin records it, accepted at once and counted as asked.</summary>
    public static StaffLeaveRequest RecordByAdmin(
        Guid id, Guid clinicId, string userId, StaffLeaveType type, DateOnly startDate, DateOnly endDate,
        bool halfDay, string reason, decimal days, string adminUserId)
    {
        var leave = new StaffLeaveRequest(id, clinicId, userId, type, startDate, endDate, halfDay, reason, days)
        {
            EnteredByAdmin = true,
        };
        leave.Approve(adminUserId, days);
        return leave;
    }

    /// <summary>The French refusal for a request this type would not accept, or null when it is valid.</summary>
    public static string? RefusalFor(DateOnly startDate, DateOnly endDate, bool halfDay, string? reason)
    {
        if (RangeRefusalFor(startDate, endDate) is { } range)
            return range;
        if (halfDay && startDate != endDate)
            return "Une demi-journée ne peut porter que sur un seul jour.";
        if (string.IsNullOrWhiteSpace(reason))
            return "Le motif est requis.";
        if (reason.Trim().Length > MaxReasonLength)
            return $"Le motif ne peut pas dépasser {MaxReasonLength} caractères.";
        return null;
    }

    /// <summary>The French refusal for the dates alone, or null — the form's preview asks this before a motif exists.</summary>
    public static string? RangeRefusalFor(DateOnly startDate, DateOnly endDate)
    {
        // A JSON body without the dates binds them to 01/01/0001 rather than failing.
        if (startDate == default || endDate == default)
            return "Les dates du congé sont requises.";
        if (endDate < startDate)
            return "Le dernier jour doit être le même jour ou après le premier.";
        if (endDate.DayNumber - startDate.DayNumber + 1 > MaxDays)
            return $"Un congé ne peut pas dépasser {MaxDays} jours — vérifiez l'année saisie.";
        return null;
    }

    /// <summary>The French refusal for a refusal motif, or null.</summary>
    public static string? RejectionRefusalFor(string? rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
            return "Indiquez le motif du refus.";
        if (rejectionReason.Trim().Length > MaxReasonLength)
            return $"Le motif du refus ne peut pas dépasser {MaxReasonLength} caractères.";
        return null;
    }

    /// <summary>The French refusal for an accepted count, or null — steps of 0.5, never negative.</summary>
    public static string? CountRefusalFor(decimal days)
    {
        if (days < 0 || days > MaxDays)
            return "Le nombre de jours décomptés n'est pas valide.";
        if (days * 2 != decimal.Truncate(days * 2))
            return "Le nombre de jours décomptés se compte par demi-journée (0,5).";
        return null;
    }

    public bool CanMoveTo(StaffLeaveStatus next) => AllowedTransitions[Status].Contains(next);

    /// <summary>Whether it may still be withdrawn on <paramref name="clinicToday"/>: waiting, or accepted and not begun.</summary>
    public bool CanBeCancelledOn(DateOnly clinicToday) =>
        Status == StaffLeaveStatus.Pending || (Status == StaffLeaveStatus.Approved && clinicToday < StartDate);

    /// <summary>An absence the admin recorded is withdrawn by an admin only; anything else by its owner too.</summary>
    public bool CanBeCancelledBy(bool byAdmin, DateOnly clinicToday) =>
        (byAdmin || !EnteredByAdmin) && CanBeCancelledOn(clinicToday);

    public void Approve(string adminUserId, decimal countedDays)
    {
        if (CountRefusalFor(countedDays) is { } refusal)
            throw new ArgumentException(refusal);
        MoveTo(StaffLeaveStatus.Approved);
        CountedDays = countedDays;
        DecidedByUserId = adminUserId;
        DecidedAt = DateTime.UtcNow;
    }

    public void Reject(string adminUserId, string rejectionReason)
    {
        if (RejectionRefusalFor(rejectionReason) is { } refusal)
            throw new ArgumentException(refusal, nameof(rejectionReason));
        MoveTo(StaffLeaveStatus.Rejected);
        RejectionReason = rejectionReason.Trim();
        DecidedByUserId = adminUserId;
        DecidedAt = DateTime.UtcNow;
    }

    public void Cancel(string byUserId, DateOnly clinicToday)
    {
        if (!CanBeCancelledOn(clinicToday))
            throw new InvalidOperationException("Ce congé a commencé ou est passé : il ne peut plus être annulé.");
        MoveTo(StaffLeaveStatus.Cancelled);
        CancelledAt = DateTime.UtcNow;
        CancelledByUserId = byUserId;
    }

    private void MoveTo(StaffLeaveStatus next)
    {
        if (!CanMoveTo(next))
            throw new InvalidOperationException($"Une demande « {Status} » ne peut pas passer à « {next} ».");
        Status = next;
        UpdatedAt = DateTime.UtcNow;
    }
}
