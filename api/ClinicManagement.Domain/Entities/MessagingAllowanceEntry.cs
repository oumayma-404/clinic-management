using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One entry of a cabinet's append-only WhatsApp reminder allocation ledger: a figure, the Tunisian month it takes
/// effect in, and what the vendor was paid for it (FR-2, AC-6.2). Every month's
/// <see cref="ClinicMessagingMonth.AllowanceMessages"/> is a full re-fold of these.
///
/// <para><b>Append-only, and correctable only by cancellation.</b> There is no mutator for the figure, the month,
/// the amount or the kind: a mistaken entry is <see cref="Cancel"/>led with a written motif, stays visible struck
/// through, and every month it fed recomputes (AC-7.1, AC-7.2, AC-7.4). Editing one in place would make « what were
/// we paid, and for what » unanswerable; deleting one would make it unaskable.</para>
///
/// <para><b>An <see cref="AggregateRoot{TId}"/> rather than a child</b>, and not for tidiness:
/// <c>AuditSaveChangesInterceptor</c> writes one row per mutated <i>aggregate root</i>, so as a plain entity a grant
/// and a cancellation would produce no audit row at all. It is deliberately the opposite decision from
/// <see cref="ClinicMessagingMonth"/> beside it (D-6) — the <b>entry</b> is the audited artefact, the month row is a
/// derived counter written minutely.</para>
///
/// <para>⚠️ <b><see cref="EffectiveMonth"/> is an <c>AAAA-MM</c> string end to end</b> (D-7). Zero-padded, so
/// lexicographic ordering <i>is</i> chronological ordering and « effective month ≤ M » needs no conversion in SQL,
/// in the fold, or in a console argument.</para>
/// </summary>
public class MessagingAllowanceEntry : AggregateRoot<Guid>
{
    public const int MonthKeyLength = 7;
    public const int MaxReferenceLength = 200;
    public const int MaxNoteLength = 1000;
    public const int MaxCancelReasonLength = 500;
    public const int MaxActorLength = 200;

    /// <summary>A guard on the vendor's figure, not a policy: a million messages a month is a typo.</summary>
    public const int MaxMessages = 1_000_000;

    /// <summary>The cabinet this entry allocates to.</summary>
    public Guid ClinicId { get; private set; }

    public MessagingAllowanceKind Kind { get; private set; }

    /// <summary>
    /// The figure. For a <see cref="MessagingAllowanceKind.Standing"/> entry it <b>replaces</b> the monthly
    /// allowance; for a <see cref="MessagingAllowanceKind.TopUp"/> it is <b>added</b> to that month alone.
    ///
    /// <para><b>Zero is legal and meaningful</b> for a standing entry — « this cabinet sends no WhatsApp reminders »
    /// — and is not the same state as having no entry at all, which is FR-4's second branch and reads as our own
    /// bookkeeping fault (AC-4.3).</para>
    /// </summary>
    public int Messages { get; private set; }

    /// <summary>
    /// The Tunisian month this entry starts applying in, as <c>AAAA-MM</c>. Decided by the <b>server</b> for a
    /// standing entry (AC-6.4a) and named by the vendor for a top-up (AC-6.5).
    /// </summary>
    public string EffectiveMonth { get; private set; } = string.Empty;

    /// <summary>
    /// What the vendor was paid, or <b>null</b> for a complimentary allocation (AC-6.6). Null rather than zero: an
    /// amount of 0,000 DT reads as a transaction that happened for nothing.
    ///
    /// <para>⚠️ Never the clinic's money (FR-2) — nothing here reaches an invoice, la caisse, « Créances », the
    /// dashboard's Argent section or a patient's balance.</para>
    /// </summary>
    public decimal? Amount { get; private set; }

    /// <summary>How the vendor was paid. Reuses the vendor's own enum, never the clinic's <c>PaymentMethod</c>.</summary>
    public SubscriptionPaymentMethod? Method { get; private set; }

    /// <summary>The transfer reference, cheque number or receipt number, as the vendor recorded it.</summary>
    public string? Reference { get; private set; }

    /// <summary>Free text. The rollout backfill's entry carries FR-3's reason here.</summary>
    public string? Note { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>
    /// Who recorded it: a user id, <c>console|&lt;accountId&gt;</c> for the vendor console, or
    /// <c>job|&lt;command&gt;</c> for a console verb.
    /// </summary>
    public string? RecordedBy { get; private set; }

    public bool IsCancelled { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private MessagingAllowanceEntry() { } // For EF Core

    /// <summary>
    /// Records an allocation. <paramref name="effectiveMonth"/> is an <c>AAAA-MM</c> key the <b>caller</b> has
    /// already decided (through <c>MessagingAllowanceLedger.EffectiveMonthFor</c> for a standing figure, or the
    /// vendor's own <c>--month</c> for a top-up), because deciding it needs the ledger and this entity holds one row.
    /// </summary>
    public static MessagingAllowanceEntry Create(
        Guid clinicId,
        MessagingAllowanceKind kind,
        int messages,
        string effectiveMonth,
        DateTime recordedAtUtc,
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

        if (messages < 0)
        {
            throw new ArgumentException("Le nombre de rappels ne peut pas être négatif.", nameof(messages));
        }

        if (messages > MaxMessages)
        {
            throw new ArgumentException(
                $"Le nombre de rappels ne peut pas dépasser {MaxMessages}.", nameof(messages));
        }

        // A top-up of nothing is not an allocation, and it would be indistinguishable on screen from one the
        // vendor meant to make. A *standing* zero is a real decision and is allowed above.
        if (kind == MessagingAllowanceKind.TopUp && messages == 0)
        {
            throw new ArgumentException(
                "Un forfait supplémentaire doit porter au moins un rappel.", nameof(messages));
        }

        if (amount is < 0)
        {
            throw new ArgumentException("Le montant ne peut pas être négatif.", nameof(amount));
        }

        return new MessagingAllowanceEntry
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Kind = kind,
            Messages = messages,
            EffectiveMonth = ValidMonthKey(effectiveMonth, nameof(effectiveMonth)),
            Amount = amount,
            Method = method,
            Reference = Trimmed(reference, MaxReferenceLength, nameof(reference)),
            Note = Trimmed(note, MaxNoteLength, nameof(note)),
            RecordedAtUtc = recordedAtUtc,
            RecordedBy = Trimmed(recordedBy, MaxActorLength, nameof(recordedBy)),
            CreatedAt = recordedAtUtc
        };
    }

    /// <summary>
    /// The standing figure a cabinet is provisioned with (FR-3) and the one the rollout backfill wrote. Named so
    /// the two doors that create a cabinet cannot disagree about what its first entry looks like.
    /// </summary>
    public static MessagingAllowanceEntry Provisioned(
        Guid clinicId, int messagesPerMonth, string effectiveMonth, DateTime recordedAtUtc, string? recordedBy = null) =>
        Create(
            clinicId,
            MessagingAllowanceKind.Standing,
            messagesPerMonth,
            effectiveMonth,
            recordedAtUtc,
            note: $"Forfait de rappels WhatsApp à l'ouverture du cabinet : {messagesPerMonth} par mois.",
            recordedBy: recordedBy);

    /// <summary>
    /// Voids this entry with a written motif, keeping the row (AC-7.1, AC-7.2). The motif is mandatory because
    /// every month the entry fed recomputes as a result — including the current one, possibly to « épuisé »
    /// (AC-7.4) — and « why did this cabinet's forfait shrink » must be answerable.
    /// </summary>
    public void Cancel(string reason, string? by, DateTime whenUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif d'annulation est obligatoire.", nameof(reason));
        }

        if (IsCancelled)
        {
            throw new InvalidOperationException("Cette allocation de forfait est déjà annulée.");
        }

        IsCancelled = true;
        CancelReason = Trimmed(reason, MaxCancelReasonLength, nameof(reason));
        CancelledBy = Trimmed(by, MaxActorLength, nameof(by));
        CancelledAtUtc = whenUtc;
    }

    /// <summary>Projects onto the fold's input. The one bridge between the entity and the arithmetic.</summary>
    public MessagingAllowanceLedgerEntry ToLedgerEntry() =>
        new(Id, Kind, Messages, EffectiveMonth, RecordedAtUtc, IsCancelled);

    /// <summary>
    /// ⚠️ Validated <b>here</b> rather than trusted, because a malformed key does not fail — it silently never
    /// matches, so the entry folds into no month at all and the cabinet reads as having no allowance.
    /// </summary>
    private static string ValidMonthKey(string? monthKey, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(monthKey))
        {
            throw new ArgumentException("Le mois d'effet est requis.", parameterName);
        }

        var trimmed = monthKey.Trim();
        if (trimmed.Length != MonthKeyLength
            || trimmed[4] != '-'
            || !int.TryParse(trimmed.AsSpan(0, 4), out var year)
            || !int.TryParse(trimmed.AsSpan(5, 2), out var month)
            || year is < 2000 or > 2999
            || month is < 1 or > 12)
        {
            throw new ArgumentException(
                $"'{monthKey}' n'est pas un mois au format AAAA-MM.", parameterName);
        }

        return trimmed;
    }

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
