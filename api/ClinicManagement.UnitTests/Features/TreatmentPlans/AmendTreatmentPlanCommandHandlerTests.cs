using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// [AC-20][AC-21][AC-22][AC-22a][AC-22b][AC-22c] Amending an accepted devis. Before this the plan froze at
/// acceptance and the only way to change treatment was Cancel + retype, losing the number, the échéancier and
/// every réalisé act.
/// <para>
/// Every rejection path asserts that **nothing was committed** — an amendment that half-applies would leave
/// <c>Σ installment.Amount ≠ TotalPlanned</c>, and « Solde patient » (<c>TotalPlanned − Σ AmountPaid</c>) would
/// permanently disagree with « Créances » (<c>Σ (Amount − AmountPaid)</c>) for that patient.
/// </para>
/// </summary>
public class AmendTreatmentPlanCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Due = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalActCodeRepository> _dentalActs = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public AmendTreatmentPlanCommandHandlerTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        NoBridgeInvoice();
        NoAppointments();
    }

    private AmendTreatmentPlanCommandHandler CreateHandler() => new(
        _plans.Object, _patients.Object, _invoices.Object, _appointments.Object, _dentalActs.Object,
        _clinicResolver.Object, _uow.Object, NullLogger<AmendTreatmentPlanCommandHandler>.Instance);

    private void NoBridgeInvoice() =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

    private void BridgedTo(Guid planId, InvoiceStatus status) =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (planId, Guid.NewGuid(), "2026-0031", status)
            });

    private void NoAppointments() =>
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());

    private void BookedFor(Guid itemId, DateTime at) =>
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Appointment(Guid.NewGuid(), ClinicId, PatientId, null, at, TimeSpan.FromMinutes(30),
                    treatmentPlanItemId: itemId)
            });

    /// <summary>An accepted 1 000 DT devis (two acts) with a single lump-sum échéance.</summary>
    private TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[]
        {
            ("Couronne", 600m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }),
            ("Détartrage", 400m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 12 }),
        });
        plan.Accept("2026-0014");
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plan;
    }

    private static List<InstallmentRequest> Schedule(params decimal[] amounts) =>
        amounts.Select(a => new InstallmentRequest { DueDate = Due, Amount = a }).ToList();

    private void NothingCommitted() =>
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

    // [AC-20] The happy path: adding an act raises the total, bumps the revision, and preserves everything
    // that made Cancel + retype so costly — the devis number, the paid installments and the réalisé acts.
    [Fact]
    public async Task Adding_An_Act_Preserves_Number_Payments_And_Done_Acts()
    {
        var plan = AcceptedPlan();
        plan.MarkItemDone(plan.Items.First().Id, Due, Guid.NewGuid());
        var paidId = plan.Installments.First().Id;
        plan.RecordInstallmentPayment(paidId, 250m, PaymentMethod.Cash, Due);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest>
            {
                new() { DesignationFr = "Implant", PlannedCost = 500m },
            },
            // The paid row is echoed back by id — dropping it would erase its collected 250 DT, which the
            // domain refuses (see Dropping_A_Paid_Installment_Is_Rejected).
            Installments = new List<InstallmentRequest>
            {
                new() { Id = paidId, DueDate = Due, Amount = 250m },
                new() { DueDate = Due.AddMonths(1), Amount = 1250m },
            },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("2026-0014", plan.Number);
        Assert.Equal(1500m, plan.TotalPlanned);
        Assert.Equal(250m, plan.AmountPaid);
        Assert.Equal(1, plan.Items.Count(i => i.Status == TreatmentPlanItemStatus.Done));
        Assert.Equal(1, plan.RevisionNumber);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-20] A new act appends at max(SequenceNumber) + 1, so an amendment never reshuffles the order the
    // dentist already set.
    [Fact]
    public async Task An_Added_Act_Appends_At_The_End_Of_The_Order()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1500m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Implant", plan.Items.Last().DesignationFr);
        Assert.Equal(2, plan.Items.Last().SequenceNumber);
    }

    // [AC-21] Removing an open, unbooked act lowers the total.
    [Fact]
    public async Task Removing_An_Open_Unbooked_Act_Lowers_The_Total()
    {
        var plan = AcceptedPlan();
        var removed = plan.Items.Last().Id;

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            RemoveItemIds = new List<Guid> { removed },
            Installments = Schedule(600m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(600m, plan.TotalPlanned);
        Assert.Single(plan.Items);
        Assert.Equal(1, plan.RevisionNumber);
    }

    // [AC-21] A réalisé act cannot be removed — it happened, and the devis must keep saying so.
    [Fact]
    public async Task Removing_A_Done_Act_Is_Rejected()
    {
        var plan = AcceptedPlan();
        var doneId = plan.Items.First().Id;
        plan.MarkItemDone(doneId, Due, Guid.NewGuid());

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            RemoveItemIds = new List<Guid> { doneId },
            Installments = Schedule(400m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingCommitted();
    }

    // [AC-21] An act the patient is still booked for cannot be removed, and the message names the date and
    // the remedy — the patient would otherwise be expected (reminders already sent) for work that no longer
    // exists, with no FK to catch the orphaned appointment.
    [Fact]
    public async Task Removing_An_Act_With_A_Live_Appointment_Is_Rejected_With_Its_Date()
    {
        var plan = AcceptedPlan();
        var bookedId = plan.Items.First().Id;
        BookedFor(bookedId, DateTime.UtcNow.AddDays(9));

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            RemoveItemIds = new List<Guid> { bookedId },
            Installments = Schedule(400m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("rendez-vous", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Annulez ou déplacez", result.Error!);
        NothingCommitted();
    }

    // [AC-2][AC-21] Cancelling the appointment is what unblocks the removal: the projection excludes
    // cancelled bookings, so the act is no longer "booked".
    [Fact]
    public async Task Removing_An_Act_Whose_Appointment_Was_Cancelled_Is_Allowed()
    {
        var plan = AcceptedPlan();
        var itemId = plan.Items.First().Id;
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, DateTime.UtcNow.AddDays(9), TimeSpan.FromMinutes(30),
            treatmentPlanItemId: itemId);
        appointment.Cancel("Reporté");
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { appointment });

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            RemoveItemIds = new List<Guid> { itemId },
            Installments = Schedule(400m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(plan.Items);
    }

    // [AC-22] A schedule that doesn't sum to the new total is rejected — this is the invariant that keeps
    // « Solde patient » and « Créances » agreeing.
    [Fact]
    public async Task A_Schedule_That_Does_Not_Match_The_New_Total_Is_Rejected()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1000m), // total is now 1500
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingCommitted();
    }

    // [AC-22] Changing the total without sending a schedule at all is rejected rather than silently leaving
    // the old échéancier out of sync.
    [Fact]
    public async Task Changing_The_Total_Without_A_Schedule_Is_Rejected()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("échéancier", result.Error!);
        NothingCommitted();
    }

    // [AC-22] An installment revised below what it has already collected is rejected — collected cash cannot
    // be un-received, and a negative balance would flow straight into « Créances ».
    [Fact]
    public async Task An_Installment_Below_Its_Collected_Amount_Is_Rejected()
    {
        var plan = AcceptedPlan();
        var installmentId = plan.Installments.First().Id;
        plan.RecordInstallmentPayment(installmentId, 800m, PaymentMethod.Cash, Due);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Installments = new List<InstallmentRequest>
            {
                new() { Id = installmentId, DueDate = Due, Amount = 500m },
                new() { DueDate = Due, Amount = 500m },
            },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingCommitted();
    }

    // [AC-22] An amendment whose resulting total falls below what the patient has already paid is rejected.
    [Fact]
    public async Task A_Total_Below_The_Amount_Already_Paid_Is_Rejected()
    {
        var plan = AcceptedPlan();
        plan.RecordInstallmentPayment(plan.Installments.First().Id, 800m, PaymentMethod.Cash, Due);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            RemoveItemIds = new List<Guid> { plan.Items.First().Id }, // 1000 → 400, under the 800 collected
            Installments = Schedule(400m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingCommitted();
    }

    // [AC-22] A paid installment cannot be dropped from the schedule — that would erase collected cash from
    // the plan's balance with no trace.
    [Fact]
    public async Task Dropping_A_Paid_Installment_Is_Rejected()
    {
        var plan = AcceptedPlan();
        plan.RecordInstallmentPayment(plan.Installments.First().Id, 300m, PaymentMethod.Cash, Due);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Installments = Schedule(500m, 500m), // both are new rows; the paid one is gone
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingCommitted();
    }

    // [AC-22a] THE correctness guard: a plan already represented by an issued invoice refuses every
    // amendment. The money reads count that invoice, and its lines froze at issue with no re-sync — so an
    // added act would be silently invisible in every balance.
    [Fact]
    public async Task Amending_A_Billed_Plan_Is_Rejected()
    {
        var plan = AcceptedPlan();
        BridgedTo(plan.Id, InvoiceStatus.Issued);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1500m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("déjà facturé", result.Error!);
        Assert.Equal(1000m, plan.TotalPlanned);
        Assert.Equal(0, plan.RevisionNumber);
        NothingCommitted();
    }

    // [AC-22a] …and cancelling that invoice releases the block, which is what makes the guard escapable
    // rather than a dead end.
    [Fact]
    public async Task Cancelling_The_Invoice_Makes_The_Plan_Amendable_Again()
    {
        var plan = AcceptedPlan();
        BridgedTo(plan.Id, InvoiceStatus.Cancelled);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1500m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1500m, plan.TotalPlanned);
    }

    // [AC-22b] After a successful amendment the two formulas the money reads use still agree.
    [Fact]
    public async Task After_An_Amendment_The_Schedule_Still_Sums_To_The_Total()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(700m, 800m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
        Assert.Equal(plan.Outstanding, plan.Installments.Sum(i => i.Amount - i.AmountPaid));
    }

    // [AC-22c] The revision counts amendments, one per successful call — and the devis number never changes.
    [Fact]
    public async Task RevisionNumber_Increments_Once_Per_Amendment()
    {
        var plan = AcceptedPlan();
        var handler = CreateHandler();

        await handler.Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1500m),
        }, CancellationToken.None);
        await handler.Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Bridge", PlannedCost = 500m } },
            Installments = Schedule(2000m),
        }, CancellationToken.None);

        Assert.Equal(2, plan.RevisionNumber);
        Assert.Equal("2026-0014", plan.Number);
    }

    // A Draft is edited outright, not amended — the amend path must not become a second editing route that
    // skips SetItems' guards.
    [Fact]
    public async Task Amending_A_Draft_Is_Rejected()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { ("Couronne", 600m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1100m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingCommitted();
    }

    // An empty request is a client bug, not a no-op that bumps the revision.
    [Fact]
    public async Task An_Empty_Amendment_Is_Rejected()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(
            new AmendTreatmentPlanCommand { Id = plan.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, plan.RevisionNumber);
        NothingCommitted();
    }
}
