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
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
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
        _plans.Object, _patients.Object, _invoices.Object, _appointments.Object, _procedureTypes.Object,
        _clinicResolver.Object, _uow.Object, NullLogger<AmendTreatmentPlanCommandHandler>.Instance);

    private void NoBridgeInvoice() =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>());

    private void BridgedTo(Guid planId, InvoiceStatus status) =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>
            {
                (planId, Guid.NewGuid(), "2026-0031", status, 0m, 0m)
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
            ("Couronne", 600m, (IReadOnlyList<int>)new[] { 11 }),
            ("Détartrage", 400m, (IReadOnlyList<int>)new[] { 12 }),
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

    // [AC-22] Changing the total without sending a schedule RE-SPREADS the échéancier rather than refusing.
    //
    // ⚠️ This test asserted the refusal until the multi-séance redesign, and the refusal was the app fighting
    // the dentist: a price corrected from the booking dialog or the acts table has no échéancier on screen to
    // re-send, so « renvoyez l'échéancier » named something the caller could not see. The invariant it
    // protected — Σ installment.Amount == TotalPlanned, which is what keeps « Solde patient » and
    // « Créances » agreeing — is now held by the re-spread instead of by the refusal.
    [Fact]
    public async Task Changing_The_Total_Without_A_Schedule_Respreads_The_Echeancier()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1500m, plan.TotalPlanned);
        Assert.Equal(1500m, plan.Installments.Sum(i => i.Amount));
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
    /*
     * ⚠️ This asserted the OPPOSITE until the owner's decision that a dentist must be able to correct anything.
     *
     * The old refusal — « annulez la facture (ou émettez un avoir) avant de modifier le plan » — was sound about
     * the consequence and wrong about the remedy: it asked a dentist to reverse a numbered fiscal document in
     * order to fix a plan. The divergence between a corrected devis and the note raised from it is now STATED
     * (the amend dialog names the note and points at an avoir) instead of pre-empted.
     *
     * It is also what unblocked every plan the continuation feature creates: those are born attached to a note,
     * so under the old rule a treatment still under way could never be adjusted.
     *
     * ⚠️ The money reads are untouched by this — `GetPatientBillingSummaryQuery` drops a plan billed into an
     * invoice — so a changed `TotalPlanned` here moves no balance, no caisse figure and no receivable. That is
     * precisely why the gap is documentary and stating it is the whole fix.
     */
    public async Task Amending_A_Billed_Plan_Is_Allowed()
    {
        var plan = AcceptedPlan();
        BridgedTo(plan.Id, InvoiceStatus.Issued);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1500m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1500m, plan.TotalPlanned);
        Assert.Equal(1, plan.RevisionNumber);
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

    /// <summary>
    /// An un-numbered treatment is amendable, and it keeps its null <c>Number</c>.
    ///
    /// <para>⚠️ This asserted a <b>refusal</b> — « A Draft is edited outright, not amended » — and that was
    /// true while a Draft meant a form nobody had finished. It does not describe what a Draft is now: « Suivre
    /// ce traitement » creates one from the booking dialog, carrying séances and recorded work, and it opens
    /// the same workspace a numbered devis does. Refusing amendment there left that screen with no way to
    /// correct a total, which is the one thing the practice asked to be possible at any moment.</para>
    ///
    /// <para>The guard that matters is still asserted: amending must not <b>number</b> the treatment. Taking a
    /// devis number is <c>IssueDevisCommand</c>'s alone, because a number is gapless and releasable only by a
    /// cancellation carrying a motif.</para>
    /// </summary>
    [Fact]
    public async Task Amending_A_Draft_Is_Allowed_And_Does_Not_Number_It()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { ("Couronne", 600m, (IReadOnlyList<int>)new[] { 11 }) });
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
            Installments = Schedule(1100m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1100m, plan.TotalPlanned);
        // Still un-numbered, and still a Draft: amending is not a promotion.
        Assert.Null(plan.Number);
        Assert.Equal(TreatmentPlanStatus.Draft, plan.Status);
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

    // ---- In-place act edits ------------------------------------------------------------------------------
    //
    // The gap these close: the endpoint took additions and removals only, so "this act's price is wrong" had to
    // be expressed as remove-then-add — which re-issues the act's id (orphaning the appointment and fiche links,
    // neither of which has an FK) and is refused outright once the act is Done or booked. Those are exactly the
    // acts whose price is usually noticed to be wrong, so the correction the dentist needs most was unreachable.

    /// <summary>One in-place edit of an existing act, carrying whatever the caller wants changed.</summary>
    private static List<TreatmentPlanItemRequest> Edit(
        Guid itemId, string designation, decimal cost, params int[] teeth) =>
        new()
        {
            new()
            {
                Id = itemId,
                DesignationFr = designation,
                PlannedCost = cost,
                ToothNumbers = teeth.ToList(),
            },
        };

    // Editing an act in place keeps its id — the whole reason this exists rather than remove-then-add — and the
    // new fee lands in the total.
    [Fact]
    public async Task Editing_An_Act_Keeps_Its_Id_And_Moves_The_Total()
    {
        var plan = AcceptedPlan();
        var itemId = plan.Items.First().Id;

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(itemId, "Couronne céramique", 750m, 11, 21),
            Installments = Schedule(1150m), // 750 + 400
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var edited = plan.Items.Single(i => i.Id == itemId);
        Assert.Equal("Couronne céramique", edited.DesignationFr);
        Assert.Equal(750m, edited.PlannedCost);
        Assert.Equal(new[] { 11, 21 }, edited.ToothNumbers);
        Assert.Equal(1150m, plan.TotalPlanned);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1, plan.RevisionNumber);
    }

    // THE case this feature exists for: a wrong price on work already carried out. Removal is refused for a
    // réalisé act (Removing_A_Done_Act_Is_Rejected), so before this there was no way to correct it at all. The
    // act stays Done and keeps its fiche link — a price correction is not a claim that the act un-happened.
    [Fact]
    public async Task Editing_A_Done_Acts_Price_Is_Allowed_And_Leaves_It_Done()
    {
        var plan = AcceptedPlan();
        var doneId = plan.Items.First().Id;
        var recordId = Guid.NewGuid();
        plan.MarkItemDone(doneId, Due, recordId);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(doneId, "Couronne", 500m, 11),
            Installments = Schedule(900m), // 500 + 400
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var edited = plan.Items.Single(i => i.Id == doneId);
        Assert.Equal(500m, edited.PlannedCost);
        Assert.Equal(TreatmentPlanItemStatus.Done, edited.Status);
        Assert.Equal(recordId, edited.LinkedDentalRecordId);
        Assert.Equal(Due, edited.DoneDate);
    }

    // The mirror case: an act the patient is still booked for also refuses removal, so editing in place is the
    // only route. Nothing about the appointment changes — it points at the same act id.
    [Fact]
    public async Task Editing_A_Booked_Acts_Price_Is_Allowed()
    {
        var plan = AcceptedPlan();
        var bookedId = plan.Items.First().Id;
        BookedFor(bookedId, DateTime.UtcNow.AddDays(9));

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(bookedId, "Couronne", 800m, 11),
            Installments = Schedule(1200m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(800m, plan.Items.Single(i => i.Id == bookedId).PlannedCost);
    }

    // An edit naming an act that is not on this plan is refused rather than silently added — a caller asking to
    // revise a specific act and getting a new one instead would double the line and the total.
    [Fact]
    public async Task Editing_An_Act_That_Is_Not_On_The_Plan_Is_Rejected()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(Guid.NewGuid(), "Implant", 500m),
            Installments = Schedule(1000m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1000m, plan.TotalPlanned);
        Assert.Equal(0, plan.RevisionNumber);
        NothingCommitted();
    }

    // A batch is all-or-nothing: the second edit is unknown, so the first must not have been applied either —
    // a half-applied batch leaves a total matching neither the old devis nor the new one.
    [Fact]
    public async Task A_Batch_Of_Edits_Is_All_Or_Nothing()
    {
        var plan = AcceptedPlan();
        var goodId = plan.Items.First().Id;

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = new List<TreatmentPlanItemRequest>
            {
                new() { Id = goodId, DesignationFr = "Couronne céramique", PlannedCost = 750m },
                new() { Id = Guid.NewGuid(), DesignationFr = "Fantôme", PlannedCost = 100m },
            },
            Installments = Schedule(1250m),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Couronne", plan.Items.Single(i => i.Id == goodId).DesignationFr);
        Assert.Equal(600m, plan.Items.Single(i => i.Id == goodId).PlannedCost);
        NothingCommitted();
    }

    // The re-spread applies to an EDIT exactly as it does to an add or a remove — and the edit is the path that
    // matters most, since « corriger le prix d'un acte » is the gesture the whole redesign exists to keep open.
    [Fact]
    public async Task An_Edit_That_Changes_The_Total_Without_A_Schedule_Respreads_The_Echeancier()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(plan.Items.First().Id, "Couronne", 750m, 11),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1150m, plan.TotalPlanned);
        Assert.Equal(1150m, plan.Installments.Sum(i => i.Amount));
    }

    // A rename that leaves the fee alone changes no total, so it needs no schedule.
    [Fact]
    public async Task A_Rename_That_Leaves_The_Total_Alone_Needs_No_Schedule()
    {
        var plan = AcceptedPlan();
        var itemId = plan.Items.First().Id;

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(itemId, "Couronne céramo-métallique", 600m, 11),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Couronne céramo-métallique", plan.Items.Single(i => i.Id == itemId).DesignationFr);
        Assert.Equal(1000m, plan.TotalPlanned);
    }

    // The billed-plan guard covers the new path too — it is the correctness guard for every amendment, not just
    // for additions.
    [Fact]
    // The in-place twin of the row above: a mistyped fee on a billed devis is corrected, not refused.
    public async Task Editing_An_Act_On_A_Billed_Plan_Is_Allowed()
    {
        var plan = AcceptedPlan();
        BridgedTo(plan.Id, InvoiceStatus.Issued);

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            UpdateItems = Edit(plan.Items.First().Id, "Couronne", 750m, 11),
            Installments = Schedule(1150m),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(750m, plan.Items.First().PlannedCost);
        Assert.Equal("Couronne", plan.Items.First().DesignationFr);
    }

    // ---- Title / notes ----------------------------------------------------------------------------------

    // The title is what the patient reads on their devis; a typo used to freeze at acceptance, fixable only by
    // cancelling the devis and losing its number.
    [Fact]
    public async Task Retitling_An_Accepted_Devis_Is_Allowed_And_Bumps_The_Revision()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Title = "Réhabilitation complète",
            Notes = "Accord du patient le 12/03.",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Réhabilitation complète", plan.Title);
        Assert.Equal("Accord du patient le 12/03.", plan.Notes);
        Assert.Equal("2026-0014", plan.Number);
        Assert.Equal(1, plan.RevisionNumber);
    }

    // Notes are tri-state: an explicit null clears them.
    [Fact]
    public async Task Sending_Null_Notes_Clears_Them()
    {
        var plan = AcceptedPlan();
        plan.UpdateDetails(plan.Title, "À revoir");

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Notes = null,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(plan.Notes);
    }

    // The amend dialog always sends both fields, so re-submitting them unchanged must not read as an
    // amendment — « révision N » only means something if it counts edits the patient could hold a printout of.
    [Fact]
    public async Task Resubmitting_The_Same_Title_And_Notes_Is_Not_An_Amendment()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Title = "Réhabilitation",
            Notes = null, // the plan has none
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Aucune modification demandée.", result.Error);
        Assert.Equal(0, plan.RevisionNumber);
        NothingCommitted();
    }

    // A blank title is "leave it alone", not "clear it" — the aggregate requires one.
    [Fact]
    public async Task A_Blank_Title_Leaves_The_Existing_One()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Title = "   ",
            UpdateItems = Edit(plan.Items.First().Id, "Couronne céramique", 600m, 11),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Réhabilitation", plan.Title);
    }

    // The version the user was editing is what the save is validated against — an amendment now rewrites fees
    // in place, so a lost update is a money defect rather than a merge annoyance.
    [Fact]
    public async Task The_Clients_Version_Is_Passed_To_The_Concurrency_Check()
    {
        var plan = AcceptedPlan();

        var result = await CreateHandler().Handle(new AmendTreatmentPlanCommand
        {
            Id = plan.Id,
            Version = 42,
            UpdateItems = Edit(plan.Items.First().Id, "Couronne céramique", 600m, 11),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _uow.Verify(u => u.SetExpectedVersion(plan, 42u), Times.Once);
    }
}
