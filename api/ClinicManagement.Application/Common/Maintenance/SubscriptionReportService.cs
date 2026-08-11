using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Common.Maintenance;

/// <summary>One cabinet as the vendor report shows it. <paramref name="State"/> is null where it has no entitlement.</summary>
public sealed record SubscriptionReportLine(
    Guid ClinicId,
    string ClinicName,
    SubscriptionState? State,
    string StateLabel,
    DateTime? EndsOn,
    int? DaysRemaining,
    bool AllowsWrites,
    string? PlanLabel,
    string? SuspensionReason);

/// <summary>
/// The deployment's cabinets grouped by what the vendor would act on (AC-5.9).
/// </summary>
/// <param name="WithoutEntitlement">
/// FR-13's failure state. Its own group rather than folded into « expired », because the two have different causes
/// and only one of them is a defect.
/// </param>
public sealed record SubscriptionReport(
    DateTime ClinicToday,
    int WithinDays,
    int TotalCabinets,
    IReadOnlyList<SubscriptionReportLine> Expiring,
    IReadOnlyList<SubscriptionReportLine> Expired,
    IReadOnlyList<SubscriptionReportLine> Suspended,
    IReadOnlyList<SubscriptionReportLine> WithoutEntitlement,
    IReadOnlyList<SubscriptionReportLine> Healthy)
{
    /// <summary>
    /// What makes the verb exit <b>2</b>.
    ///
    /// <para>⚠️ <b>A suspended cabinet is listed but does not count</b>, deliberately: suspension is a decision the
    /// vendor already made, so counting it would leave a scheduled report permanently at exit 2 with nothing to do
    /// — and a safety net that always alarms is one nobody reads. A cabinet with <i>no</i> entitlement does count,
    /// because that is a fault rather than a state anyone chose.</para>
    /// </summary>
    public bool NeedsAttention => Expiring.Count > 0 || Expired.Count > 0 || WithoutEntitlement.Count > 0;
}

/// <summary>One ledger entry of a single cabinet, with the stretch the fold says it covers.</summary>
public sealed record SubscriptionReportEntry(
    Guid EntryId,
    SubscriptionPeriodKind Kind,
    string KindLabel,
    DateTime RecordedOnClinicDay,
    DateTime? FromDay,
    DateTime? ThroughDay,
    decimal? Amount,
    string? MethodLabel,
    string? Reference,
    string? Note,
    bool IsCancelled,
    string? CancelReason,
    string? RecordedBy);

/// <summary>One cabinet in full: where it stands, and every entry behind that date.</summary>
public sealed record SubscriptionCabinetReport(
    SubscriptionReportLine Cabinet,
    IReadOnlyList<SubscriptionReportEntry> Ledger);

/// <summary>
/// The core of the <c>subscription-report</c> console verb (AC-5.9): a read-only view of where every cabinet of the
/// deployment stands, and — for one cabinet — the ledger behind its date.
///
/// <para><b>Deliberately not DI-registered</b>, like <c>MoneyReconciliationService</c> and
/// <c>AdminPasswordRecoveryService</c> beside it: there is no HTTP-reachable vendor report (FR-6), and it lives here
/// so <c>UnitTests</c> — which references Application — can exercise it.</para>
///
/// <para><b>⚠️ It derives nothing itself.</b> Every verdict comes from <c>SubscriptionStateReader</c> and every
/// covered stretch from <c>SubscriptionLedger.FoldWithSpans</c> — the same two rules the gate, the screen, the
/// banner and the warning job read. A report that computed « is this cabinet expired? » its own way would be the
/// one place able to disagree with the product about who may work.</para>
///
/// <para><b>« Today » is a parameter</b>, for <c>SubscriptionWarningJob</c>'s reason: the thresholds and the
/// midnight they turn on are otherwise untestable, and midnight is the only boundary that matters for a date that
/// arrives by itself.</para>
/// </summary>
public class SubscriptionReportService
{
    /// <summary>The default lead window — the same figure the banner and the first warning use (AC-3.1/3.4).</summary>
    public static int DefaultWithinDays => SubscriptionStateReader.WarningWindowDays;

    private readonly IClinicSubscriptionRepository _subscriptions;

    public SubscriptionReportService(IClinicSubscriptionRepository subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public async Task<SubscriptionReport> RunAsync(
        DateTime clinicToday, int withinDays, CancellationToken cancellationToken = default)
    {
        var rows = await _subscriptions.GetForReportAsync(cancellationToken);

        var expiring = new List<SubscriptionReportLine>();
        var expired = new List<SubscriptionReportLine>();
        var suspended = new List<SubscriptionReportLine>();
        var missing = new List<SubscriptionReportLine>();
        var healthy = new List<SubscriptionReportLine>();

        foreach (var row in rows)
        {
            var line = Describe(row, clinicToday);

            var bucket = line.State switch
            {
                null => missing,
                SubscriptionState.Suspended => suspended,
                SubscriptionState.Expired => expired,
                _ when line.DaysRemaining is { } days && days <= withinDays => expiring,
                _ => healthy,
            };

            bucket.Add(line);
        }

        // Soonest first inside each group: the cabinet that stops working first is the one to act on.
        expiring.Sort((a, b) => Nullable.Compare(a.EndsOn, b.EndsOn));
        expired.Sort((a, b) => Nullable.Compare(a.EndsOn, b.EndsOn));

        return new SubscriptionReport(
            clinicToday.Date, withinDays, rows.Count, expiring, expired, suspended, missing, healthy);
    }

    /// <summary>
    /// One cabinet and its whole ledger — the read behind « which entry do I cancel? », which no deployment-wide
    /// listing can answer because the entry ids are the thing being looked up.
    /// </summary>
    public async Task<SubscriptionCabinetReport?> RunForCabinetAsync(
        Guid clinicId, DateTime clinicToday, CancellationToken cancellationToken = default)
    {
        var row = (await _subscriptions.GetForReportAsync(cancellationToken))
            .FirstOrDefault(r => r.ClinicId == clinicId);

        if (row is null)
        {
            return null;
        }

        var entries = await _subscriptions.GetEntriesAsync(clinicId, cancellationToken);
        var spans = SubscriptionLedger.FoldWithSpans(entries.Select(e => e.ToLedgerEntry())).Spans;
        var spanByEntry = spans.ToDictionary(s => s.EntryId);

        var ledger = entries
            .Select(e =>
            {
                var span = spanByEntry.TryGetValue(e.Id, out var found) ? found : null;
                return new SubscriptionReportEntry(
                    e.Id,
                    e.Kind,
                    SubscriptionLabels.PeriodKind(e.Kind),
                    e.RecordedOnClinicDay,
                    span?.FromDay,
                    span?.ThroughDay,
                    e.Amount,
                    e.Method is { } method ? SubscriptionLabels.PaymentMethod(method) : null,
                    e.Reference,
                    e.Note,
                    e.IsCancelled,
                    e.CancelReason,
                    e.RecordedBy);
            })
            .ToList();

        return new SubscriptionCabinetReport(Describe(row, clinicToday), ledger);
    }

    private static SubscriptionReportLine Describe(ClinicSubscriptionReportRow row, DateTime clinicToday)
    {
        if (row.Subscription is null)
        {
            // Never « Actif » by omission: a cabinet with no entitlement is refused by the gate under
            // subscription_missing (EC-6), so the report has to say the same thing the cabinet is being told.
            return new SubscriptionReportLine(
                row.ClinicId, row.ClinicName, null, "Aucun abonnement",
                null, null, AllowsWrites: false, null, null);
        }

        var status = SubscriptionStateReader.Read(row.Subscription, clinicToday);

        return new SubscriptionReportLine(
            row.ClinicId,
            row.ClinicName,
            status.State,
            SubscriptionLabels.State(status.State),
            status.EndsOn,
            status.DaysRemaining,
            status.AllowsWrites,
            row.Subscription.Plan is { } plan ? SubscriptionLabels.Plan(plan) : null,
            row.Subscription.SuspensionReason);
    }
}
