using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>
/// « Is the cover in force <b>today</b> the free trial? » — the one answer to
/// <see cref="SubscriptionStateReader.Read(ClinicSubscription, DateTime, bool)"/>'s <c>isTrial</c> parameter, which
/// exists precisely because the entitlement row carries one date and no memory of where it came from.
///
/// <para>It lives here rather than as a private method of <c>GetSubscriptionQuery</c> because that read is no longer
/// its only caller — the vendor console asks the same question — and a correct helper wired to one call site is this
/// repository's dominant defect shape. It was <b>moved</b>, not copied.</para>
///
/// <para>⚠️ <b>Distinct from <see cref="ClinicSubscription.LatestCoverKind"/>, and neither replaces the other.</b>
/// This reads the ledger's spans against a <i>day</i>, so it can only be computed by someone holding the whole
/// ledger; the stored kind is clock-free and is what the console can filter on in SQL. They agree everywhere the
/// console's « en essai » filter can select — see that property's own remarks for the one shape in which they part.</para>
/// </summary>
public static class SubscriptionTrial
{
    /// <summary>
    /// <b>The last covering entry wins.</b> A grandfathered cabinet that later pays holds two entries covering
    /// today — the open-ended one and the paid one — and it is on neither a trial nor a mystery: it is on the most
    /// recently recorded cover. Fold order (<c>RecordedAtUtc</c> then id) is the ledger's own, applied inside
    /// <see cref="SubscriptionLedger.FoldWithSpans"/>, so this cannot depend on how the rows came back.
    ///
    /// <para>No covering entry at all means the cabinet has lapsed — the state reader answers <c>Expired</c>, and a
    /// label it would not use is not worth deriving.</para>
    /// </summary>
    public static bool IsOnTrial(IReadOnlyList<SubscriptionPeriod> entries, DateTime clinicToday)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var spans = SubscriptionLedger.FoldWithSpans(entries.Select(e => e.ToLedgerEntry())).Spans;
        var day = clinicToday.Date;

        var covering = spans.LastOrDefault(s =>
            s.FromDay is { } from
            && from <= day
            && (s.ThroughDay is null || day <= s.ThroughDay.Value));

        return covering is not null
               && entries.FirstOrDefault(e => e.Id == covering.EntryId)?.Kind == SubscriptionPeriodKind.Trial;
    }
}
