using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [AC-31][AC-32][AC-33] Issuing a devis→facture bridge carries the money already collected on the plan's
/// échéancier onto the invoice.
///
/// <para>
/// Without this, bridging a plan that had taken a deposit re-billed the patient for money they had already
/// paid: the bridge invoice was created with <c>AmountCollected = 0</c>, and the moment it left Draft the
/// plan's outstanding was suppressed everywhere — so the deposit vanished from the balance and reappeared as
/// invoice debt.
/// </para>
/// </summary>
public class BridgeCarryOverTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime January = new(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime February = new(2026, 2, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public BridgeCarryOverTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private IssueInvoiceCommandHandler CreateHandler() => new(
        _invoices.Object, _clinics.Object, _patients.Object, _plans.Object,
        _clinicResolver.Object, _uow.Object, NullLogger<IssueInvoiceCommandHandler>.Instance);

    /// <summary>An accepted plan for <paramref name="total"/> DT, registered with the repository mock.</summary>
    private TreatmentPlan PlanCollecting(decimal total, params (decimal Amount, DateTime PaidOn)[] payments)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(new[]
        {
            ("Couronne", total, (IReadOnlyList<int>)new[] { 11 }),
        });
        plan.Accept("2026-0001");

        var installment = plan.Installments.Single();
        foreach (var (amount, paidOn) in payments)
        {
            plan.RecordInstallmentPayment(installment.Id, amount, PaymentMethod.Cash, paidOn);
        }

        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plan;
    }

    /// <summary>A draft bridge invoice for the plan, billing <paramref name="total"/> DT.</summary>
    private Invoice BridgeDraft(TreatmentPlan plan, decimal total)
    {
        var invoice = new Invoice(
            Guid.NewGuid(), ClinicId, PatientId,
            dentalRecordId: null, appointmentId: null, treatmentPlanId: plan.Id);
        invoice.SetLines(new[] { ("Couronne", 1, total) });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    // [AC-31] THE fix: 1000 DT plan with 600 DT collected → the issued invoice reads 400 DT outstanding,
    // not 1000.
    [Fact]
    public async Task Issuing_Carries_The_Collected_Deposit_Onto_The_Invoice()
    {
        var plan = PlanCollecting(1000m, (600m, January));
        var invoice = BridgeDraft(plan, 1000m);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(600m, invoice.AmountCollected);
        // Asserted against TotalTtc rather than a literal: the clinic's default billing settings add 7 % TVA and
        // a 1,000 DT timbre fiscal at issue, so hard-coding 400 would be asserting the clinic's config rather
        // than the carry-over — and it would have broken the day J11 corrected that default, which is exactly
        // the churn writing it this way avoids.
        Assert.Equal(invoice.TotalTtc - 600m, invoice.Outstanding);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
    }

    // [AC-31] Each carried payment keeps its ORIGINAL date, so no month's takings move.
    [Fact]
    public async Task Carried_Payments_Keep_Their_Original_Dates()
    {
        var plan = PlanCollecting(1000m, (400m, January), (200m, February));
        var invoice = BridgeDraft(plan, 1000m);

        await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.Equal(2, invoice.Payments.Count);
        Assert.Equal(400m, invoice.Payments.Single(p => p.PaidOn == January).Amount);
        Assert.Equal(200m, invoice.Payments.Single(p => p.PaidOn == February).Amount);
    }

    // [AC-32] Each carried payment records where it came from — the provenance that lets the caisse de-dup
    // and the detail modal explain the row.
    [Fact]
    public async Task Carried_Payments_Record_Their_Source_Installment_Payment()
    {
        var plan = PlanCollecting(1000m, (600m, January));
        var sourceId = plan.Installments.Single().Payments.Single().Id;
        var invoice = BridgeDraft(plan, 1000m);

        await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.Equal(sourceId, invoice.Payments.Single().SourceInstallmentPaymentId);
    }

    // [AC-31] A voided installment payment is not carried — it was never received.
    [Fact]
    public async Task A_Voided_Installment_Payment_Is_Not_Carried()
    {
        var plan = PlanCollecting(1000m, (600m, January), (100m, February));
        var installment = plan.Installments.Single();
        var voided = installment.Payments.Single(p => p.Amount == 100m);
        plan.VoidInstallmentPayment(installment.Id, voided.Id, "Erreur de saisie");
        var invoice = BridgeDraft(plan, 1000m);

        await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.Single(invoice.Payments);
        Assert.Equal(600m, invoice.AmountCollected);
    }

    // [AC-33] When the plan collected MORE than the invoice bills, issuing is refused with a message naming
    // the amount — never clamped, and never allowed to throw from inside the payment loop, which would strand
    // a numbered invoice that can then be neither issued nor rebuilt.
    [Fact]
    public async Task Issuing_Is_Refused_When_The_Plan_Collected_More_Than_The_Invoice_Bills()
    {
        var plan = PlanCollecting(1000m, (900m, January));
        // The acts were cut back after the deposit was taken, so the invoice bills only 500 DT (+ timbre).
        var invoice = BridgeDraft(plan, 500m);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("900", result.Error);                              // what the plan collected
        Assert.Contains($"{invoice.TotalTtc:0.000}", result.Error);        // what the invoice actually bills
        // Nothing was recorded: the refusal happens BEFORE the payment loop, so the invoice is not stranded
        // half-carried with a number already burned.
        Assert.Empty(invoice.Payments);
        Assert.Equal(0m, invoice.AmountCollected);
    }

    // [AC-31] A plan with no collected money issues exactly as before — the common case is untouched.
    [Fact]
    public async Task A_Plan_With_No_Payments_Issues_Unchanged()
    {
        var plan = PlanCollecting(1000m);
        var invoice = BridgeDraft(plan, 1000m);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(invoice.Payments);
        Assert.Equal(0m, invoice.AmountCollected);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    // [AC-31] A standalone invoice (no bridge link) never consults the plan repository at all.
    [Fact]
    public async Task A_Standalone_Invoice_Does_Not_Touch_The_Plan_Repository()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 120m) });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.True(result.IsSuccess);
        _plans.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-31] The bridge link is a soft reference with no FK. A missing plan must not block issuing a
    // numbered fiscal document — there is simply nothing to carry.
    [Fact]
    public async Task A_Missing_Plan_Does_Not_Block_Issuing()
    {
        var invoice = new Invoice(
            Guid.NewGuid(), ClinicId, PatientId,
            dentalRecordId: null, appointmentId: null, treatmentPlanId: Guid.NewGuid());
        invoice.SetLines(new[] { ("Couronne", 1, 500m) });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _plans.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TreatmentPlan?)null);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(invoice.Payments);
    }

    // [AC-76] Tenant isolation: a plan belonging to another clinic is not carried from.
    [Fact]
    public async Task A_Foreign_Clinics_Plan_Is_Not_Carried_From()
    {
        var foreignPlan = new TreatmentPlan(
            Guid.NewGuid(), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), PatientId, "Plan");
        foreignPlan.SetItems(new[]
        {
            ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }),
        });
        foreignPlan.Accept("2026-0009");
        foreignPlan.RecordInstallmentPayment(
            foreignPlan.Installments.Single().Id, 600m, PaymentMethod.Cash, January);
        _plans.Setup(r => r.GetByIdAsync(foreignPlan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignPlan);

        var invoice = new Invoice(
            Guid.NewGuid(), ClinicId, PatientId,
            dentalRecordId: null, appointmentId: null, treatmentPlanId: foreignPlan.Id);
        invoice.SetLines(new[] { ("Couronne", 1, 1000m) });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(invoice.Payments);
    }
}
