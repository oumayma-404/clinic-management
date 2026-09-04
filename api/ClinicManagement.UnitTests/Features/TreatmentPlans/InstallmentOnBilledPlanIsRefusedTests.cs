using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// [J1][J2] An échéance collected on a devis that a note d'honoraires already represents is <b>refused</b>.
///
/// <para>
/// This is the money Blocker of spec J, and the shape of it matters: <c>RecordInstallmentPaymentCommand</c>
/// contained <b>zero</b> references to invoices, while <c>CarryOverPlanPaymentsAsync</c> runs exactly once (at
/// issue) and both installment money reads carry <c>&amp;&amp; !excluded.Contains(p.Id)</c> unconditionally. So
/// cash collected on the plan <i>after</i> the bridge reduced the patient's balance and reached <b>no</b> money
/// read — not la caisse, not the dashboard, not « Encaissé » on /factures. It was entered, receipted, and
/// invisible.
/// </para>
/// <para>
/// Refusing at the write is the only correct side: teaching the reads to include a billed plan would
/// double-count the payments the bridge already carried across. So these tests are as much about the cases that
/// must STILL WORK as about the refusal — a guard that stranded a legitimately unbilled plan would be a worse
/// bug than the one it fixes.
/// </para>
/// <para>
/// The authority is <c>PlanBillingRules.RepresentsItsPlan</c>, the same rule the reads use, read through the
/// same <c>GetTreatmentPlanLinksAsync</c> projection — so the guard and the exclusion cannot disagree about
/// which invoices count. <c>PlanBillingRulesTests</c> owns the rule itself; this class owns the wiring.
/// </para>
/// </summary>
public class InstallmentOnBilledPlanIsRefusedTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>
    /// Fixed and in the past. The handler validates the payment date (J2) *before* it looks at invoices, so a
    /// date derived from « today » would eventually drift into the future and make every test here pass on
    /// « date dans le futur » instead of on the thing it is named for.
    /// </summary>
    private static readonly DateTime PaidOn = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public InstallmentOnBilledPlanIsRefusedTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        // Default: this clinic has no devis→facture bridge at all.
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>());
    }

    /// <summary>An accepted 1 000 DT devis with a single unpaid lump-sum échéance.</summary>
    private TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }) });
        plan.Accept("2026-0014");
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plan;
    }

    /// <summary>Point <c>GetTreatmentPlanLinksAsync</c> at one bridge invoice in the given fiscal state.</summary>
    private void BridgeExists(TreatmentPlan plan, InvoiceStatus status, string? number = "2026-0031")
    {
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (plan.Id, Guid.NewGuid(), number, status, 0m, 0m) });
    }

    private RecordInstallmentPaymentCommandHandler Handler() => new(
        _plans.Object, _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<RecordInstallmentPaymentCommandHandler>.Instance);

    private Task<Result<Application.DTOs.TreatmentPlanDto>> Collect(TreatmentPlan plan, decimal amount = 400m) =>
        Handler().Handle(
            new RecordInstallmentPaymentCommand
            {
                PlanId = plan.Id,
                InstallmentId = plan.Installments.First().Id,
                Amount = amount,
                Method = nameof(PaymentMethod.Cash),
                PaidOn = PaidOn,
            },
            CancellationToken.None);

    /// <summary>Nothing reached the database: no stage, no commit.</summary>
    private void AssertNothingWasWritten()
    {
        _plans.Verify(r => r.UpdateAsync(It.IsAny<TreatmentPlan>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------ the refusal

    // [J1] An issued bridge invoice represents the plan, so the échéance is refused — and the money never moves.
    [Fact]
    public async Task An_Issued_Bridge_Invoice_Refuses_The_Payment()
    {
        var plan = AcceptedPlan();
        BridgeExists(plan, InvoiceStatus.Issued);

        var result = await Collect(plan);

        Assert.True(result.IsFailure);
        AssertNothingWasWritten();
        // The échéance is untouched — the balance must not move on a refused write.
        Assert.Equal(0m, plan.Installments.First().AmountPaid);
    }

    // [J1] The message must NAME the note and say where to enter the money instead. « Opération impossible »
    // would leave the dentist holding cash with nowhere to put it, which is how a workaround gets invented.
    [Fact]
    public async Task The_Refusal_Names_The_Invoice_And_The_Action()
    {
        var plan = AcceptedPlan();
        BridgeExists(plan, InvoiceStatus.Issued, "2026-0042");

        var result = await Collect(plan);

        Assert.True(result.IsFailure);
        Assert.Contains("2026-0042", result.Error);
        Assert.Contains("note d'honoraires", result.Error);
    }

    // [J1] Every non-Draft, non-Cancelled state represents the plan — a partially-paid or fully-paid bridge is
    // just as much "the invoice speaks for this devis" as a freshly issued one.
    [Theory]
    [InlineData(InvoiceStatus.Issued)]
    [InlineData(InvoiceStatus.PartiallyPaid)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task Every_Representing_Status_Refuses(InvoiceStatus status)
    {
        var plan = AcceptedPlan();
        BridgeExists(plan, status);

        var result = await Collect(plan);

        Assert.True(result.IsFailure);
        AssertNothingWasWritten();
    }

    // ------------------------------------------------------------------ what must STILL work

    // [J1][edge] The spec's critical edge case: collecting on a plan whose bridge invoice was later CANCELLED
    // must still work. A cancelled note is void, the plan is handed back to both money reads, and refusing here
    // would strand a patient's échéancier permanently with no way to take their money.
    [Fact]
    public async Task A_Cancelled_Bridge_Invoice_Still_Allows_Collection()
    {
        var plan = AcceptedPlan();
        BridgeExists(plan, InvoiceStatus.Cancelled);

        var result = await Collect(plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(400m, plan.Installments.First().AmountPaid);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [J1][edge] A DRAFT bridge does not represent the plan yet (`PlanBillingRules` already encodes this): the
    // plan still carries its own debt, so it must still be collectable. Refusing on a draft would block the
    // window between "invoice created" and "invoice issued" — during which the plan is the only truth.
    [Fact]
    public async Task A_Draft_Bridge_Invoice_Still_Allows_Collection()
    {
        var plan = AcceptedPlan();
        BridgeExists(plan, InvoiceStatus.Draft, number: null);

        var result = await Collect(plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(400m, plan.Installments.First().AmountPaid);
    }

    // [J1] An ordinary unbilled devis — the overwhelmingly common case — is untouched by the guard.
    [Fact]
    public async Task An_Unbilled_Plan_Collects_Normally()
    {
        var plan = AcceptedPlan();

        var result = await Collect(plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(400m, plan.Installments.First().AmountPaid);
    }

    // [J1] A bridge invoice belonging to ANOTHER plan must not block this one. The projection is clinic-wide, so
    // the handler has to match on the plan id — filtering only on status would refuse every échéance in a clinic
    // that has ever issued one devis→facture bridge.
    [Fact]
    public async Task A_Bridge_For_A_Different_Plan_Does_Not_Block_This_One()
    {
        var plan = AcceptedPlan();
        var otherPlanId = Guid.NewGuid();
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (otherPlanId, Guid.NewGuid(), (string?)"2026-0099", InvoiceStatus.Issued, 0m, 0m) });

        var result = await Collect(plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(400m, plan.Installments.First().AmountPaid);
    }

    // ------------------------------------------------------------------ J2, on this ledger

    // [J2] `PaymentDateRules`' own docstring named "an installment payment's" as a caller and this path was not
    // one of the three. A future date drops the balance now and appears in no caisse until the date arrives.
    [Fact]
    public async Task A_Future_Payment_Date_Is_Refused()
    {
        var plan = AcceptedPlan();

        var result = await Handler().Handle(
            new RecordInstallmentPaymentCommand
            {
                PlanId = plan.Id,
                InstallmentId = plan.Installments.First().Id,
                Amount = 400m,
                Method = nameof(PaymentMethod.Cash),
                PaidOn = DateTime.UtcNow.AddMonths(1),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("futur", result.Error);
        AssertNothingWasWritten();
    }

    // [J2] The mirror image, and the worse one: an omitted key posts `0001-01-01`, which moves the collected
    // total while being invisible in every cash window FOREVER.
    [Fact]
    public async Task An_Absent_Payment_Date_Is_Refused()
    {
        var plan = AcceptedPlan();

        var result = await Handler().Handle(
            new RecordInstallmentPaymentCommand
            {
                PlanId = plan.Id,
                InstallmentId = plan.Installments.First().Id,
                Amount = 400m,
                Method = nameof(PaymentMethod.Cash),
                PaidOn = default,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasWritten();
    }

    // [J1] Tenant isolation still runs, and the date/billed guards must not have jumped ahead of it: another
    // clinic's plan reads as "introuvable", never as "facturé" (which would confirm the plan exists).
    [Fact]
    public async Task A_Foreign_Plan_Is_Still_NotFound()
    {
        var foreign = new TreatmentPlan(Guid.NewGuid(), OtherClinicId, PatientId, "Plan d'un autre cabinet");
        foreign.SetItems(new[] { ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }) });
        foreign.Accept("2026-0001");
        _plans.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Collect(foreign);

        Assert.True(result.IsFailure);
        Assert.Contains("introuvable", result.Error);
        AssertNothingWasWritten();
    }
}
