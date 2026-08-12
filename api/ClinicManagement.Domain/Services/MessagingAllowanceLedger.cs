using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// One allocation entry reduced to what the fold reads. A record rather than the entity so the fold has exactly one
/// implementation: the write path projects <c>MessagingAllowanceEntry</c> onto it, and <c>verify-schema</c> — which
/// reads over raw ADO and builds no entities — projects the same shape out of PostgreSQL.
/// </summary>
/// <param name="EffectiveMonth">
/// The <c>AAAA-MM</c> month this entry starts applying in. Compared <b>ordinally</b>: the key is zero-padded, so
/// lexicographic order is chronological order and no parsing happens in the fold at all (D-7).
/// </param>
/// <param name="RecordedAtUtc">
/// When it was recorded — the tie-break that orders two entries effective in the same month, so « the standing
/// figure in force » cannot depend on which row the database returned first.
/// </param>
public sealed record MessagingAllowanceLedgerEntry(
    Guid Id,
    MessagingAllowanceKind Kind,
    int Messages,
    string EffectiveMonth,
    DateTime RecordedAtUtc,
    bool IsCancelled);

/// <summary>
/// Folds a cabinet's append-only allocation ledger into the one figure a month is metered against (FR-2).
/// <b>Pure, total, and clock-free.</b>
///
/// <para><b>⚠️ The month is a parameter and there is no clock, and that is load-bearing.</b> The naive shape reads
/// « what is this cabinet allowed <i>now</i> », which makes the answer depend on when it is recomputed: a re-fold
/// after midnight on the 1st would silently rewrite a closed month's snapshot, and
/// <c>verify-schema</c>'s <c>monthly-allowance-matches-ledger</c> would flap monthly. Taking
/// <paramref name="monthKey"/> in is also what lets <c>messaging-report --month 2026-07</c> answer for a month that
/// has <b>closed</b>, which is when the vendor reconciles — and it is free, because the fold never needed a clock in
/// the first place. <c>SubscriptionLedger</c>'s own reasoning, one dimension over.</para>
///
/// <para><b>⚠️ No entry folds to <c>null</c>, never to 0.</b> They are opposite facts: 0 is a cabinet the vendor
/// decided sends no WhatsApp reminders, while null is a cabinet whose allowance record is <i>missing</i> — our
/// bookkeeping fault, held under its own reason and its own French sentence (FR-4's second branch, AC-4.3). Collapsing
/// them would tell a practice « votre forfait est épuisé » about a row nobody ever wrote.</para>
///
/// <para><b>⚠️ A cancelled entry contributes nothing to <i>every</i> month it fed, the current one included</b>
/// (AC-7.4) — which is the deliberate asymmetry with AC-6.4, where a <i>lowering</i> waits for the next month
/// (AC-7.4a). The distinction is that a lowering is a decision about the future, while a cancellation says the entry
/// should never have existed. It falls out of the fold for free: a cancelled entry is simply skipped, whatever month
/// is being asked about.</para>
/// </summary>
public static class MessagingAllowanceLedger
{
    /// <summary>
    /// What <paramref name="monthKey"/> is allowed, or <b>null</b> when no entry reaches it at all.
    ///
    /// <para>The standing figure is the <i>last</i> non-cancelled <see cref="MessagingAllowanceKind.Standing"/> entry
    /// effective on or before the month; every non-cancelled <see cref="MessagingAllowanceKind.TopUp"/> effective
    /// <i>in</i> that month is added on top. A top-up with no standing entry behind it still yields a figure — a
    /// cabinet the vendor has given messages to is not a cabinet with no allowance record.</para>
    /// </summary>
    public static int? Fold(IEnumerable<MessagingAllowanceLedgerEntry> entries, string monthKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(monthKey);

        int? standing = null;
        var topUps = 0;
        var toppedUp = false;

        // Ordered here rather than trusted from the caller: the answer depends on the order, and two call sites
        // (the repository read and verify-schema's raw projection) would otherwise each carry an ORDER BY the fold
        // silently depends on. `Id` after the instant, so two entries recorded in the same tick fold stably.
        foreach (var entry in entries
                     .Where(e => !e.IsCancelled)
                     .OrderBy(e => e.EffectiveMonth, StringComparer.Ordinal)
                     .ThenBy(e => e.RecordedAtUtc)
                     .ThenBy(e => e.Id))
        {
            var reach = string.CompareOrdinal(entry.EffectiveMonth, monthKey);

            // Effective after the month asked about: nothing later can reach it either, since the enumeration is
            // ordered by effective month.
            if (reach > 0)
            {
                break;
            }

            switch (entry.Kind)
            {
                case MessagingAllowanceKind.Standing:
                    // Replaces, never accumulates: a later standing figure supersedes the earlier one outright.
                    standing = entry.Messages;
                    break;

                case MessagingAllowanceKind.TopUp when reach == 0:
                    // A top-up belongs to its own month alone; an earlier month's is already spent or lapsed.
                    topUps += entry.Messages;
                    toppedUp = true;
                    break;
            }
        }

        return standing is null && !toppedUp ? null : (standing ?? 0) + topUps;
    }

    /// <summary>
    /// Which month a newly-recorded <b>standing</b> figure takes effect in (AC-6.4a): the current month when it
    /// <b>raises</b> the figure in force, the next one when it <b>lowers</b> it.
    ///
    /// <para>The vendor states an amount and never a month — that is the whole of AC-6.4a — so this is the server's
    /// decision, and it is made against the ledger rather than against a stored snapshot the console might not have
    /// refreshed.</para>
    ///
    /// <para>⚠️ <b>Both month keys are parameters, and the second is not derived here.</b> Month arithmetic lives in
    /// <c>ClinicClock</c> (FR-8b), which is in the Application layer — this project references nothing — so computing
    /// « the next month » here would be the second copy FR-8b exists to prevent, in the place it would be least
    /// visible. The caller passes <c>ClinicClock.CurrentMonthKey()</c> and <c>ClinicClock.NextMonthKey(…)</c>.</para>
    ///
    /// <para>An <b>equal</b> figure resolves to the current month. It changes nothing either way, and treating a
    /// no-op as a lowering would leave a puzzling « prend effet le mois prochain » on a confirmation screen.</para>
    /// </summary>
    public static string EffectiveMonthFor(
        IEnumerable<MessagingAllowanceLedgerEntry> entries,
        int newMessagesPerMonth,
        string currentMonthKey,
        string nextMonthKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentMonthKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextMonthKey);

        // Measured against the STANDING figure alone, not the folded total: a top-up is a one-off addition to this
        // month, so comparing a new standing figure against « standing + top-up » would read an ordinary raise as a
        // lowering and defer it by a month for a reason nobody chose.
        var inForce = StandingInForce(entries, currentMonthKey);

        return inForce is { } current && newMessagesPerMonth < current ? nextMonthKey : currentMonthKey;
    }

    /// <summary>
    /// The standing figure covering <paramref name="monthKey"/>, ignoring top-ups, or null where none reaches it.
    /// Public because <c>messaging-report</c> and the console file both state « forfait mensuel » separately from
    /// « ce mois-ci », and re-deriving it beside the fold is how the two come to disagree.
    /// </summary>
    public static int? StandingInForce(IEnumerable<MessagingAllowanceLedgerEntry> entries, string monthKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(monthKey);

        int? standing = null;

        foreach (var entry in entries
                     .Where(e => !e.IsCancelled && e.Kind == MessagingAllowanceKind.Standing)
                     .Where(e => string.CompareOrdinal(e.EffectiveMonth, monthKey) <= 0)
                     .OrderBy(e => e.EffectiveMonth, StringComparer.Ordinal)
                     .ThenBy(e => e.RecordedAtUtc)
                     .ThenBy(e => e.Id))
        {
            standing = entry.Messages;
        }

        return standing;
    }

    /// <summary>
    /// Every month from <paramref name="fromMonthKey"/> through <paramref name="throughMonthKey"/> inclusive that a
    /// given entry feeds — what a refold has to rewrite after a grant or a cancellation (AC-6.3, AC-7.4).
    ///
    /// <para>The <b>months are supplied</b>, for <see cref="EffectiveMonthFor"/>'s reason: enumerating a calendar
    /// range is month arithmetic and belongs to <c>ClinicClock</c>. This answers only « which of these does the
    /// entry reach », which is a fact about the ledger.</para>
    /// </summary>
    public static IReadOnlyList<string> MonthsFedBy(
        MessagingAllowanceLedgerEntry entry, IEnumerable<string> candidateMonths)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(candidateMonths);

        return entry.Kind switch
        {
            // A standing figure reaches its effective month and every month after it, until superseded — and
            // « until superseded » is the fold's business, not this one's, so every later month is a candidate.
            MessagingAllowanceKind.Standing => candidateMonths
                .Where(m => string.CompareOrdinal(m, entry.EffectiveMonth) >= 0)
                .ToList(),
            MessagingAllowanceKind.TopUp => candidateMonths
                .Where(m => string.Equals(m, entry.EffectiveMonth, StringComparison.Ordinal))
                .ToList(),
            _ => Array.Empty<string>()
        };
    }
}
