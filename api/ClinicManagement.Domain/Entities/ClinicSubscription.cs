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

    /// <summary>
    /// Why the <b>last non-cancelled entry</b> of the ledger covers this cabinet — a denormalisation of
    /// <see cref="SubscriptionFold.LatestCoverKind"/>, written by <see cref="RecomputeFrom"/> and by nothing else,
    /// exactly as <see cref="EndsOn"/> is. Null for a cabinet whose every entry has been cancelled.
    ///
    /// <para><b>It exists so « en essai » can be a SQL predicate.</b> The vendor console filters and sorts the whole
    /// portfolio <i>before</i> a page is cut (<c>platform-console</c> AC-2.4a), and folding N cabinets' ledgers to
    /// answer that is precisely the unbounded read EC-11 forbids.</para>
    ///
    /// <para>⚠️ <b>The obvious column — « is the cover in force <i>today</i> the trial? » — is unstorable</b>, which
    /// is why this one is shaped as it is. That question is a function of the ledger <b>and of today</b>, while
    /// <see cref="RecomputeFrom"/> is deliberately clock-free (see <see cref="SubscriptionLedger"/>), so a stored
    /// answer would be correct only until the next midnight and would need a daily pass to stay true —
    /// reintroducing exactly the staleness the fold is designed to avoid. This is a pure function of the ledger, so
    /// <c>verify-schema</c> can re-derive it instead of trusting it.</para>
    /// </summary>
    public SubscriptionPeriodKind? LatestCoverKind { get; private set; }

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
    public void RecomputeFrom(IEnumerable<SubscriptionPeriod> wholeLedger, DateTime whenUtc)
    {
        ArgumentNullException.ThrowIfNull(wholeLedger);

        var entries = wholeLedger.ToList();
        if (entries.Any(e => e.ClinicId != ClinicId))
        {
            throw new InvalidOperationException(
                "Le journal d'abonnement fourni contient une période appartenant à un autre cabinet.");
        }

        // One fold, two denormalisations. Reading the kind from a second pass over the entries here would be a
        // second ordering of the ledger, and the fold's own `RecordedAtUtc` then `Id` must exist exactly once.
        var fold = SubscriptionLedger.FoldWithSpans(entries.Select(e => e.ToLedgerEntry()));
        EndsOn = fold.EndsOn;
        LatestCoverKind = fold.LatestCoverKind;
        UpdatedAt = whenUtc;
    }

    /// <summary>
    /// Records the forfait the vendor sells against (AC-5.1's optional plan). A label and a price; it gates
    /// nothing (FR-10), so this never touches <see cref="EndsOn"/> and no grant depends on it being set.
    /// </summary>
    public void SetPlan(SubscriptionPlan plan, DateTime whenUtc)
    {
        Plan = plan;
        UpdatedAt = whenUtc;
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

        // Every refusal before the first assignment: an over-long value used to leave the cabinet suspended with a
        // null motif — exactly the state the mandatory-reason rule exists to prevent.
        var trimmedReason = Trimmed(reason, MaxSuspensionReasonLength, "Le motif de suspension", nameof(reason));
        var trimmedActor = Trimmed(by, MaxActorLength, "L'auteur de la suspension", nameof(by));

        IsSuspended = true;
        SuspensionReason = trimmedReason;
        SuspendedBy = trimmedActor;
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

    /// <summary>Refuses in French what the column would otherwise refuse as a 500, `SubscriptionPeriod.Trimmed`'s job.</summary>
    private static string? Trimmed(string? value, int maxLength, string subject, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? throw new ArgumentException($"{subject} dépasse {maxLength} caractères.", parameterName)
            : trimmed;
    }
}
