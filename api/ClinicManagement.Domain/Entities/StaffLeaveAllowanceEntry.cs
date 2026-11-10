using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One line of a secretary's « Droit à congé »: a rule, a starting balance, or days added or removed. Append-only — a
/// mistake is cancelled with a motif and stays visible struck through; what she has acquired, taken and lost is never
/// stored, it is folded from these lines and her accepted congés by <see cref="StaffLeaveLedger"/>. An aggregate root so
/// every line and every cancellation lands in the audit journal.
/// </summary>
public class StaffLeaveAllowanceEntry : AggregateRoot<Guid>
{
    public const int MaxReasonLength = 500;
    public const int MaxNameLength = 200;

    /// <summary>A month's credit cannot exceed the days in a month.</summary>
    public const decimal MaxMonthlyDays = 31m;

    /// <summary>A year's credit, an opening or an adjustment: the longest congé the product records.</summary>
    public const decimal MaxDays = StaffLeaveRequest.MaxDays;

    public Guid ClinicId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public StaffLeaveAllowanceKind Kind { get; private set; }

    /// <summary>In halves. Rule: credited each period · Opening: the balance on <see cref="OnDay"/> · Adjustment: ±.</summary>
    public decimal Days { get; private set; }

    /// <summary>Rules only.</summary>
    public StaffLeaveAccrual? Accrual { get; private set; }

    /// <summary>Rules only: false = days not taken by 31 December are lost.</summary>
    public bool CarryOver { get; private set; }

    /// <summary>Rule: its first credit day (1 January or the 1st of a month) · Opening / Adjustment: the day it holds.</summary>
    public DateOnly OnDay { get; private set; }

    /// <summary>Required on an adjustment.</summary>
    public string? Reason { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }
    public string? CancelledByName { get; private set; }
    public string? CancelReason { get; private set; }

    public bool IsCancelled => CancelledAt is not null;

    private StaffLeaveAllowanceEntry() { } // For EF Core

    private static bool InHalves(decimal days) => days * 2 == decimal.Truncate(days * 2);

    /// <summary>The French refusal for a rule, or null.</summary>
    public static string? RuleRefusalFor(decimal days, StaffLeaveAccrual accrual, DateOnly since)
    {
        if (!Enum.IsDefined(accrual))
            return "Choisissez « par an » ou « par mois ».";
        var max = accrual == StaffLeaveAccrual.Month ? MaxMonthlyDays : MaxDays;
        if (days < 0 || days > max || !InHalves(days))
            return $"Le nombre de jours se compte par demi-journée, entre 0 et {max:0.#} jours.";
        if (accrual == StaffLeaveAccrual.Year && (since.Month != 1 || since.Day != 1))
            return "Un droit par an commence un 1er janvier.";
        if (accrual == StaffLeaveAccrual.Month && since.Day != 1)
            return "Un droit par mois commence le 1er d'un mois.";
        return null;
    }

    /// <summary>The French refusal for a starting balance, or null.</summary>
    public static string? OpeningRefusalFor(decimal days) =>
        days < 0 || days > MaxDays || !InHalves(days)
            ? $"Le solde de départ se compte par demi-journée, entre 0 et {MaxDays:0.#} jours."
            : null;

    /// <summary>The French refusal for days added or removed, or null.</summary>
    public static string? AdjustmentRefusalFor(decimal days, string? reason)
    {
        if (days == 0 || Math.Abs(days) > MaxDays || !InHalves(days))
            return $"Ajoutez ou retirez des jours par demi-journée (au plus {MaxDays:0.#}).";
        if (string.IsNullOrWhiteSpace(reason))
            return "Indiquez le motif.";
        if (reason.Trim().Length > MaxReasonLength)
            return $"Le motif ne peut pas dépasser {MaxReasonLength} caractères.";
        return null;
    }

    public static StaffLeaveAllowanceEntry Rule(
        Guid id, Guid clinicId, string userId, decimal days, StaffLeaveAccrual accrual, bool carryOver, DateOnly since,
        string byUserId, string byName, DateTime nowUtc)
    {
        if (RuleRefusalFor(days, accrual, since) is { } refusal)
            throw new ArgumentException(refusal, nameof(days));
        var entry = New(id, clinicId, userId, StaffLeaveAllowanceKind.Rule, days, since, byUserId, byName, nowUtc);
        entry.Accrual = accrual;
        entry.CarryOver = carryOver;
        return entry;
    }

    public static StaffLeaveAllowanceEntry Opening(
        Guid id, Guid clinicId, string userId, decimal days, DateOnly onDay, string byUserId, string byName, DateTime nowUtc)
    {
        if (OpeningRefusalFor(days) is { } refusal)
            throw new ArgumentException(refusal, nameof(days));
        return New(id, clinicId, userId, StaffLeaveAllowanceKind.Opening, days, onDay, byUserId, byName, nowUtc);
    }

    public static StaffLeaveAllowanceEntry Adjustment(
        Guid id, Guid clinicId, string userId, decimal days, DateOnly onDay, string reason, string byUserId, string byName,
        DateTime nowUtc)
    {
        if (AdjustmentRefusalFor(days, reason) is { } refusal)
            throw new ArgumentException(refusal, nameof(days));
        var entry = New(id, clinicId, userId, StaffLeaveAllowanceKind.Adjustment, days, onDay, byUserId, byName, nowUtc);
        entry.Reason = reason.Trim();
        return entry;
    }

    private static StaffLeaveAllowanceEntry New(
        Guid id, Guid clinicId, string userId, StaffLeaveAllowanceKind kind, decimal days, DateOnly onDay,
        string byUserId, string byName, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Un droit à congé appartient à un compte.", nameof(userId));
        return new StaffLeaveAllowanceEntry
        {
            Id = id,
            ClinicId = clinicId,
            UserId = userId,
            Kind = kind,
            Days = days,
            OnDay = onDay,
            CreatedByUserId = byUserId,
            CreatedByName = Clip(byName),
            CreatedAt = nowUtc,
        };
    }

    /// <summary>Struck through, never deleted: every balance it fed recomputes on the next read.</summary>
    public void Cancel(string reason, string byName, DateTime nowUtc)
    {
        if (IsCancelled)
            throw new InvalidOperationException("Cette ligne est déjà annulée.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Indiquez le motif de l'annulation.", nameof(reason));
        CancelledAt = nowUtc;
        CancelledByName = Clip(byName);
        CancelReason = reason.Trim().Length > MaxReasonLength ? reason.Trim()[..MaxReasonLength] : reason.Trim();
    }

    public StaffLeaveLedgerEntry ToLedgerEntry() =>
        new(Id, Kind, Days, Accrual, CarryOver, OnDay, CreatedAt, IsCancelled);

    private static string Clip(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }
}
