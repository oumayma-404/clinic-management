using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One entry of a cabinet's append-only subscription ledger: a stretch of time it is entitled to, and where that
/// entitlement came from. <see cref="ClinicSubscription.EndsOn"/> is a full re-fold of these (FR-1).
///
/// <para><b>Append-only, and correctable only by cancellation.</b> There is no mutator for the duration, the
/// amount or the kind: a mistaken entry is <see cref="Cancel"/>led with a written reason, stays visible struck
/// through, and the date recomputes — possibly into the past (AC-5.5, EC-4). Editing one in place would make
/// « what were we paid, and for what » unanswerable, and deleting one would make it unaskable.</para>
///
/// <para><b>An <see cref="AggregateRoot{TId}"/> rather than a child of the entitlement</b>, and not for tidiness:
/// <c>AuditSaveChangesInterceptor</c> writes one row per mutated <i>aggregate root</i>, so as a plain entity a
/// grant and a cancellation would produce no audit row at all and FR-12 would be silently false.</para>
///
/// <para>⚠️ <b>Exactly one duration form, or none.</b> <see cref="DurationMonths"/>,
/// <see cref="DurationDays"/> and <see cref="ExplicitEndsOn"/> are mutually exclusive, and all three absent means
/// open-ended. Two of them set is not a longer period — it is a row the fold would have to guess at.</para>
/// </summary>
public class SubscriptionPeriod : AggregateRoot<Guid>
{
    public const int MaxReferenceLength = 200;
    public const int MaxNoteLength = 1000;
    public const int MaxCancelReasonLength = 500;
    public const int MaxActorLength = 200;

    /// <summary>The cabinet this entry entitles. Denormalised beside the entitlement's so both are filtered.</summary>
    public Guid ClinicId { get; private set; }

    public SubscriptionPeriodKind Kind { get; private set; }

    /// <summary>Whole calendar months, clamped by <c>AddMonths</c> (31 Jan + 1 month → 28/29 Feb — FR-2, EC-3).</summary>
    public int? DurationMonths { get; private set; }

    /// <summary>Whole days. The trial's form: 30 days counting the creation day as day 1 (AC-1.1).</summary>
    public int? DurationDays { get; private set; }

    /// <summary>An inclusive last day named outright, for the cases a duration cannot express.</summary>
    public DateTime? ExplicitEndsOn { get; private set; }

    /// <summary>What the vendor was paid. ⚠️ Never the clinic's money — FR-2; see <see cref="SubscriptionPaymentMethod"/>.</summary>
    public decimal? Amount { get; private set; }

    public SubscriptionPaymentMethod? Method { get; private set; }

    /// <summary>The transfer reference, cheque number or receipt number, as the vendor recorded it.</summary>
    public string? Reference { get; private set; }

    /// <summary>Free text. For a <see cref="SubscriptionPeriodKind.Grandfathered"/> entry this carries AC-6.2's reason.</summary>
    public string? Note { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>
    /// The clinic-local day this entry was recorded on — the fold's anchor, and the reason the fold needs no clock.
    /// Stored as a date, never converted: it is a calendar day, not an instant.
    /// </summary>
    public DateTime RecordedOnClinicDay { get; private set; }

    /// <summary>Who recorded it: a user id, or <c>job|&lt;command&gt;</c> for a console verb (FR-12).</summary>
    public string? RecordedBy { get; private set; }

    public bool IsCancelled { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private SubscriptionPeriod() { } // For EF Core

    /// <summary>
    /// Records an entry. <paramref name="recordedOnClinicDay"/> is the caller's clinic-local today (through
    /// <c>ClinicClock</c>), which is what makes the fold reproduce AC-5.2 without reading a clock itself.
    /// </summary>
    public static SubscriptionPeriod Create(
        Guid clinicId,
        SubscriptionPeriodKind kind,
        DateTime recordedOnClinicDay,
        DateTime recordedAtUtc,
        int? durationMonths = null,
        int? durationDays = null,
        DateTime? explicitEndsOn = null,
        decimal? amount = null,
        SubscriptionPaymentMethod? method = null,
        string? reference = null,
        string? note = null,
        string? recordedBy = null)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant du cabinet est requis.", nameof(clinicId));
        }

        var forms = (durationMonths.HasValue ? 1 : 0)
                    + (durationDays.HasValue ? 1 : 0)
                    + (explicitEndsOn.HasValue ? 1 : 0);

        if (forms > 1)
        {
            throw new ArgumentException(
                "Une période d'abonnement porte une seule durée : un nombre de mois, un nombre de jours, "
                + "une date de fin explicite, ou aucune (sans échéance).",
                nameof(durationMonths));
        }

        if (durationMonths is <= 0)
        {
            throw new ArgumentException("La durée en mois doit être positive.", nameof(durationMonths));
        }

        if (durationDays is <= 0)
        {
            throw new ArgumentException("La durée en jours doit être positive.", nameof(durationDays));
        }

        if (amount is < 0)
        {
            throw new ArgumentException("Le montant ne peut pas être négatif.", nameof(amount));
        }

        return new SubscriptionPeriod
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Kind = kind,
            DurationMonths = durationMonths,
            DurationDays = durationDays,
            ExplicitEndsOn = explicitEndsOn?.Date,
            Amount = amount,
            Method = method,
            Reference = Trimmed(reference, MaxReferenceLength, nameof(reference)),
            Note = Trimmed(note, MaxNoteLength, nameof(note)),
            RecordedAtUtc = recordedAtUtc,
            RecordedOnClinicDay = recordedOnClinicDay.Date,
            RecordedBy = Trimmed(recordedBy, MaxActorLength, nameof(recordedBy)),
            CreatedAt = recordedAtUtc
        };
    }

    /// <summary>
    /// The free days a new cabinet arrives with (AC-1.1). Expressed as a <b>duration</b>, not as a computed end
    /// date, so the trial's own date comes out of the fold like every other — see <see cref="SubscriptionLedger"/>.
    /// </summary>
    public static SubscriptionPeriod Trial(
        Guid clinicId, DateTime recordedOnClinicDay, int trialDays, DateTime recordedAtUtc) =>
        Create(
            clinicId,
            SubscriptionPeriodKind.Trial,
            recordedOnClinicDay,
            recordedAtUtc,
            durationDays: trialDays,
            note: $"Essai gratuit de {trialDays} jours, sans carte bancaire.");

    /// <summary>
    /// An entry with no end date. Both callers matter: a cabinet grandfathered by AC-6.1, and a cabinet on a
    /// deployment where subscriptions are not enforced — which is how FR-13's « no cabinet without an entitlement »
    /// holds in all three topologies while nothing can expire in two of them.
    /// </summary>
    public static SubscriptionPeriod OpenEnded(
        Guid clinicId,
        SubscriptionPeriodKind kind,
        DateTime recordedOnClinicDay,
        DateTime recordedAtUtc,
        string? note = null,
        string? recordedBy = null) =>
        Create(clinicId, kind, recordedOnClinicDay, recordedAtUtc, note: note, recordedBy: recordedBy);

    /// <summary>
    /// Voids this entry with a written reason, keeping the row. The reason is mandatory because the end date can
    /// move into the past as a result (EC-4), and « why is this cabinet suddenly read-only » must be answerable.
    /// </summary>
    public void Cancel(string reason, string? by, DateTime whenUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif d'annulation est obligatoire.", nameof(reason));
        }

        if (IsCancelled)
        {
            throw new InvalidOperationException("Cette période d'abonnement est déjà annulée.");
        }

        IsCancelled = true;
        CancelReason = Trimmed(reason, MaxCancelReasonLength, nameof(reason));
        CancelledBy = Trimmed(by, MaxActorLength, nameof(by));
        CancelledAtUtc = whenUtc;
    }

    /// <summary>No duration of any kind — cover with no end date.</summary>
    public bool IsOpenEnded =>
        DurationMonths is null && DurationDays is null && ExplicitEndsOn is null;

    /// <summary>Projects onto the fold's input. The one bridge between the entity and the arithmetic.</summary>
    public SubscriptionLedgerEntry ToLedgerEntry() =>
        new(Id, RecordedOnClinicDay, RecordedAtUtc, DurationMonths, DurationDays, ExplicitEndsOn, IsCancelled);

    private static string? Trimmed(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"La valeur dépasse {maxLength} caractères.", parameterName);
        }

        return trimmed;
    }
}
