namespace ClinicManagement.Domain.Services;

/// <summary>
/// One ledger entry reduced to what the fold reads. A record rather than the entity so the fold has exactly one
/// implementation: the write path projects <c>SubscriptionPeriod</c> onto it, and <c>verify-schema</c> — which
/// reads over raw ADO and builds no entities — projects the same shape out of PostgreSQL.
/// </summary>
/// <param name="RecordedOnClinicDay">
/// The clinic-local day this entry was recorded on, and the <b>inclusive start</b> of the cover it may open. It is
/// the entry's own anchor, never "today": see the class remarks on why passing a clock in is wrong.
/// </param>
public sealed record SubscriptionLedgerEntry(
    Guid Id,
    DateTime RecordedOnClinicDay,
    DateTime RecordedAtUtc,
    int? DurationMonths,
    int? DurationDays,
    DateTime? ExplicitEndsOn,
    bool IsCancelled)
{
    /// <summary>No duration of any kind — cover with no end date at all (FR-1's real « sans échéance » state).</summary>
    public bool IsOpenEnded => DurationMonths is null && DurationDays is null && ExplicitEndsOn is null;
}

/// <summary>
/// The stretch one entry actually covers, derived by the fold rather than stored (FR-2, AC-2.3's « période
/// couverte »). A <b>cancelled</b> entry gets <c>(null, null)</c> — it is shown « Annulé » and contributes
/// nothing — and an <b>open-ended</b> one gets <c>(FromDay, null)</c>, « sans échéance ».
/// </summary>
public sealed record PeriodSpan(Guid EntryId, DateTime? FromDay, DateTime? ThroughDay);

/// <summary>
/// Folds the append-only ledger into the one date the product enforces on. Pure, total, and <b>clock-free</b>.
///
/// <para><b>⚠️ No clock parameter, and that is load-bearing.</b> The naive reading of AC-5.2 (« the later of the
/// current end date or today, plus the duration ») is to pass <c>today</c> in. Do that and the result depends on
/// <i>when</i> it is recomputed: a lapsed entry would restart from today on every re-fold, cancelling one entry
/// would move unrelated dates, and <c>verify-schema</c>'s « stored == fold » check would flap daily. Each entry
/// anchors on its <b>own</b> recorded clinic-day instead, which reproduces AC-5.2 exactly — at the moment an entry
/// is recorded, its recorded day <i>is</i> today.</para>
///
/// <para><b>⚠️ The cursor is exclusive — « the first day not yet covered » — and that is what makes one formula
/// correct for both anchors.</b> An inclusive running end and a recorded day are not the same kind of value: a
/// recorded day is an inclusive <i>start</i> (creation day is day 1, AC-1.1) while a running end is an inclusive
/// <i>end</i>. A single <c>anchor + duration</c> over both is therefore wrong in one of the two cases whichever
/// way it is written — it yields a <b>31-day</b> trial (AC-1.1 says 10 Aug → 8 Sep) or a one-day grant on a lapsed
/// cabinet (EC-3 says end 20 Sep + 12 months → 20 Sep 2027, with no −1). Folding on an exclusive cursor removes
/// the asymmetry instead of branching on it.</para>
///
/// <para><b>Consequently the trial's end date is not written directly either.</b>
/// <c>SubscriptionProvisioning</c> builds the trial entry and calls <c>ClinicSubscription.RecomputeFrom</c> like
/// every other date. A hand-computed <c>creationDay.AddDays(trialDays - 1)</c> beside a fold that disagrees with it
/// states the arithmetic twice and makes <c>subscription-end-date-matches-ledger</c> red on every newly created
/// cabinet — the shape most likely to be dismissed as « the new check is noisy ».</para>
/// </summary>
public static class SubscriptionLedger
{
    /// <summary>The entitlement's inclusive last working day, or null for « sans échéance ».</summary>
    public static DateTime? Fold(IEnumerable<SubscriptionLedgerEntry> entries) => FoldWithSpans(entries).EndsOn;

    /// <summary>
    /// The fold, plus the stretch each entry covers. One implementation with two callers rather than two
    /// arithmetics: the write path reads <see cref="Fold"/> and the history screen reads the spans.
    ///
    /// <para>Entries are ordered here rather than trusted from the caller. The answer depends on the order, and
    /// two call sites (the repository read and <c>verify-schema</c>'s raw projection) would otherwise each carry
    /// an <c>ORDER BY</c> the fold silently depends on.</para>
    /// </summary>
    public static (DateTime? EndsOn, IReadOnlyList<PeriodSpan> Spans) FoldWithSpans(
        IEnumerable<SubscriptionLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var spans = new List<PeriodSpan>();
        DateTime? endsOn = null;
        DateTime? cursor = null;
        var openEnded = false;

        foreach (var entry in entries.OrderBy(e => e.RecordedAtUtc).ThenBy(e => e.Id))
        {
            if (entry.IsCancelled)
            {
                spans.Add(new PeriodSpan(entry.Id, null, null));
                continue;
            }

            if (entry.IsOpenEnded)
            {
                // The cursor is deliberately left alone: there is no first-uncovered day to advance to. A later
                // paid entry therefore anchors on its own recorded day, and the entitlement stays open-ended.
                spans.Add(new PeriodSpan(entry.Id, entry.RecordedOnClinicDay.Date, null));
                openEnded = true;
                continue;
            }

            var start = cursor is null || cursor < entry.RecordedOnClinicDay.Date
                ? entry.RecordedOnClinicDay.Date  // the first entry, or the cabinet had lapsed
                : cursor.Value;                   // still covered: resume where the cover ran out

            cursor = entry.DurationMonths is { } months ? start.AddMonths(months)
                : entry.DurationDays is { } days ? start.AddDays(days)
                : entry.ExplicitEndsOn!.Value.Date.AddDays(1);

            endsOn = cursor.Value.AddDays(-1);
            spans.Add(new PeriodSpan(entry.Id, start, endsOn));
        }

        return (openEnded ? null : endsOn, spans);
    }
}
