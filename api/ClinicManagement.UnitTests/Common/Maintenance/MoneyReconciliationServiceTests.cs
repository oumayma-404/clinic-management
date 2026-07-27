using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using Moq;

namespace ClinicManagement.UnitTests.Common.Maintenance;

/// <summary>
/// [AC-74] The reconciliation report runs read-only and reports every check in the slice-H list.
/// [AC-75] Its figures are the instrument the money migrations are verified against, so each check must
/// distinguish "these two agree" from "these two do not" precisely — a false clean is worse than no report.
/// </summary>
public class MoneyReconciliationServiceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PlanId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid InvoiceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IMoneyReconciliationReader> _reader = new();

    private MoneyReconciliationService CreateService() => new(_reader.Object);

    /// <summary>A clinic where everything agrees. Individual tests override one facet at a time.</summary>
    private static ClinicMoneyFacts CleanClinic(
        decimal paymentRowSum = 1000m,
        decimal invoiceAmountCollectedSum = 1000m,
        decimal installmentAmountPaidSum = 600m,
        decimal? installmentLedgerSum = null,
        IReadOnlyList<PlanScheduleFact>? planSchedules = null,
        IReadOnlyList<MonthlyCollectedFact>? monthly = null,
        IReadOnlyList<ContactValueFact>? contacts = null,
        IReadOnlyList<OverCreditedInvoiceFact>? overCredited = null,
        IReadOnlyList<DuplicateBridgeFact>? duplicateBridges = null) =>
        new(ClinicId,
            "Cabinet Test",
            paymentRowSum,
            invoiceAmountCollectedSum,
            installmentAmountPaidSum,
            // Defaults to matching the denormalization: a clean clinic has ledger == AmountPaid.
            installmentLedgerSum ?? installmentAmountPaidSum,
            planSchedules ?? new[] { new PlanScheduleFact(PlanId, "2026-0001", 1000m, 1000m) },
            monthly ?? Array.Empty<MonthlyCollectedFact>(),
            contacts ?? new[] { new ContactValueFact("sonia@example.tn", "20123456") },
            overCredited ?? Array.Empty<OverCreditedInvoiceFact>(),
            duplicateBridges ?? Array.Empty<DuplicateBridgeFact>());

    private void Arrange(ClinicMoneyFacts clinic, OrphanFacts? orphans = null) =>
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MoneyReconciliationFacts(
                new[] { clinic },
                orphans ?? new OrphanFacts(0, 0, 0, 0)));

    private static MoneyReconciliationFinding Finding(MoneyReconciliationReport report, string check) =>
        report.Findings.Single(f => f.Check == check);

    // [AC-74] A clinic whose figures all agree reports no drift at all.
    [Fact]
    public async Task A_Consistent_Clinic_Reports_No_Drift()
    {
        Arrange(CleanClinic());

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
        Assert.All(report.Findings, f => Assert.Equal(MoneyReconciliationSeverity.Info, f.Severity));
    }

    // [AC-74] The two invoice ledgers — the Payment rows and the AmountCollected column — are compared.
    // Nothing in the app has ever reconciled them, so a historical drift is invisible until this runs.
    [Fact]
    public async Task Disagreeing_Invoice_Ledgers_Are_Reported_As_Drift()
    {
        Arrange(CleanClinic(paymentRowSum: 1000m, invoiceAmountCollectedSum: 950m));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "invoice-ledgers-agree");
        Assert.Equal(MoneyReconciliationSeverity.Drift, finding.Severity);
        Assert.Contains("50", finding.Detail);
        Assert.True(report.HasDrift);
    }

    // [AC-74] A millime of difference still counts — money comparisons round to 3 decimals, not to the dinar.
    [Fact]
    public async Task A_Single_Millime_Of_Ledger_Difference_Is_Drift()
    {
        Arrange(CleanClinic(paymentRowSum: 1000.001m, invoiceAmountCollectedSum: 1000m));

        var report = await CreateService().RunAsync();

        Assert.Equal(MoneyReconciliationSeverity.Drift, Finding(report, "invoice-ledgers-agree").Severity);
    }

    // [AC-74] Σ installment.Amount must equal the plan's TotalPlanned; « Solde patient » and « Créances »
    // read those two different ways and agree only while the invariant holds.
    [Fact]
    public async Task A_Plan_Whose_Schedule_Does_Not_Sum_To_Its_Total_Is_Drift()
    {
        Arrange(CleanClinic(planSchedules: new[]
        {
            new PlanScheduleFact(PlanId, "2026-0007", TotalPlanned: 1200m, InstallmentSum: 1000m)
        }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "plan-schedule-balances");
        Assert.Equal(MoneyReconciliationSeverity.Drift, finding.Severity);
        Assert.Contains("2026-0007", finding.Detail);
    }

    // [AC-47] All four sentinel literals are counted, so the post-migration run can prove they reached zero.
    [Theory]
    [InlineData("noemail@example.com", null)]
    [InlineData("unknown@example.com", null)]
    [InlineData(null, "0000000000")]
    [InlineData(null, "000-000-0000")]
    public async Task Each_Sentinel_Literal_Is_Counted(string? email, string? phone)
    {
        Arrange(CleanClinic(contacts: new[]
        {
            new ContactValueFact(email ?? "real@example.tn", phone ?? "20123456")
        }));

        var report = await CreateService().RunAsync();

        Assert.Equal(MoneyReconciliationSeverity.Drift, Finding(report, "contact-sentinels").Severity);
    }

    // [AC-47] A hand-typed placeholder like eight zeros is a DIFFERENT string the blanking migration will not
    // match — and it normalises to a deliverable +216 number, so the gateway gets billed for it. It must be
    // counted separately rather than silently folded into the sentinel total.
    [Fact]
    public async Task A_Near_Miss_Placeholder_Phone_Is_Counted_Separately()
    {
        Arrange(CleanClinic(contacts: new[]
        {
            new ContactValueFact("real@example.tn", "00000000"),      // near-miss: 8 zeros
            new ContactValueFact("real2@example.tn", "0000000000")    // exact sentinel: 10 zeros
        }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "contact-sentinels");
        Assert.Equal(MoneyReconciliationSeverity.Drift, finding.Severity);
        Assert.Contains("1 sentinel phone(s)", finding.Detail);
        Assert.Contains("1 near-miss placeholder phone(s)", finding.Detail);
    }

    // [AC-47] A real Tunisian number is neither a sentinel nor a near-miss.
    [Fact]
    public async Task A_Real_Phone_Is_Not_Flagged()
    {
        Arrange(CleanClinic(contacts: new[] { new ContactValueFact("sonia@example.tn", "20 123 456") }));

        var report = await CreateService().RunAsync();

        Assert.Equal(MoneyReconciliationSeverity.Info, Finding(report, "contact-sentinels").Severity);
    }

    // [AC-74] The avoir amount guard is a read-then-write with no unique index, so two concurrent avoirs can
    // credit more than the invoice ever collected.
    [Fact]
    public async Task An_Over_Credited_Invoice_Is_Drift()
    {
        Arrange(CleanClinic(overCredited: new[]
        {
            new OverCreditedInvoiceFact(InvoiceId, "2026-0031", AmountCollected: 300m, Credited: 400m)
        }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "credit-notes-within-collected");
        Assert.Equal(MoneyReconciliationSeverity.Drift, finding.Severity);
        Assert.Contains("2026-0031", finding.Detail);
    }

    // [AC-74] The devis→facture bridge refuses a second invoice with a read-then-write and no unique index.
    [Fact]
    public async Task A_Plan_Billed_Twice_Is_Drift()
    {
        Arrange(CleanClinic(duplicateBridges: new[]
        {
            new DuplicateBridgeFact(PlanId, "2026-0007", NonCancelledInvoiceCount: 2)
        }));

        var report = await CreateService().RunAsync();

        Assert.Equal(MoneyReconciliationSeverity.Drift, Finding(report, "one-bridge-invoice-per-plan").Severity);
    }

    // [AC-74] Invoices and treatment plans carry no FK to Patients, so a past cascading delete left rows
    // pointing at nothing while still counting toward « Créances ».
    [Fact]
    public async Task Orphaned_Rows_Are_Drift()
    {
        Arrange(CleanClinic(), new OrphanFacts(Invoices: 2, TreatmentPlans: 1, ToothStates: 0, Notifications: 0));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "no-orphaned-rows");
        Assert.Equal(MoneyReconciliationSeverity.Drift, finding.Severity);
        Assert.Contains("2 invoice(s)", finding.Detail);
    }

    // [AC-24] The monthly « encaissé » baseline is the line the installment-ledger migration is judged against.
    // It must be emitted per clinic-month, in chronological order, with both tracks kept separate.
    [Fact]
    public async Task The_Monthly_Baseline_Is_Emitted_In_Chronological_Order()
    {
        Arrange(CleanClinic(monthly: new[]
        {
            new MonthlyCollectedFact(2026, 2, 600m, 0m, 0m),
            new MonthlyCollectedFact(2026, 1, 0m, 400m, 400m)
        }));

        var report = await CreateService().RunAsync();

        Assert.Collection(report.MonthlyBaseline,
            first =>
            {
                Assert.Equal(1, first.Month);
                Assert.Equal(400m, first.InstallmentCollected);
                Assert.Equal(400m, first.Total);
            },
            second =>
            {
                Assert.Equal(2, second.Month);
                Assert.Equal(600m, second.InvoiceCollected);
                Assert.Equal(600m, second.Total);
            });
    }

    // [AC-23] The ledger and the AmountPaid denormalization it derives are written and backfilled together,
    // so any difference means they have diverged.
    [Fact]
    public async Task A_Ledger_That_Matches_The_Denormalization_Is_Clean()
    {
        Arrange(CleanClinic(installmentAmountPaidSum: 1234.567m, installmentLedgerSum: 1234.567m));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "installment-ledger-agrees");
        Assert.Equal(MoneyReconciliationSeverity.Info, finding.Severity);
        Assert.Contains("1234.567", finding.Detail);
    }

    // [AC-23] …and a difference is drift.
    [Fact]
    public async Task A_Ledger_That_Disagrees_With_The_Denormalization_Is_Drift()
    {
        Arrange(CleanClinic(installmentAmountPaidSum: 1000m, installmentLedgerSum: 950m));

        var report = await CreateService().RunAsync();

        Assert.Equal(MoneyReconciliationSeverity.Drift, Finding(report, "installment-ledger-agrees").Severity);
        Assert.True(report.HasDrift);
    }

    // [AC-24] THE check the ledger migration is judged against: every month must report the same installment
    // total computed both ways. A difference means the backfill moved money between months.
    [Fact]
    public async Task A_Month_Whose_Attribution_Moved_Is_Drift()
    {
        Arrange(CleanClinic(monthly: new[]
        {
            // Ledger says 400 in January; the old computation said 0 — the backfill moved a month.
            new MonthlyCollectedFact(2026, 1, 0m, 400m, 0m)
        }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "monthly-attribution-unchanged");
        Assert.Equal(MoneyReconciliationSeverity.Drift, finding.Severity);
        Assert.Contains("2026-01", finding.Detail);
    }

    // [AC-24] Identical figures both ways is the passing state.
    [Fact]
    public async Task Months_That_Report_The_Same_Total_Both_Ways_Are_Clean()
    {
        Arrange(CleanClinic(monthly: new[]
        {
            new MonthlyCollectedFact(2026, 1, 0m, 400m, 400m),
            new MonthlyCollectedFact(2026, 2, 600m, 600m, 600m)
        }));

        var report = await CreateService().RunAsync();

        Assert.Equal(MoneyReconciliationSeverity.Info, Finding(report, "monthly-attribution-unchanged").Severity);
        Assert.False(report.HasDrift);
    }

    // [AC-74] The report reads once and mutates nothing — there is no write seam on it at all.
    [Fact]
    public async Task The_Report_Reads_Once_And_Writes_Nothing()
    {
        Arrange(CleanClinic());

        await CreateService().RunAsync(monthsOfHistory: 12);

        _reader.Verify(r => r.ReadAsync(12, It.IsAny<CancellationToken>()), Times.Once);
        _reader.VerifyNoOtherCalls();
    }
}
