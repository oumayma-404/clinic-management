using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One counting row per (cabinet, Tunisian month): the month's <b>allowance snapshot</b> and how much of it has been
/// spent (FR-1). One WhatsApp reminder sent is one unit, counted where the send is marked <c>Sent</c>.
///
/// <para><b>⚠️ A plain <see cref="Entity{TId}"/> and deliberately not an <see cref="AggregateRoot{TId}"/> (D-6).</b>
/// <c>AuditSaveChangesInterceptor</c> writes one audit row per mutated aggregate root, and this row is incremented
/// on <i>every WhatsApp reminder sent</i>, minutely — which is precisely why <c>Notification</c> is on that
/// interceptor's exclusion list. A practice's real history would be buried in machine noise within a day. The
/// audited artefact is the <see cref="MessagingAllowanceEntry"/>; this is derived from it.</para>
///
/// <para><b><see cref="AllowanceMessages"/> is a denormalisation of the fold, not an independent figure.</b> It
/// exists because the vendor console filters and sorts the whole portfolio on consumption-against-allowance
/// <i>before</i> a page is cut (AC-8.2), which folding N cabinets' ledgers cannot serve. Nothing in the model can
/// say it must equal the fold, so <c>verify-schema</c>'s <c>monthly-allowance-matches-ledger</c> re-derives every row
/// through the <b>real</b> fold and reports both directions (R-6) — the
/// <c>subscription-end-date-matches-ledger</c> precedent.</para>
///
/// <para>⚠️ <b>A row's absence is a fact, and it is not zero.</b> « Non mesuré » (no row) and « 0 rappel envoyé » (a
/// row reading zero) are opposite claims — the first about our counting, the second about the practice — and AC-2.4
/// / AC-8.3 keep them apart on every screen. That is why nothing here defaults a missing row into existence.</para>
/// </summary>
public class ClinicMessagingMonth : Entity<Guid>
{
    public const int MonthKeyLength = 7;

    public Guid ClinicId { get; private set; }

    /// <summary>The Tunisian calendar month, <c>AAAA-MM</c> (D-7). Unique per cabinet — see the EF configuration.</summary>
    public string MonthKey { get; private set; } = string.Empty;

    /// <summary>
    /// What the fold says this cabinet was allowed this month. Rewritten by <c>MessagingAllowanceRefold</c> and by
    /// the daily pass, never incremented.
    /// </summary>
    public int AllowanceMessages { get; private set; }

    /// <summary>
    /// WhatsApp reminders actually sent this month. Only ever goes up, and only through
    /// <see cref="RecordSend"/> — a cancellation of an allocation moves the allowance and leaves this alone
    /// (AC-7.4), because the messages were sent and the vendor paid for them.
    /// </summary>
    public int ConsumedMessages { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private ClinicMessagingMonth() { } // For EF Core

    public static ClinicMessagingMonth For(Guid clinicId, string monthKey, int allowanceMessages, DateTime createdAtUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant du cabinet est requis.", nameof(clinicId));
        }

        if (string.IsNullOrWhiteSpace(monthKey) || monthKey.Trim().Length != MonthKeyLength)
        {
            throw new ArgumentException("Le mois doit être au format AAAA-MM.", nameof(monthKey));
        }

        if (allowanceMessages < 0)
        {
            throw new ArgumentException("Le forfait ne peut pas être négatif.", nameof(allowanceMessages));
        }

        return new ClinicMessagingMonth
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            MonthKey = monthKey.Trim(),
            AllowanceMessages = allowanceMessages,
            CreatedAt = createdAtUtc
        };
    }

    /// <summary>
    /// One WhatsApp reminder left the building. Staged into the <b>same save</b> that marks the row <c>Sent</c>, so a
    /// crash loses both or neither (FR-1, EC-14).
    /// </summary>
    public void RecordSend(DateTime whenUtc)
    {
        ConsumedMessages++;
        UpdatedAt = whenUtc;
    }

    /// <summary>
    /// Rewrites the month's allowance from the fold. Idempotent, and it deliberately does <b>not</b> touch
    /// <see cref="ConsumedMessages"/>: an allocation cancelled after the messages were sent leaves consumption
    /// standing and the month reading « épuisé » (AC-7.4), which is the honest outcome.
    /// </summary>
    public void SetAllowance(int allowanceMessages, DateTime whenUtc)
    {
        if (allowanceMessages < 0)
        {
            throw new ArgumentException("Le forfait ne peut pas être négatif.", nameof(allowanceMessages));
        }

        if (AllowanceMessages == allowanceMessages)
        {
            return;
        }

        AllowanceMessages = allowanceMessages;
        UpdatedAt = whenUtc;
    }

    /// <summary>
    /// What is left, floored at zero (AC-2.1). Never negative: a cancellation can put consumption above the
    /// allowance, and « −17 rappels » is not a quantity a practice can act on.
    /// </summary>
    public int RemainingMessages => Math.Max(0, AllowanceMessages - ConsumedMessages);

    /// <summary>Nothing left to send with. A zero allowance is exhausted from the first tick (AC-8.2's own note).</summary>
    public bool IsExhausted => RemainingMessages == 0;
}
