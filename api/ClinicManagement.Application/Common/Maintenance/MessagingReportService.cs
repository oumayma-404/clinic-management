using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Common.Maintenance;

/// <summary>
/// Which group of the report a cabinet falls in — the one classification, named
/// (<c>vendor-whatsapp-messaging-quota</c> AC-9.4).
///
/// <para>⚠️ <b>Three finding kinds, deliberately distinguished</b>, because the vendor's action differs for each:
/// <see cref="Exhausted"/> is « recharge-le », <see cref="Unmeasured"/> and <see cref="NoAllowance"/> are « notre
/// comptabilité est cassée », and <see cref="TemplateNotUtility"/> is « notre coût par message a bougé » (FR-7b).
/// Folding them into one « à traiter » count would tell the vendor to top up a cabinet whose real problem is that
/// nothing is counting.</para>
/// </summary>
public enum MessagingReportBucket
{
    /// <summary>No allowance record reaches the month at all — FR-4's second branch, and a fault on our side.</summary>
    NoAllowance,

    /// <summary>
    /// No counting row for the month (FR-1a). ⚠️ Its own bucket rather than folded into <see cref="NoAllowance"/>: an
    /// allocation with no counting row behind it means the daily provisioning pass has not reached this cabinet, which
    /// is a different fault with a different fix from having no allocation at all.
    /// </summary>
    Unmeasured,

    /// <summary>Consumption has met or passed the forfait: reminders are being held right now.</summary>
    Exhausted,

    /// <summary>
    /// Meta has re-categorised the cabinet's reminder template away from <c>UTILITY</c> (FR-7b) — the vendor is being
    /// billed at another rate. ⚠️ Nothing holds the reminders for this, and the practice is never told: it is our cost,
    /// not their limit.
    /// </summary>
    TemplateNotUtility,

    /// <summary>Nothing to do.</summary>
    Healthy
}

/// <summary>One cabinet as the messaging report shows it.</summary>
/// <param name="Allowance">
/// The folded forfait for the month, or <b>null</b> where no entry reaches it. Never defaulted to 0 — see
/// <c>MessagingAllowanceLedger</c> for why those are opposite facts.
/// </param>
/// <param name="Consumed">Null where the cabinet has no counting row for the month (« non mesuré »).</param>
/// <param name="StoredAllowance">
/// The <b>snapshot</b> on the counting row, beside the folded figure above. Both are printed so a drift between them is
/// visible here as well as in <c>verify-schema</c>'s <c>monthly-allowance-matches-ledger</c> — this verb is the one a
/// vendor runs by hand when a cabinet complains, and « the two disagree » is the answer that explains everything else.
/// </param>
public sealed record MessagingReportLine(
    Guid ClinicId,
    string ClinicName,
    MessagingReportBucket Bucket,
    int? Allowance,
    int? StoredAllowance,
    int? Consumed,
    int? Remaining,
    int? StandingAllowance,
    string SenderStateLabel,
    string? TemplateCategory);

/// <summary>
/// The deployment's cabinets grouped by what the vendor would act on, for one Tunisian month (AC-8.6, AC-9.4).
/// </summary>
/// <param name="MonthKey">
/// Which month this describes, <c>AAAA-MM</c>. On the response rather than left to the caller's memory: the whole point
/// of <c>--month</c> is that the report can answer for a <b>closed</b> month, and an unlabelled figure invites reading
/// last month's totals as this month's.
/// </param>
public sealed record MessagingReport(
    string MonthKey,
    string MonthLabel,
    int TotalCabinets,
    IReadOnlyList<MessagingReportLine> Exhausted,
    IReadOnlyList<MessagingReportLine> NoAllowance,
    IReadOnlyList<MessagingReportLine> Unmeasured,
    IReadOnlyList<MessagingReportLine> TemplateNotUtility,
    IReadOnlyList<MessagingReportLine> Healthy)
{
    /// <summary>
    /// What makes the verb exit <b>2</b> (AC-9.4).
    ///
    /// <para>⚠️ All four groups count, unlike <c>subscription-report</c> where a <i>suspended</i> cabinet is listed
    /// without alarming. The difference is that suspension is a decision the vendor already made and will not act on
    /// again, whereas every group here is something they have not: a cabinet out of messages, a cabinet nothing is
    /// counting for, a cabinet with no forfait on record, and a template costing more than it should.</para>
    /// </summary>
    public bool NeedsAttention =>
        Exhausted.Count > 0 || NoAllowance.Count > 0 || Unmeasured.Count > 0 || TemplateNotUtility.Count > 0;
}

/// <summary>
/// The core of the <c>messaging-report</c> console verb (AC-8.6, AC-9.4): where every cabinet of the deployment stands
/// on its WhatsApp reminder forfait, for a month the caller names.
///
/// <para><b>Deliberately not DI-registered</b>, like <c>SubscriptionReportService</c> and
/// <c>MoneyReconciliationService</c> beside it: there is no HTTP-reachable vendor report (AC-9.3), and it lives here so
/// <c>UnitTests</c> — which references Application — can exercise it.</para>
///
/// <para><b>⚠️ It derives nothing itself.</b> The forfait comes from the real <c>MessagingAllowanceLedger.Fold</c>, the
/// sender state from <c>MessagingSender.From</c>, and « épuisé » from the counting row's own
/// <c>ClinicMessagingMonth.IsExhausted</c> — the same three rules the outbox gate, the clinic card and the console file
/// read. A report computing « is this cabinet out of messages? » its own way would be the one place able to disagree
/// with the product about whose reminders are being held.</para>
///
/// <para><b>⚠️ The month is a parameter and there is no clock</b>, which is what makes <c>--month 2026-07</c> answer for
/// a <b>closed</b> month — when the vendor actually reconciles against Meta's bill. It is free, because the fold never
/// needed a clock (FR-2); reading one here would have made « what did we bill for July? » unanswerable in August.</para>
/// </summary>
public class MessagingReportService
{
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicReminderSettingsRepository _reminderSettings;

    public MessagingReportService(
        IMessagingAllowanceRepository allowances,
        IClinicReminderSettingsRepository reminderSettings)
    {
        _allowances = allowances;
        _reminderSettings = reminderSettings;
    }

    /// <param name="monthKey">The Tunisian month, <c>AAAA-MM</c>. Current or closed; the fold takes either.</param>
    /// <param name="monthLabel">Its French name, resolved by the caller through <c>ClinicClock.MonthLabelFr</c>.</param>
    public async Task<MessagingReport> RunAsync(
        string monthKey,
        string monthLabel,
        CancellationToken cancellationToken = default)
    {
        // Every cabinet beside its row for the month, or beside null — keying off the counting table would make « the
        // provisioning pass has not run » the one state this report cannot show, which is the opposite of what a safety
        // net is for (FR-1a).
        var rows = await _allowances.GetForReportAsync(monthKey, cancellationToken);

        var exhausted = new List<MessagingReportLine>();
        var noAllowance = new List<MessagingReportLine>();
        var unmeasured = new List<MessagingReportLine>();
        var templateNotUtility = new List<MessagingReportLine>();
        var healthy = new List<MessagingReportLine>();

        foreach (var row in rows)
        {
            var line = await DescribeAsync(row, monthKey, cancellationToken);

            var bucket = line.Bucket switch
            {
                MessagingReportBucket.NoAllowance => noAllowance,
                MessagingReportBucket.Unmeasured => unmeasured,
                MessagingReportBucket.Exhausted => exhausted,
                MessagingReportBucket.TemplateNotUtility => templateNotUtility,
                _ => healthy,
            };

            bucket.Add(line);
        }

        // Least remaining first inside the group that matters: the cabinet furthest past its forfait is the one whose
        // patients have gone unwarned longest.
        exhausted.Sort((a, b) => Nullable.Compare(a.Remaining, b.Remaining));

        return new MessagingReport(
            monthKey, monthLabel, rows.Count, exhausted, noAllowance, unmeasured, templateNotUtility, healthy);
    }

    /// <summary>
    /// One cabinet's whole allocation ledger beside its month — the read behind « which allocation do I cancel? »,
    /// which no deployment-wide listing can answer because the entry ids are the thing being looked up.
    ///
    /// <para>It is the reason <c>messaging-cancel --entry &lt;id&gt;</c> is usable at all: nothing else in the product
    /// prints a <c>MessagingAllowanceEntry</c> id, so without this mode a mis-keyed allocation older than the current
    /// console session would be uncorrectable from a terminal — <c>subscription-report --clinic</c>'s own argument.</para>
    /// </summary>
    public async Task<MessagingCabinetReport?> RunForCabinetAsync(
        Guid clinicId,
        string monthKey,
        string monthLabel,
        CancellationToken cancellationToken = default)
    {
        var rows = await _allowances.GetForReportAsync(monthKey, cancellationToken);
        var row = rows.FirstOrDefault(r => r.ClinicId == clinicId);

        if (row is null)
        {
            return null;
        }

        var line = await DescribeAsync(row, monthKey, cancellationToken);
        var entries = await _allowances.GetEntriesAsync(clinicId, cancellationToken);

        var ledger = entries
            .OrderBy(e => e.EffectiveMonth, StringComparer.Ordinal)
            .ThenBy(e => e.RecordedAtUtc)
            .ThenBy(e => e.Id)
            .Select(e => new MessagingReportEntry(
                e.Id,
                e.Kind,
                MessagingAllowanceLabels.Kind(e.Kind),
                e.Messages,
                e.EffectiveMonth,
                e.RecordedAtUtc,
                e.Amount,
                e.Reference,
                e.Note,
                e.RecordedBy,
                e.IsCancelled,
                e.CancelReason))
            .ToList();

        return new MessagingCabinetReport(monthKey, monthLabel, line, ledger);
    }

    /// <summary>
    /// One cabinet's line, and its bucket. Both the deployment-wide run and the single-cabinet one go through here, so
    /// « which cabinets must the vendor act on » has one implementation and one exit code behind it — the lesson
    /// <c>SubscriptionReportService.RunForCabinetAsync</c> records.
    /// </summary>
    private async Task<MessagingReportLine> DescribeAsync(
        ClinicMessagingReportRow row, string monthKey, CancellationToken cancellationToken)
    {
        var entries = await _allowances.GetEntriesAsync(row.ClinicId, cancellationToken);
        var ledger = entries.Select(e => e.ToLedgerEntry()).ToList();

        var allowance = MessagingAllowanceLedger.Fold(ledger, monthKey);
        var standing = MessagingAllowanceLedger.StandingInForce(ledger, monthKey);
        var settings = await _reminderSettings.GetByClinicIdAsync(row.ClinicId, cancellationToken);

        var senderState = MessagingSender.From(
            settings?.WhatsAppConnectionStatus ?? WhatsAppConnectionStatus.NotConnected,
            settings?.WhatsAppTemplateStatus);

        return new MessagingReportLine(
            ClinicId: row.ClinicId,
            ClinicName: row.ClinicName,
            Bucket: Classify(allowance, row.Month, settings?.WhatsAppTemplateCategory),
            Allowance: allowance,
            StoredAllowance: row.Month?.AllowanceMessages,
            Consumed: row.Month?.ConsumedMessages,
            Remaining: row.Month?.RemainingMessages,
            StandingAllowance: standing,
            SenderStateLabel: MessagingSender.Label(senderState),
            TemplateCategory: settings?.WhatsAppTemplateCategory);
    }

    /// <summary>
    /// AC-9.4's three finding kinds plus « non mesuré », in one ordered decision.
    ///
    /// <para>⚠️ <b>The order is the design.</b> « No allowance record » is asked first because it is the only one that
    /// makes the other answers meaningless — a cabinet with no forfait on record cannot meaningfully be « épuisé »
    /// (there is nothing it was allowed to spend) and telling the vendor to top it up would skip the question of how it
    /// came to have none. « Non mesuré » comes next for the same reason one layer down: with no counting row there is no
    /// consumption to compare against anything.</para>
    /// </summary>
    /// <remarks>
    /// Public so the ordering itself is assertable. It is a pure function of three values and the whole of AC-9.4's
    /// « distinguishes … and exits with a distinct code », so it is worth a test of its own rather than only being
    /// reachable through a fixture that has to manufacture each state — the shape
    /// <c>SubscriptionReportService.IsFinding</c> reaches for and can only offer to its own assembly.
    /// </remarks>
    public static MessagingReportBucket Classify(
        int? allowance, ClinicMessagingMonth? month, string? templateCategory) =>
        (allowance, month) switch
        {
            (null, _) => MessagingReportBucket.NoAllowance,
            (_, null) => MessagingReportBucket.Unmeasured,
            _ when month.IsExhausted => MessagingReportBucket.Exhausted,
            _ when IsNotUtility(templateCategory) => MessagingReportBucket.TemplateNotUtility,
            _ => MessagingReportBucket.Healthy,
        };

    /// <summary>
    /// FR-7b. ⚠️ <b>Null is not a finding</b> — nothing stores a category until Part 4, and reporting « catégorie
    /// inconnue » on every cabinet of the deployment would make this verb exit 2 for ever, i.e. an alarm nobody reads.
    /// </summary>
    private static bool IsNotUtility(string? templateCategory) =>
        !string.IsNullOrWhiteSpace(templateCategory)
        && !string.Equals(templateCategory.Trim(), "UTILITY", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One allocation of a single cabinet's ledger, as the report prints it.</summary>
public sealed record MessagingReportEntry(
    Guid EntryId,
    MessagingAllowanceKind Kind,
    string KindLabel,
    int Messages,
    string EffectiveMonth,
    DateTime RecordedAtUtc,
    decimal? Amount,
    string? Reference,
    string? Note,
    string? RecordedBy,
    bool IsCancelled,
    string? CancelReason);

/// <summary>One cabinet in full: where it stands for the month, and every allocation behind that figure.</summary>
public sealed record MessagingCabinetReport(
    string MonthKey,
    string MonthLabel,
    MessagingReportLine Cabinet,
    IReadOnlyList<MessagingReportEntry> Ledger)
{
    /// <summary>
    /// Whether <c>messaging-report --clinic &lt;id&gt;</c> exits <b>2</b>, computed from the same
    /// <see cref="MessagingReportBucket"/> the deployment-wide run buckets on rather than re-derived in the verb — the
    /// lesson <c>SubscriptionCabinetReport.NeedsAttention</c> records: two implementations of « must the vendor act on
    /// this? » agreed only by coincidence, and an exit code that quietly stops alarming reads exactly like a clean run.
    /// </summary>
    public bool NeedsAttention => Cabinet.Bucket != MessagingReportBucket.Healthy;
}
