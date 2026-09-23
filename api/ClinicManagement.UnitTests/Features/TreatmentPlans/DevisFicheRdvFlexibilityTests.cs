using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// <b>A visit, a fiche and a devis must never lock each other.</b> Each test is one way the three used to: an
/// edit refused because of what the record already held, a re-save that did new work, or a link left pointing
/// at something that no longer exists. See <c>features/devis-fiche-rdv-flexibility/notes.md</c>.
/// </summary>
public class DevisFicheRdvFlexibilityTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FicheOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Day1 = new(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc);

    private static TreatmentPlan OneActDevis(Guid? procedureTypeId = null)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Canal");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Traitement de canal", 150m, procedureTypeId ?? Guid.NewGuid(),
                Array.Empty<int>()),
        });
        plan.Accept("2026-0100");
        return plan;
    }

    private static TreatmentPlan ThreeSeanceDevis()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", 60),
            new TreatmentPlanItemStepInput(null, "Empreinte", 30),
            new TreatmentPlanItemStepInput(null, "Scellement", 30),
        });
        return plan;
    }

    private static Appointment VisitFor(TreatmentPlanItem item, Guid? stepId = null, Guid? procedureTypeId = null)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, Day1.AddDays(7), TimeSpan.FromMinutes(30));
        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(procedureTypeId ?? item.ProcedureTypeId, "Acte", 30, null, null,
                item.Id, stepId),
        });
        return appointment;
    }

    private static Mock<ITreatmentPlanRepository> Plans(TreatmentPlan plan)
    {
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        foreach (var item in plan.Items)
        {
            plans.Setup(r => r.GetByItemIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        }
        return plans;
    }

    private static Mock<IAppointmentRepository> Appointments(params Appointment[] appointments)
    {
        var repository = new Mock<IAppointmentRepository>();
        foreach (var a in appointments)
        {
            repository.Setup(r => r.GetByIdAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        }
        repository
            .Setup(r => r.GetByTreatmentPlanItemIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                appointments.Where(a => a.LinkedTreatmentPlanItemIds.Any(ids.Contains)).ToList());
        return repository;
    }

    // ── the fiche: a re-save is not new work ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Fiche_On_A_Devis_It_Completed_Can_Be_Saved_Again()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        var plans = Plans(plan).Object;
        var none = Appointments().Object;

        var first = await DentalRecordLinker.LinkPlanItemAsync(
            plans, none, plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        // Fixing a typo on that fiche used to be refused « Ce devis est clôturé ».
        var again = await DentalRecordLinker.LinkPlanItemAsync(
            plans, none, plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    [Fact]
    public async Task Re_Saving_A_Walk_In_Fiche_Does_Not_Mark_Another_Seance()
    {
        var plan = ThreeSeanceDevis();
        var item = plan.Items.Single();
        var plans = Plans(plan).Object;
        var none = Appointments().Object;

        await DentalRecordLinker.LinkPlanItemAsync(
            plans, none, plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None);
        await DentalRecordLinker.LinkPlanItemAsync(
            plans, none, plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None);
        await DentalRecordLinker.LinkPlanItemAsync(
            plans, none, plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None);

        Assert.Equal(1, item.Steps.Count(s => s.IsDone));
        Assert.NotEqual(TreatmentPlanItemStatus.Done, item.Status);
    }

    [Fact]
    public async Task A_Visit_Booked_On_A_Removed_Seance_Can_Still_Be_Charted()
    {
        var plan = ThreeSeanceDevis();
        var item = plan.Items.Single();
        var visit = VisitFor(item, stepId: Guid.NewGuid()); // a séance id the act no longer has

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            Plans(plan).Object, Appointments(visit).Object, plan.Id, item.Id, PatientId, ClinicId, FicheOne,
            Day1, CancellationToken.None, appointmentId: visit.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, item.Steps.Count(s => s.IsDone));
    }

    // ── the fiche lets go ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Releasing_A_Fiche_On_A_Cancelled_Devis_Clears_The_Link_And_Keeps_The_Status()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, Day1, FicheOne);
        plan.Cancel("patient parti");

        var released = plan.ReleaseDentalRecord(FicheOne);

        Assert.Equal(1, released);
        Assert.Null(item.LinkedDentalRecordId);
        Assert.Equal(TreatmentPlanStatus.Cancelled, plan.Status);
    }

    [Fact]
    public void Releasing_The_Fiche_That_Completed_A_Devis_Reopens_It()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, Day1, FicheOne);

        plan.ReleaseDentalRecordFor(item.Id, FicheOne);

        Assert.NotEqual(TreatmentPlanItemStatus.Done, item.Status);
        Assert.NotEqual(TreatmentPlanStatus.Completed, plan.Status);
    }

    // ── the visit: what it already holds is never re-litigated ──────────────────────────────────────────

    [Fact]
    public async Task A_Link_The_Visit_Already_Holds_Is_Accepted_On_A_Closed_Devis()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, Day1, FicheOne);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        var result = await AppointmentPlanLink.ValidateManyAsync(
            Plans(plan).Object, plan.Id, new List<(Guid, Guid?)> { (item.Id, null) }, ClinicId, PatientId,
            CancellationToken.None, alreadyHeldItemIds: new[] { item.Id });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_New_Link_To_A_Cancelled_Devis_Is_Refused_By_Name()
    {
        var plan = OneActDevis();
        plan.Cancel("patient parti");
        var item = plan.Items.Single();

        var result = await AppointmentPlanLink.ValidateManyAsync(
            Plans(plan).Object, plan.Id, new List<(Guid, Guid?)> { (item.Id, null) }, ClinicId, PatientId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentPlanLink.ClosedPlanRefusal, result.Error);
    }

    [Fact]
    public async Task A_Held_Link_Sent_Without_Its_Plan_Id_Finds_Its_Plan_And_Drops_A_Dead_Seance()
    {
        var plan = ThreeSeanceDevis();
        var item = plan.Items.Single();
        var requests = new List<AppointmentProcedureRequest>
        {
            new() { ProcedureTypeId = item.ProcedureTypeId, TreatmentPlanItemId = item.Id,
                    TreatmentPlanItemStepId = Guid.NewGuid() },
        };

        var (repaired, planId) = await AppointmentPlanLink.RepairHeldLinksAsync(
            Plans(plan).Object, requests, treatmentPlanId: null, heldItemIds: new[] { item.Id }, ClinicId,
            PatientId, CancellationToken.None);

        Assert.Equal(plan.Id, planId);
        Assert.Equal(item.Id, repaired.Single().TreatmentPlanItemId);
        Assert.Null(repaired.Single().TreatmentPlanItemStepId);
    }

    [Fact]
    public async Task A_Held_Link_To_A_Deleted_Devis_Line_Keeps_The_Act_And_Drops_The_Link()
    {
        var procedureTypeId = Guid.NewGuid();
        var requests = new List<AppointmentProcedureRequest>
        {
            new() { ProcedureTypeId = procedureTypeId, TreatmentPlanItemId = Guid.NewGuid(), AgreedCost = 40m },
        };

        var (repaired, planId) = await AppointmentPlanLink.RepairHeldLinksAsync(
            new Mock<ITreatmentPlanRepository>().Object, requests, null,
            heldItemIds: new[] { requests[0].TreatmentPlanItemId!.Value }, ClinicId, PatientId,
            CancellationToken.None);

        Assert.Null(planId);
        var row = Assert.Single(repaired);
        Assert.Equal(procedureTypeId, row.ProcedureTypeId);
        Assert.Null(row.TreatmentPlanItemId);
        Assert.Equal(40m, row.AgreedCost);
    }

    [Fact]
    public async Task An_Archived_Act_Is_Kept_On_The_Visit_That_Holds_It_And_Refused_As_A_New_One()
    {
        var archived = new ProcedureType(Guid.NewGuid(), ClinicId, "Inlay", 45, ColorHex.FromString("#2A9D8F"));
        archived.Deactivate();
        var catalogue = new Mock<IProcedureTypeRepository>();
        catalogue.Setup(r => r.GetByIdAsync(archived.Id, It.IsAny<CancellationToken>())).ReturnsAsync(archived);

        var visit = new Appointment(Guid.NewGuid(), ClinicId, PatientId, null, Day1, TimeSpan.FromMinutes(45));
        visit.SetProcedures(new[] { new AppointmentProcedureInput(archived.Id, "Inlay", 45, "#2A9D8F", null, null) });
        var request = new List<AppointmentProcedureRequest> { new() { ProcedureTypeId = archived.Id } };

        var held = await AppointmentProcedureSelection.ResolveAsync(
            catalogue.Object, ClinicId, request, new Dictionary<Guid, string>(), CancellationToken.None,
            visit.Procedures);
        var fresh = await AppointmentProcedureSelection.ResolveAsync(
            catalogue.Object, ClinicId, request, new Dictionary<Guid, string>(), CancellationToken.None);

        Assert.True(held.IsSuccess);
        Assert.Equal(archived.Id, held.Value!.Single().ProcedureTypeId);
        Assert.True(fresh.IsFailure);
    }

    // ── the devis lets go of its bookings ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Visit_Still_Ahead_Is_Cancelled_And_Unlinked_When_Its_Only_Act_Is_Released()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        var visit = VisitFor(item);

        var toCancel = await PlanBookingRelease.ReleaseAsync(
            new[] { item.Id }, ClinicId, Appointments(visit).Object, CancellationToken.None);

        Assert.Equal(new[] { visit.Id }, toCancel);
        Assert.Empty(visit.LinkedTreatmentPlanItemIds);
    }

    [Fact]
    public async Task A_Visit_Whose_Slot_Has_Passed_Is_Unlinked_But_Never_Cancelled()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        var visit = VisitFor(item);
        visit.MarkAwaitingClosure();

        var toCancel = await PlanBookingRelease.ReleaseAsync(
            new[] { item.Id }, ClinicId, Appointments(visit).Object, CancellationToken.None);

        Assert.Empty(toCancel);
        Assert.Empty(visit.LinkedTreatmentPlanItemIds);
        Assert.Equal(AppointmentStatus.AwaitingClosure, visit.Status);
    }

    [Fact]
    public async Task Removing_A_Booked_Seance_Keeps_The_Visit_On_The_Act()
    {
        var plan = ThreeSeanceDevis();
        var item = plan.Items.Single();
        var removed = item.Steps[1].Id;
        var visit = VisitFor(item, stepId: removed);

        await PlanBookingRelease.ReleaseStepsAsync(
            item.Id, new[] { removed }, ClinicId, Appointments(visit).Object, CancellationToken.None);

        var row = Assert.Single(visit.Procedures);
        Assert.Equal(item.Id, row.TreatmentPlanItemId);
        Assert.Null(row.TreatmentPlanItemStepId);
    }

    [Fact]
    public async Task A_Visit_Follows_Its_Devis_Line_When_The_Act_Is_Replaced()
    {
        var plan = OneActDevis();
        var item = plan.Items.Single();
        var visit = VisitFor(item);
        var extraction = new ProcedureType(Guid.NewGuid(), ClinicId, "Extraction", 20, ColorHex.FromString("#E76F51"));

        await PlanBookingRelease.FollowActChangeAsync(
            item.Id, extraction, ClinicId, Appointments(visit).Object, CancellationToken.None);

        var row = Assert.Single(visit.Procedures);
        Assert.Equal(extraction.Id, row.ProcedureTypeId);
        Assert.Equal("Extraction", row.ProcedureName);
        Assert.Equal(item.Id, row.TreatmentPlanItemId);
    }
}
