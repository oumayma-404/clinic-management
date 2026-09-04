using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Billing;

/// <summary>
/// [AC-12a][AC-12b][AC-12c] The three outstanding-balance reads — « Solde patient », « Créances » and the
/// dashboard KPI — must report the same figure for the same data. They did not: only the per-patient summary
/// de-duplicated a plan already bridged into an invoice, and the two clinic-wide reads additionally counted
/// Draft devis as debt.
/// <para>
/// One fixture drives all three. The repository mocks deliberately reimplement what
/// <c>TreatmentPlanRepository</c> / <c>InvoiceRepository</c> do in SQL (the status filters and the
/// excluded-plan filter), so the test proves the <b>handlers</b> feed those repositories the same rule —
/// which is the part that was actually broken.
/// </para>
/// <para>
/// [J5] It now covers the <b>cash</b> side to the same standard: la caisse, the dashboard's « Encaissé » AND
/// « Total encaissé » on <c>/factures</c>. That third read is why the extension was necessary rather than
/// optional — it counted invoice payments only while both siblings added devis instalments, and it survived
/// precisely because this file pinned <i>two</i> of the three. A consistency test that omits one read cannot
/// catch the read that drifts, so « extend it » is the fix and « add a parallel class » would have repeated
/// the mistake.
/// </para>
/// </summary>
public class MoneyReadConsistencyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Auth0Sub = "auth0|dashboard-user";

    /// <summary>Fixed so the dashboard and the caisse are compared over provably identical bounds.</summary>
    private static readonly DateTime FixedNow = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IExpenseRepository> _expenses = new();
    private readonly Mock<ICnamBillingCalculator> _cnam = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    // No avoirs in these fixtures. Stated explicitly rather than left to Moq's default, because the
    // batch read returns a dictionary the handlers immediately enumerate.
    private readonly Mock<ICreditNoteRepository> _creditNotes = NoCreditNotes();

    private static Mock<ICreditNoteRepository> NoCreditNotes()
    {
        var mock = new Mock<ICreditNoteRepository>();
        mock.Setup(r => r.GetTotalsForInvoicesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());
        return mock;
    }

    private static Patient PatientFixture() => new(
        PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    /// <summary>An accepted 1 000 DT devis with a single unpaid lump-sum échéance.</summary>
    private static TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }) });
        plan.Accept("2026-0014");
        return plan;
    }

    /// <summary>A Draft devis with a hand-built 1 000 DT échéancier — a quote, never debt (AC-12b).</summary>
    private static TreatmentPlan DraftPlanWithSchedule()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis en attente");
        plan.SetItems(new[] { ("Implant", 1000m, (IReadOnlyList<int>)new[] { 21 }) });
        plan.SetInstallments(new[]
        {
            (new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 400m),
            (new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 600m),
        });
        return plan;
    }

    /// <summary>The devis→facture bridge: an issued 1 000 DT note carrying the plan link, nothing collected.</summary>
    private static Invoice BridgeInvoiceFor(TreatmentPlan plan)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, treatmentPlanId: plan.Id);
        invoice.SetLines(new[] { ("Couronne", 1, 1000m) });
        invoice.Issue("2026-0031");
        return invoice;
    }

    /// <summary>
    /// Wire every repository the three handlers use, mirroring the real implementations' filters so the
    /// figures below are produced by the same rules production would apply.
    /// </summary>
    private void Wire(IReadOnlyList<Invoice> invoices, IReadOnlyList<TreatmentPlan> plans)
    {
        // L8 slice B — the caisse summary now also reads the per-method breakdown. Moq returns `null` for an
        // unstubbed Task<IReadOnlyList<T>>, which the handler's merge dereferences, so an unstubbed read turns every
        // assertion in this file into « Result.IsSuccess == false ». Empty lists reproduce the original behaviour:
        // the four totals are unchanged and the breakdown is all zeros, which no test here asserts on.
        _invoices.Setup(r => r.GetCollectedByMethodBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PaymentMethodTotal>());
        _plans.Setup(r => r.GetInstallmentCollectedByMethodBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PaymentMethodTotal>());
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, ClinicId, "doctor"));

        var patient = PatientFixture();
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        // Mirrors PatientRepository.GetByIdsAsync: the requested ids, narrowed to the clinic. « Créances »
        // resolves its names through this batch (AC-P6.21) instead of one GetByIdAsync per row — and per R-10
        // this fake is part of the repository's contract, so it moves with it or the suite passes against the
        // old shape.
        _patients.Setup(r => r.GetByIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, Patient>)(ids.Contains(PatientId)
                    ? new Dictionary<Guid, Patient> { [PatientId] = patient }
                    : new Dictionary<Guid, Patient>()));
        _patients.Setup(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _patients.Setup(r => r.CountFlaggedByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _appointments.Setup(r => r.CountByClinicIdAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AppointmentStatus?>(),
                It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // « Solde patient » reads the aggregates directly.
        _invoices.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<InvoiceStatus?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((invoices).AsPage());
        _plans.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((plans).AsPage());

        // Mirrors InvoiceRepository.GetOutstandingByPatientAsync: issued, non-cancelled, balance > 0 — and,
        // since J7, the oldest issue date among the patient's unpaid notes, which is what ages invoice debt.
        var invoiceOutstanding = invoices
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
            .GroupBy(i => i.PatientId)
            .Select(g => (
                PatientId: g.Key,
                Outstanding: g.Sum(i => i.Outstanding),
                OldestUnpaidIssueDate: g.Where(i => i.Outstanding > 0m).Min(i => i.IssueDate)))
            .Where(r => r.Outstanding > 0m)
            .ToList();
        _invoices.Setup(r => r.GetOutstandingByPatientAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(Guid, decimal, DateTime?)>)invoiceOutstanding);

        // Mirrors InvoiceRepository.GetTreatmentPlanLinksAsync (cancelled bridges included — the caller decides).
        var links = invoices
            .Where(i => i.TreatmentPlanId.HasValue)
            .Select(i => (
                TreatmentPlanId: i.TreatmentPlanId!.Value,
                InvoiceId: i.Id,
                i.Number,
                i.Status,
                i.TotalTtc,
                i.Outstanding))
            .ToList();
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>)links);

        // Cash defaults. The outstanding-balance tests below are unaffected by these; the caisse-agreement test
        // overrides them with real figures so both reads have something non-trivial to disagree about.
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _invoices.Setup(r => r.GetInvoicedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _expenses.Setup(r => r.GetTotalBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        // Mirrors TreatmentPlanRepository.GetInstallmentOutstandingByPatientAsync: committed plans only,
        // minus whatever the caller passes as already-billed.
        _plans.Setup(r => r.GetInstallmentOutstandingByPatientAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateTime asOf, IReadOnlyCollection<Guid> excluded, CancellationToken _) =>
                (IReadOnlyList<(Guid, decimal, DateTime?)>)plans
                    .Where(p => PlanBillingRules.CarriesDebt(p.Status) && !excluded.Contains(p.Id))
                    .SelectMany(p => p.Installments
                        .Where(i => i.Amount > i.AmountPaid)
                        .Select(i => new { p.PatientId, Open = i.Amount - i.AmountPaid, i.DueDate }))
                    .GroupBy(r => r.PatientId)
                    .Select(g => (
                        PatientId: g.Key,
                        Outstanding: g.Sum(x => x.Open),
                        // Calendar-day comparison, mirroring the real repository — an échéance due today is
                        // not overdue (see InstallmentOverdueBoundaryTests).
                        OldestOverdue: g.Where(x => x.DueDate.Date < asOf.Date).Select(x => (DateTime?)x.DueDate).Min()))
                    .Where(r => r.Outstanding > 0m)
                    .ToList());

        // CNAM is indicative only and irrelevant to the balance — everything out of pocket.
        _cnam.Setup(c => c.ComputeAsync(
                It.IsAny<IReadOnlyCollection<CnamBillingLine>>(), It.IsAny<decimal>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<CnamBillingLine> _, decimal total, DateTime? _, DateTime _, CancellationToken _)
                => new CnamSplit(0m, total));
    }

    private async Task<decimal> SoldePatientAsync()
    {
        var handler = new GetPatientBillingSummaryQueryHandler(
            _invoices.Object, _plans.Object, _patients.Object, _creditNotes.Object, _cnam.Object,
            _clinicResolver.Object, NullLogger<GetPatientBillingSummaryQueryHandler>.Instance);
        var result = await handler.Handle(
            new GetPatientBillingSummaryQuery { PatientId = PatientId }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.TotalOutstanding;
    }

    private async Task<decimal> CreancesAsync()
    {
        var handler = new GetReceivablesQueryHandler(
            _invoices.Object, _plans.Object, _patients.Object, _clinicResolver.Object,
            NullLogger<GetReceivablesQueryHandler>.Instance);
        var result = await handler.Handle(new GetReceivablesQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Items.Sum(r => r.TotalOutstanding);
    }

    /// <summary>
    /// The dashboard's money section, read through the same reader the composed <c>GetDashboardQuery</c> uses. The
    /// reader is exercised directly rather than through the handler because the handler additionally reads activity,
    /// alerts and the trend, none of which this test is about — and mocking six more repositories to assert one
    /// money figure would bury the thing being proved.
    /// </summary>
    private async Task<(DashboardMoneyDto Money, DashboardReceivablesDto Receivables)> DashboardMoneyAsync()
    {
        var reader = new DashboardMoneyReader(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object);

        return await reader.ReadAsync(
            ClinicId, DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow), FixedNow, doctorId: null, cancellationToken: CancellationToken.None);
    }

    private async Task<decimal> DashboardOutstandingAsync() => (await DashboardMoneyAsync()).Receivables.Total;

    private async Task<CaisseSummaryDto> CaisseAsync(DashboardPeriod period)
    {
        var handler = new GetCaisseSummaryQueryHandler(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetCaisseSummaryQueryHandler>.Instance);
        var result = await handler.Handle(
            new GetCaisseSummaryQuery { From = period.From, To = period.ToInclusive }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    /// <summary>
    /// « Total encaissé » on <c>/factures</c> — the <b>third</b> cash read, and the one J5 brought into the
    /// contract. <c>null</c> bounds exercise the no-period branch the page actually loads with.
    /// </summary>
    private async Task<InvoiceRevenueDto> RevenueAsync(DashboardPeriod? period = null)
    {
        var handler = new GetInvoiceRevenueQueryHandler(
            _invoices.Object, _plans.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoiceRevenueQueryHandler>.Instance);
        var result = await handler.Handle(
            new GetInvoiceRevenueQuery { From = period?.From, To = period?.ToInclusive }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    // [AC-12a] A plan bridged to an issued invoice is counted ONCE, through the invoice, on all three reads.
    // Before the fix « Créances » and the dashboard reported 2 000 DT for this fixture while « Solde patient »
    // reported 1 000 — the same patient owing two different amounts on two screens.
    [Fact]
    public async Task A_Bridged_Plan_Is_Counted_Once_On_All_Three_Reads()
    {
        var plan = AcceptedPlan();
        Wire(new[] { BridgeInvoiceFor(plan) }, new[] { plan });

        var solde = await SoldePatientAsync();
        var creances = await CreancesAsync();
        var dashboard = await DashboardOutstandingAsync();

        Assert.Equal(1000m, solde);
        Assert.Equal(solde, creances);
        Assert.Equal(solde, dashboard);
    }

    // [AC-12b] A Draft devis with a hand-built échéancier contributes 0 everywhere — B1's "a Draft devis is
    // not debt" was previously applied only to « Solde patient ».
    [Fact]
    public async Task A_Draft_Plan_With_A_Schedule_Contributes_Zero_Everywhere()
    {
        var draft = DraftPlanWithSchedule();
        Wire(Array.Empty<Invoice>(), new[] { draft });

        Assert.Equal(0m, await SoldePatientAsync());
        Assert.Equal(0m, await CreancesAsync());
        Assert.Equal(0m, await DashboardOutstandingAsync());
    }

    // [AC-12a] An un-bridged accepted plan is real debt and must still be counted — the de-dup must not
    // quietly swallow plans that have no invoice.
    [Fact]
    public async Task An_Unbridged_Accepted_Plan_Is_Counted_On_All_Three_Reads()
    {
        var plan = AcceptedPlan();
        Wire(Array.Empty<Invoice>(), new[] { plan });

        var solde = await SoldePatientAsync();

        Assert.Equal(1000m, solde);
        Assert.Equal(solde, await CreancesAsync());
        Assert.Equal(solde, await DashboardOutstandingAsync());
    }

    // [AC-12a] Cancelling the bridge invoice re-opens the plan: the invoice stops counting, the plan starts
    // again, and the three reads stay equal through the transition (a balance must never vanish).
    [Fact]
    public async Task Cancelling_The_Bridge_Invoice_Returns_The_Plan_To_The_Balance()
    {
        var plan = AcceptedPlan();
        var invoice = BridgeInvoiceFor(plan);
        invoice.Cancel("Devis à revoir");
        Wire(new[] { invoice }, new[] { plan });

        var solde = await SoldePatientAsync();

        Assert.Equal(1000m, solde);
        Assert.Equal(solde, await CreancesAsync());
        Assert.Equal(solde, await DashboardOutstandingAsync());
    }

    // [AC-12a] A draft bridge invoice does not represent the plan yet, so the plan keeps its balance and the
    // total does not double between "invoice created" and "invoice issued".
    [Fact]
    public async Task A_Draft_Bridge_Invoice_Leaves_The_Plan_Counted_Once()
    {
        var plan = AcceptedPlan();
        var draftInvoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, treatmentPlanId: plan.Id);
        draftInvoice.SetLines(new[] { ("Couronne", 1, 1000m) });
        Wire(new[] { draftInvoice }, new[] { plan });

        var solde = await SoldePatientAsync();

        Assert.Equal(1000m, solde);
        Assert.Equal(solde, await CreancesAsync());
        Assert.Equal(solde, await DashboardOutstandingAsync());
    }

    // [AC-12c] The clinic-wide reads must actually feed the shared rule's output to the repository — this is
    // the wiring that used to be missing entirely.
    [Fact]
    public async Task Both_ClinicWide_Reads_Pass_The_Billed_Plan_Ids_To_The_Repository()
    {
        var plan = AcceptedPlan();
        Wire(new[] { BridgeInvoiceFor(plan) }, new[] { plan });

        await CreancesAsync();
        await DashboardOutstandingAsync();

        _plans.Verify(r => r.GetInstallmentOutstandingByPatientAsync(
                ClinicId,
                It.IsAny<DateTime>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(plan.Id)),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // [AC-6] The fourth money read joins the contract. Over the SAME window and the SAME rows, the dashboard's
    // « Encaissé / Dépenses / Net » must equal la caisse's cashIn / cashOut / net. The dashboard grew these three
    // figures in dashboard-insights, and la caisse had reported them for a year — two screens computing cash from
    // the same ledgers is exactly the shape that drifted before (the dashboard KPI used to omit avoirs while the
    // caisse netted them, so the same month showed two different figures).
    [Fact]
    public async Task Dashboard_Cash_Figures_Equal_La_Caisse_Over_The_Same_Window()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        // Non-trivial figures on every component, including an avoir — the refund is the term that was missing
        // from the dashboard side, so a fixture without one could not catch the original defect.
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(4200.500m);
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(800.250m);
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(150.750m);
        _expenses.Setup(r => r.GetTotalBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1300.000m);

        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);
        var (money, _) = await DashboardMoneyAsync();
        var caisse = await CaisseAsync(period);

        // FOUR figures now, not three. `CashIn` is gross and the avoir has its own line on both reads — the split
        // arrived with the caisse statement, which shows a refund as money leaving and so could not be reconciled
        // against a total that had already absorbed it.
        Assert.Equal(caisse.CashIn, money.Collected.Current);
        Assert.Equal(caisse.Refunds, money.Refunds.Current);
        Assert.Equal(caisse.CashOut, money.Expenses.Current);
        Assert.Equal(caisse.Net, money.Net.Current);

        // And the dashboard's own figures must be internally consistent, or a caisse that does not add up is
        // shown to the user even when each figure is individually right.
        Assert.Equal(
            money.Collected.Current - money.Refunds.Current - money.Expenses.Current,
            money.Net.Current);
    }

    // [AC-6] The avoir is genuinely accounted for, not merely equal-by-coincidence to a caisse that also ignores it.
    // It is no longer *netted into* « Encaissé » — it is its own reported figure, and « Net » is what nets it. That
    // reversal is deliberate: a statement lists a refund as money leaving, so a gross Collected is the only version
    // the lines can be reconciled against. A month with a large avoir also used to just look like a weak month.
    [Fact]
    public async Task Dashboard_Reports_Avoirs_Separately_And_Nets_Them_In_Net()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1000m);
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(250m);

        var (money, _) = await DashboardMoneyAsync();

        Assert.Equal(1000m, money.Collected.Current);
        Assert.Equal(250m, money.Refunds.Current);
        Assert.Equal(750m, money.Net.Current);
    }

    // [AC-P6.11] § 6.2 arrives already closed by the § 1 merge, so this is a REGRESSION pin, not a fix. The two
    // cases above cover an avoir inside a window with real receipts; this one is the loud case: a window whose only
    // movement is a refund, where netting it takes cash-in NEGATIVE. A read that quietly dropped the refund term
    // would report 0 here — indistinguishable from « rien encaissé » — while its sibling reported −180,500.
    [Fact]
    public async Task Dashboard_And_Caisse_Agree_When_A_Window_Holds_Only_A_Refund() // [AC-P6.11]
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(180.500m);

        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);
        var (money, _) = await DashboardMoneyAsync();
        var caisse = await CaisseAsync(period);

        // With the split, a refund-only window reads honestly: nothing came in, 180,500 went out, net is negative.
        // Under the old netting this was a *negative CashIn*, which is not a thing a till can have.
        Assert.Equal(0m, caisse.CashIn);
        Assert.Equal(180.500m, caisse.Refunds);
        Assert.Equal(-180.500m, caisse.Net);
        Assert.Equal(caisse.CashIn, money.Collected.Current);
        Assert.Equal(caisse.Refunds, money.Refunds.Current);
        Assert.Equal(caisse.Net, money.Net.Current);
    }

    // [AC-P6.3] La caisse with no arguments means the CLINIC's day, not the UTC one. The browser callers always
    // sent their own bounds, so this defect only ever bit a direct API caller — but it sat in the read the clinic
    // reconciles its till against, and « aujourd'hui » running 01:00 to 01:00 is not a thing anyone would notice
    // until a payment went missing from the day it was taken.
    [Fact]
    public async Task La_Caisse_Defaults_To_The_Clinic_Local_Day() // [AC-P6.3]
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        DateTime? askedFrom = null;
        DateTime? askedTo = null;
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime from, DateTime to, Guid? _, CancellationToken _) => { askedFrom = from; askedTo = to; })
            .ReturnsAsync(0m);

        var handler = new GetCaisseSummaryQueryHandler(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetCaisseSummaryQueryHandler>.Instance);

        var result = await handler.Handle(new GetCaisseSummaryQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var (expectedFrom, expectedTo) = ClinicClock.TodayRangeUtc();
        Assert.Equal(expectedFrom, askedFrom);
        Assert.Equal(expectedTo, askedTo);

        // The bounds are a clinic-local day, so the lower one is 23:00 UTC — an hour before UTC midnight. That is
        // the assertion the old `DateTime.UtcNow.Date` default fails.
        Assert.Equal(23, askedFrom!.Value.Hour);
        // Inclusive on both ends (finding #20): the upper bound is inside the day, never the next midnight.
        Assert.True(askedTo < ClinicClock.EndOfLocalDayUtc(ClinicClock.ClinicToday()));
    }

    // ------------------------------------------------------------------ [J5] the THIRD cash read

    /*
     * [J5] « Total encaissé » on /factures joins la caisse and the dashboard.
     *
     * It counted **invoice payments only** while both siblings added devis instalments, so a practice collecting
     * on an échéancier saw a smaller figure on /factures than on the two screens beside it, with nothing to
     * explain the gap. And the reason it survived is written into this very file: the test that existed pinned
     * caisse↔dashboard and **never touched the third read**. A consistency test that covers two of three reads
     * does not catch the one that drifts — which is the whole argument for extending it rather than adding a
     * parallel class.
     *
     * The arithmetic relating them: la caisse reports `CashIn` **gross** with `Refunds` as its own field, while
     * /factures reports a single net « encaissé ». So the contract is
     *     revenue.TotalCollected == caisse.CashIn − caisse.Refunds
     * and that identity is what these tests hold.
     */

    // [J5] The load-bearing case: all THREE cash reads over one window, one fixture, non-trivial figures on every
    // component including an avoir and a plan instalment — the two terms /factures used to be missing.
    [Fact]
    public async Task All_Three_Cash_Reads_Agree_Over_The_Same_Window()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(4200.500m);
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(800.250m);
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(150.750m);
        _expenses.Setup(r => r.GetTotalBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1300.000m);

        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);
        var (money, _) = await DashboardMoneyAsync();
        var caisse = await CaisseAsync(period);
        var revenue = await RevenueAsync(period);

        // The two that already agreed.
        Assert.Equal(caisse.CashIn, money.Collected.Current);
        Assert.Equal(caisse.Refunds, money.Refunds.Current);

        // And the third. 4200,500 + 800,250 − 150,750 = 4850,000.
        Assert.Equal(4850.000m, revenue.TotalCollected);
        Assert.Equal(caisse.CashIn - caisse.Refunds, revenue.TotalCollected);
        Assert.Equal(money.Collected.Current - money.Refunds.Current, revenue.TotalCollected);
    }

    // [J5] The defect itself, isolated: a window whose ONLY cash is a devis instalment. /factures used to report
    // 0 here while la caisse reported 800,250 — the same money, two screens, one of them saying « rien encaissé ».
    [Fact]
    public async Task Revenue_Counts_A_Window_Whose_Only_Cash_Is_A_Devis_Instalment()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(800.250m);

        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);
        var revenue = await RevenueAsync(period);
        var caisse = await CaisseAsync(period);

        Assert.Equal(800.250m, revenue.TotalCollected);
        Assert.Equal(caisse.CashIn, revenue.TotalCollected);
    }

    // [J5] The plan side must go through the SAME billed-plan de-dup its siblings use, or a devis bridged into a
    // note would have its carried-over payments counted twice — once on the invoice track, once on the plan.
    [Fact]
    public async Task Revenue_Passes_The_Billed_Plan_Ids_To_The_Installment_Read()
    {
        var plan = AcceptedPlan();
        Wire(new[] { BridgeInvoiceFor(plan) }, new[] { plan });

        await RevenueAsync(DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow));

        _plans.Verify(r => r.GetInstallmentCollectedBetweenAsync(
                ClinicId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(plan.Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [J5] The no-period branch is what /factures loads on arrival (both date filters start empty), so it is the
    // figure nearly every user actually sees — and it must count instalments too. Asserted separately because it
    // is a genuinely different code path: it has no date-free plan aggregate, so it asks for the whole time axis.
    [Fact]
    public async Task Revenue_Without_A_Period_Also_Counts_Instalments()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(640.000m);

        var revenue = await RevenueAsync();

        Assert.Equal(640.000m, revenue.TotalCollected);
    }

    // [J5] Every figure leaves the read rounded through the one money authority. This was the only money read that
    // did not, so a sum of two ledgers could print a fourth decimal the rest of the product never shows.
    [Fact]
    public async Task Revenue_Is_Rounded_To_The_Millime()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        // Two ledgers whose sum has a fourth decimal — 100,00005 + 0,00005 = 100,0001.
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(100.00005m);
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(0.00005m);

        var revenue = await RevenueAsync(DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow));

        // 100,000 — not 100,0001. Decimal equality is exact, so this assertion alone proves the rounding ran.
        Assert.Equal(100.000m, revenue.TotalCollected);
    }

    // ------------------------------------------------------------------ the FIFTH read: the vendor console

    /*
     * [platform-console AC-2.1] « Encaissé par le cabinet » on the vendor's portfolio joins the four reads above.
     *
     * It is the only one of the five read by somebody who is NOT at the practice, which is what makes drift here
     * worse than anywhere else: the vendor quotes a cabinet its own turnover, the cabinet opens its caisse, and
     * the two numbers disagree with nothing able to say which is right. Extending this file rather than writing a
     * parallel class is the same argument J5 made — a consistency test that covers four of five reads does not
     * catch the fifth.
     *
     * The contract, since la caisse reports CashIn gross with Refunds beside it:
     *     console == caisse.CashIn − caisse.Refunds == revenue.TotalCollected
     */

    /// <summary>The console's figure, through the reader <c>ClinicActivityCounterJob</c> itself calls.</summary>
    private async Task<decimal> ConsoleCollectedAsync(DashboardPeriod period) =>
        await PlatformCollectedReader.ReadAsync(
            _invoices.Object, _plans.Object, _creditNotes.Object,
            ClinicId, period.From, period.ToInclusive, CancellationToken.None);

    // [AC-2.1] All FIVE cash reads over one window and one fixture, with an avoir and a plan instalment in play —
    // the two terms a hand-written SUM in the counter job would most plausibly have omitted.
    [Fact]
    public async Task The_Consoles_Cabinet_Turnover_Equals_That_Cabinets_Own_Caisse()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(4200.500m);
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(800.250m);
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(150.750m);
        // Non-zero on purpose: the console must NOT subtract the practice's costs. « Encaissé » is what came in,
        // and a vendor reporting a cabinet's profit would be reporting something it has no business knowing.
        _expenses.Setup(r => r.GetTotalBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1300.000m);

        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);
        var console = await ConsoleCollectedAsync(period);
        var caisse = await CaisseAsync(period);
        var revenue = await RevenueAsync(period);

        Assert.Equal(4850.000m, console);
        Assert.Equal(caisse.CashIn - caisse.Refunds, console);
        Assert.Equal(revenue.TotalCollected, console);
        // Expenses left the figure alone: 4850 with 1300 of costs in the window is still 4850.
        Assert.NotEqual(caisse.Net, console);
    }

    // [AC-2.1] The bridged-plan de-dup reaches the console's read too. Without it one physical payment carried
    // onto a bridge invoice is counted twice, and the cabinet the vendor is about to call about « growth » simply
    // has a devis that became a note.
    [Fact]
    public async Task The_Consoles_Read_Passes_The_Billed_Plan_Ids_To_The_Installment_Read()
    {
        var plan = AcceptedPlan();
        Wire(new[] { BridgeInvoiceFor(plan) }, new[] { plan });

        await ConsoleCollectedAsync(DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow));

        _plans.Verify(r => r.GetInstallmentCollectedBetweenAsync(
                ClinicId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(plan.Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-3][AC-6] The previous window is read with its OWN bounds, not the current ones. Without this the delta on
    // every money card would be a comparison of a figure against itself — a feature that looks present and is inert.
    [Fact]
    public async Task Dashboard_Reads_The_Previous_Window_With_Its_Own_Bounds()
    {
        Wire(Array.Empty<Invoice>(), Array.Empty<TreatmentPlan>());

        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);

        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, period.From, period.ToInclusive, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1000m);
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, period.PreviousFrom, period.PreviousToInclusive, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(800m);

        var (money, _) = await DashboardMoneyAsync();

        Assert.Equal(1000m, money.Collected.Current);
        Assert.Equal(800m, money.Collected.Previous);
        Assert.Equal(25.0m, money.Collected.DeltaPercent);
    }
}
