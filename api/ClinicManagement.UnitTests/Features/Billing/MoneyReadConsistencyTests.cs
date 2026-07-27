using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Application.Features.Dashboard.Queries;
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
/// </summary>
public class MoneyReadConsistencyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Auth0Sub = "auth0|dashboard-user";

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ICnamBillingCalculator> _cnam = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private static Patient PatientFixture() => new(
        PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    /// <summary>An accepted 1 000 DT devis with a single unpaid lump-sum échéance.</summary>
    private static TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", 1000m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });
        plan.Accept("2026-0014");
        return plan;
    }

    /// <summary>A Draft devis with a hand-built 1 000 DT échéancier — a quote, never debt (AC-12b).</summary>
    private static TreatmentPlan DraftPlanWithSchedule()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis en attente");
        plan.SetItems(new[] { ("Implant", 1000m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 21 }) });
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
        invoice.Issue("2026-0031", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);
        return invoice;
    }

    /// <summary>
    /// Wire every repository the three handlers use, mirroring the real implementations' filters so the
    /// figures below are produced by the same rules production would apply.
    /// </summary>
    private void Wire(IReadOnlyList<Invoice> invoices, IReadOnlyList<TreatmentPlan> plans)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, ClinicId, "doctor"));

        var patient = PatientFixture();
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        _patients.Setup(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _patients.Setup(r => r.CountFlaggedByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _appointments.Setup(r => r.CountByClinicIdAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AppointmentStatus?>(),
                It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // « Solde patient » reads the aggregates directly.
        _invoices.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<InvoiceStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);
        _plans.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(plans);

        // Mirrors InvoiceRepository.GetOutstandingByPatientAsync: issued, non-cancelled, balance > 0.
        var invoiceOutstanding = invoices
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
            .GroupBy(i => i.PatientId)
            .Select(g => (PatientId: g.Key, Outstanding: g.Sum(i => i.Outstanding)))
            .Where(r => r.Outstanding > 0m)
            .ToList();
        _invoices.Setup(r => r.GetOutstandingByPatientAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(Guid, decimal)>)invoiceOutstanding);

        // Mirrors InvoiceRepository.GetTreatmentPlanLinksAsync (cancelled bridges included — the caller decides).
        var links = invoices
            .Where(i => i.TreatmentPlanId.HasValue)
            .Select(i => (TreatmentPlanId: i.TreatmentPlanId!.Value, InvoiceId: i.Id, i.Number, i.Status))
            .ToList();
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(Guid, Guid, string?, InvoiceStatus)>)links);

        _invoices.Setup(r => r.GetCollectedBetweenAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
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
            _invoices.Object, _plans.Object, _patients.Object, _cnam.Object, _clinicResolver.Object,
            NullLogger<GetPatientBillingSummaryQueryHandler>.Instance);
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
        return result.Value!.Sum(r => r.TotalOutstanding);
    }

    private async Task<decimal> DashboardOutstandingAsync()
    {
        var handler = new GetDashboardStatsQueryHandler(
            _appointments.Object, _patients.Object, _users.Object, _invoices.Object, _plans.Object,
            _clinicContext.Object);
        var result = await handler.Handle(new GetDashboardStatsQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.TotalOutstanding;
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
}
