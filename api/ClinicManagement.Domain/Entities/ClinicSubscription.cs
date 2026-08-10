using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A cabinet's right to record new work, as a dated entitlement (<c>clinic-subscription</c> FR-1). One row per
/// clinic; <see cref="EndsOn"/> is a denormalised full re-fold of the cabinet's <see cref="SubscriptionPeriod"/>
/// ledger, so the gate on the hot path reads one indexed row instead of a ledger.
///
/// <para><b><see cref="RecomputeFrom"/> is the only writer of <see cref="EndsOn"/>, including the trial's.</b>
/// Three ways that date can drift and all three are designed out here: an incremental <c>EndsOn += duration</c>
/// would make cancelling any but the latest entry change no date (AC-5.4); a fold that took « today » would move
/// unrelated dates on every recomputation; and a <i>second place that computes a date at all</i> — a trial end
/// written directly at provisioning — disagrees with its own fold by one day and makes
/// <c>verify-schema</c>'s <c>subscription-end-date-matches-ledger</c> red on every new cabinet.</para>
///
/// <para><b>An <see cref="AggregateRoot{TId}"/> for the audit ledger's sake</b> (FR-12) — the interceptor writes
/// one row per mutated root, so a non-root would leave every grant and suspension unattributed.</para>
///
/// <para>⚠️ <b>The state is not stored.</b> <see cref="SubscriptionState"/> is derived from this row and the
/// clinic's own today; a stored copy would be a fourth thing able to disagree with the other three, and it would
/// have to change at midnight with no write to change it.</para>
/// </summary>
public class ClinicSubscription : AggregateRoot<Guid>
{
    public const int MaxSuspensionReasonLength = 500;
    public const int MaxActorLength = 200;

    public Guid ClinicId { get; private set; }

    /// <summary>
    /// The forfait, or null when none has been chosen — which is the ordinary state of a cabinet on its free days
    /// and of every grandfathered one. A far-cheaper honest null than a default that reads as a commercial choice
    /// nobody made; it gates nothing either way (FR-10).
    /// </summary>
    public SubscriptionPlan? Plan { get; private set; }

    /// <summary>
    /// The <b>inclusive</b> last clinic-local day on which new work may be recorded, or null for « sans échéance ».
    /// A calendar day, not an instant: the cabinet may work all of this day (AC-1.1).
    /// </summary>
    public DateTime? EndsOn { get; private set; }

    /// <summary>Stopped by the vendor. Outranks an expiry when both are true — EC-11 requires « Suspendu ».</summary>
    public bool IsSuspended { get; private set; }

    public string? SuspensionReason { get; private set; }

    public DateTime? SuspendedAtUtc { get; private set; }

    public string? SuspendedBy { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private ClinicSubscription() { } // For EF Core

    /// <summary>
    /// Creates the entitlement with <b>no</b> end date, for the caller to fold its opening entry in immediately.
    /// Deliberately not « create with an end date »: that would be the second date-computing site the class note
    /// above exists to prevent.
    /// </summary>
    public static ClinicSubscription For(Guid clinicId, DateTime createdAtUtc, SubscriptionPlan? plan = null)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant du cabinet est requis.", nameof(clinicId));
        }

        return new ClinicSubscription
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Plan = plan,
            CreatedAt = createdAtUtc
        };
    }

    /// <summary>
    /// Re-folds the <b>whole</b> ledger onto <see cref="EndsOn"/> — the single write path for that date.
    ///
    /// <para>⚠️ A fold over a subset is not a fold: cancelling a <i>middle</i> entry has to be able to move the end
    /// date (AC-5.4), which is only true when every non-cancelled entry is present. Callers pass the cabinet's
    /// entire ledger, never a page of it.</para>
    /// </summary>
    public void RecomputeFrom(IEnumerable<SubscriptionPeriod> wholeLedger)
    {
        ArgumentNullException.ThrowIfNull(wholeLedger);

        var entries = wholeLedger.ToList();
        if (entries.Any(e => e.ClinicId != ClinicId))
        {
            throw new InvalidOperationException(
                "Le journal d'abonnement fourni contient une période appartenant à un autre cabinet.");
        }

        EndsOn = SubscriptionLedger.Fold(entries.Select(e => e.ToLedgerEntry()));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Stops the cabinet recording work regardless of its end date, for a stated reason (FR-7).
    ///
    /// <para>The reason is mandatory because <see cref="SubscriptionState.Suspended"/> deliberately outranks
    /// <see cref="SubscriptionState.Expired"/> (EC-11) — the cabinet is told it is suspended rather than lapsed, so
    /// « suspended why? » has to be answerable or the practice has nothing to act on. Suspension does not touch the
    /// ledger: paying does not lift it, and lifting it does not extend the entitlement.</para>
    /// </summary>
    public void Suspend(string reason, string? by, DateTime whenUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif de suspension est obligatoire.", nameof(reason));
        }

        IsSuspended = true;
        SuspensionReason = reason.Trim().Length > MaxSuspensionReasonLength
            ? throw new ArgumentException(
                $"Le motif de suspension dépasse {MaxSuspensionReasonLength} caractères.", nameof(reason))
            : reason.Trim();
        SuspendedBy = by?.Trim();
        SuspendedAtUtc = whenUtc;
        UpdatedAt = whenUtc;
    }

    /// <summary>
    /// Lifts a suspension, clearing its whole trail. The cabinet then stands on its end date alone — which may
    /// still be in the past, so unsuspending is not the same as granting time.
    /// </summary>
    public void Unsuspend(DateTime whenUtc)
    {
        IsSuspended = false;
        SuspensionReason = null;
        SuspendedBy = null;
        SuspendedAtUtc = null;
        UpdatedAt = whenUtc;
    }
}
