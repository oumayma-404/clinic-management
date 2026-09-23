using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.Recall;
using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// Wave 4 of <c>devis-fiche-rdv-flexibility</c> — status and date edits (H), the catalogue (I), the sentences (J)
/// and C4b. See <c>features/devis-fiche-rdv-flexibility/notes.md</c>.
/// </summary>
public class DevisFicheRdvFlexibilityWave4Tests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Aug = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

    private static Mock<ICurrentClinicResolver> Clinic()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        return resolver;
    }

    // ── H1 / H10 / J4: moving a visit ────────────────────────────────────────────────────────────────

    /// <summary>A patient-less slot, so the post-commit notification and reminder paths stay quiet.</summary>
    private static Appointment Visit(DateTime when) =>
        new(Guid.NewGuid(), ClinicId, patientId: null, doctorId: null,
            appointmentDateTime: when, duration: TimeSpan.FromMinutes(30));

    private static UpdateAppointmentCommandHandler Handler(Appointment appointment, Mock<IUnitOfWork>? unitOfWork = null)
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        return new UpdateAppointmentCommandHandler(
            repo.Object, new Mock<IProcedureTypeRepository>().Object, new Mock<IDoctorRepository>().Object,
            new Mock<IClinicRepository>().Object,
            new Mock<ITreatmentPlanRepository>().Object, Clinic().Object,
            new Mock<IClinicContext>().Object, (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            new Mock<IAppointmentGoogleSyncDispatcher>().Object,
            new Mock<INotificationGenerator>().Object, new Mock<IReminderScheduler>().Object,
            NullLogger<UpdateAppointmentCommandHandler>.Instance);
    }

    [Fact]
    public async Task H1_A_Finished_Visit_Moves_And_Stays_Finished()
    {
        var visit = Visit(DateTime.UtcNow.AddDays(-3));
        visit.Complete();
        var corrected = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-4).Date.AddHours(9), DateTimeKind.Utc);

        var result = await Handler(visit).Handle(
            new UpdateAppointmentCommand { Id = visit.Id, AppointmentDateTime = corrected, Status = "Completed" },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(corrected, visit.AppointmentDateTime);
        Assert.Equal(AppointmentStatus.Completed, visit.Status);
    }

    [Fact]
    public async Task H1_A_Finished_Visit_Cannot_Be_Moved_Into_The_Future()
    {
        var visit = Visit(DateTime.UtcNow.AddDays(-3));
        visit.Complete();
        var original = visit.AppointmentDateTime;

        var result = await Handler(visit).Handle(
            new UpdateAppointmentCommand { Id = visit.Id, AppointmentDateTime = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("date future", result.Error);
        Assert.Equal(original, visit.AppointmentDateTime);
    }

    [Fact]
    public async Task H1_A_Cancelled_Visit_Moved_Without_Reactivating_Is_Refused_By_Name_Not_Answered_200()
    {
        var visit = Visit(new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc));
        visit.Cancel();

        var result = await Handler(visit).Handle(
            new UpdateAppointmentCommand
            {
                Id = visit.Id,
                AppointmentDateTime = new DateTime(2026, 8, 3, 10, 30, 0, DateTimeKind.Utc),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Appointment.CancelledCannotMoveMessage, result.Error);
    }

    [Fact]
    public async Task H10_En_Cours_Moved_To_Another_Day_Is_Planned_Again_But_Not_On_The_Same_Day()
    {
        var start = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        var otherDay = Visit(start);
        otherDay.Start();
        await Handler(otherDay).Handle(
            new UpdateAppointmentCommand { Id = otherDay.Id, AppointmentDateTime = start.AddDays(2) },
            CancellationToken.None);

        var sameDay = Visit(start);
        sameDay.Start();
        await Handler(sameDay).Handle(
            new UpdateAppointmentCommand { Id = sameDay.Id, AppointmentDateTime = start.AddHours(2) },
            CancellationToken.None);

        Assert.Equal(AppointmentStatus.Scheduled, otherDay.Status);
        Assert.Equal(AppointmentStatus.InProgress, sameDay.Status);
    }

    [Fact]
    public async Task J4_A_Domain_Refusal_Reaches_The_User_And_A_Framework_One_Does_Not()
    {
        var domainVisit = Visit(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
        var domainUow = new Mock<IUnitOfWork>();
        // A throw that genuinely originates in the Domain assembly (Reactivate on a visit that is not cancelled).
        domainUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => domainVisit.Reactivate(DateTime.UtcNow));

        var frameworkVisit = Visit(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
        var frameworkUow = new Mock<IUnitOfWork>();
        frameworkUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The instance of entity type 'Appointment' cannot be tracked"));

        var domain = await Handler(domainVisit, domainUow).Handle(
            new UpdateAppointmentCommand { Id = domainVisit.Id, Notes = "x" }, CancellationToken.None);
        var framework = await Handler(frameworkVisit, frameworkUow).Handle(
            new UpdateAppointmentCommand { Id = frameworkVisit.Id, Notes = "x" }, CancellationToken.None);

        Assert.Equal("Only a cancelled appointment can be reactivated", domain.Error);
        Assert.DoesNotContain("tracked", framework.Error);
        Assert.Contains("Veuillez réessayer", framework.Error);
    }

    // ── H3: the act set changes the status ───────────────────────────────────────────────────────────

    private static TreatmentPlan FinishedDevis()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Détartrage", 60m, null, Array.Empty<int>()) });
        plan.Accept("2026-0400");
        plan.MarkItemDone(plan.Items.Single().Id, Aug, Guid.NewGuid());
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
        return plan;
    }

    [Fact]
    public void H3_An_Act_Added_To_A_Finished_Devis_Reopens_It()
    {
        var plan = FinishedDevis();

        plan.AddItems(new[] { new TreatmentPlanItemInput(null, "Couronne", 300m, null, Array.Empty<int>()) });

        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
    }

    [Fact]
    public void H3_Removing_The_Last_Act_Still_To_Do_Closes_The_Devis()
    {
        var plan = FinishedDevis();
        plan.AddItems(new[] { new TreatmentPlanItemInput(null, "Couronne", 300m, null, Array.Empty<int>()) });
        var added = plan.Items.Single(i => i.DesignationFr == "Couronne");

        plan.RemoveItem(added.Id);

        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    [Fact]
    public void H3_A_Stopped_Treatment_Is_Not_Restarted_By_Bringing_One_Act_Back()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Détartrage", 60m, null, Array.Empty<int>()),
            new TreatmentPlanItemInput(null, "Couronne", 300m, null, Array.Empty<int>()),
        });
        plan.Accept("2026-0401");
        plan.MarkItemDone(plan.Items.First().Id, Aug, Guid.NewGuid());
        var parked = plan.StopTreatment(Aug).Single();

        plan.RestoreItem(parked.Id, Aug);

        Assert.Equal(TreatmentPlanStatus.Stopped, plan.Status);
    }

    // ── H4: a done one-séance act cut into séances ───────────────────────────────────────────────────

    [Fact]
    public void H4_A_Finished_Devis_Whose_Done_Act_Gets_Three_Seances_Is_Open_With_Seance_One_Done()
    {
        var plan = FinishedDevis();
        var item = plan.Items.Single();
        var fiche = item.LinkedDentalRecordId;

        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Séance 1", null),
            new TreatmentPlanItemStepInput(null, "Séance 2", null),
            new TreatmentPlanItemStepInput(null, "Séance 3", null),
        });

        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
        var steps = item.Steps.OrderBy(s => s.SequenceNumber).ToList();
        Assert.Equal(fiche, steps[0].LinkedDentalRecordId);
        Assert.Equal(Aug, steps[0].DoneDate);
        Assert.All(steps.Skip(1), s => Assert.Null(s.DoneDate));
    }

    // ── H9: each séance's own date ──────────────────────────────────────────────────────────────────

    [Fact]
    public void H9_The_Next_Seance_Is_Due_From_The_Seance_Before_It_Not_From_The_Latest_Date()
    {
        var inOrder = TreatmentPlanItem.NextStepDueFromSteps(new[]
        {
            new TreatmentPlanItem.StepTiming(0, Aug, null),
            new TreatmentPlanItem.StepTiming(1, null, 56),
        });
        // Séance 2 done first: nothing precedes séance 1, so the protocol states no due date for it.
        var outOfOrder = TreatmentPlanItem.NextStepDueFromSteps(new[]
        {
            new TreatmentPlanItem.StepTiming(0, null, 30),
            new TreatmentPlanItem.StepTiming(1, Aug, null),
        });

        Assert.Equal(Aug.AddDays(56), inOrder);
        Assert.Null(outOfOrder);
    }

    [Fact]
    public async Task H9_The_Dots_Read_Each_Seance_And_The_Due_Date_Uses_The_Devis_Rule()
    {
        var planId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var fact = new TreatmentInProgressFact(
            planId, "2026-0402", PatientId, itemId, "Couronne", 0,
            StepsTotal: 3, StepsDone: 1, NextStepId: Guid.NewGuid(), NextStepLabel: "Préparation",
            NextStepSequenceNumber: 0, NextStepEstimatedDurationMinutes: null, NextStepMinDaysAfterPrevious: 30,
            LastStepDoneOn: Aug, PlanActRank: 1, PlanActCount: 1);

        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetTreatmentsInProgressAsync(
                ClinicId, It.IsAny<PageRequest?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<TreatmentInProgressFact>.Unpaged(new[] { fact }));
        plans.Setup(r => r.GetStepTimingsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PlanStepTimingRow(planId, itemId, TreatmentPlanItemStatus.InProgress, 0, null, 30),
                new PlanStepTimingRow(planId, itemId, TreatmentPlanItemStatus.InProgress, 1, Aug, null),
                new PlanStepTimingRow(planId, itemId, TreatmentPlanItemStatus.InProgress, 2, null, null),
            });
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient>());

        var page = await TreatmentsInProgressReader.ReadAsync(
            ClinicId, null, plans.Object, appointments.Object, patients.Object, CancellationToken.None);

        var row = Assert.Single(page.Items);
        Assert.Equal(new[] { 2 }, row.DoneStepNumbers);
        // The old composition added the interval to the LATEST date and printed « à partir du 9 sept. ».
        Assert.Null(row.NextStepDueFrom);
    }

    [Fact]
    public void H9_A_Treatment_Seen_Yesterday_On_An_Old_Devis_Is_Not_Chased()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var signedInMay = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        RecallPlanFact Fact(DateTime? lastWork, DateTime? due) => new(
            PatientId, Guid.NewGuid(), "2026-0403", TreatmentPlanStatus.InProgress, signedInMay, signedInMay,
            TotalItems: 3, DoneItems: 1, LastWorkOn: lastWork, NextStepDueFrom: due);

        Assert.False(RecallWorklistRules.IsStalled(Fact(now.AddDays(-1), null), now));
        // An implant waiting its eight weeks is not late at day 15…
        Assert.False(RecallWorklistRules.IsStalled(Fact(now.AddDays(-20), now.AddDays(36)), now));
        // …and is the day its protocol says it is due.
        Assert.True(RecallWorklistRules.IsStalled(Fact(now.AddDays(-60), now.AddDays(-4)), now));
        Assert.Equal(now.AddDays(-4), RecallWorklistRules.StalledSince(Fact(now.AddDays(-60), now.AddDays(-4))));
    }

    // ── I1 / I2 / I4: « Mes actes » ────────────────────────────────────────────────────────────────

    private static ProcedureType Act(string name = "Détartrage") =>
        new(Guid.NewGuid(), ClinicId, name, 30, new ColorHex("#4F83CC"));

    [Fact]
    public async Task I1_An_Archived_Act_Comes_Back()
    {
        var act = Act();
        act.Deactivate();
        var repo = new Mock<IProcedureTypeRepository>();
        repo.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);

        var result = await new ReactivateProcedureTypeCommandHandler(
                repo.Object, Clinic().Object, new Mock<IUnitOfWork>().Object,
                NullLogger<ReactivateProcedureTypeCommandHandler>.Instance)
            .Handle(new ReactivateProcedureTypeCommand { Id = act.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(act.IsActive);
    }

    [Fact]
    public async Task I1_Creating_A_Name_An_Archived_Act_Holds_Names_That_Act_And_Its_Way_Back()
    {
        var archived = Act();
        archived.Deactivate();
        var repo = new Mock<IProcedureTypeRepository>();
        repo.Setup(r => r.ExistsByNameAsync("détartrage", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.GetByNameAsync("détartrage", It.IsAny<CancellationToken>())).ReturnsAsync(archived);

        var result = await new CreateProcedureTypeCommandHandler(
                repo.Object, Clinic().Object, new Mock<IUnitOfWork>().Object,
                NullLogger<CreateProcedureTypeCommandHandler>.Instance)
            .Handle(new CreateProcedureTypeCommand
            {
                Name = "détartrage", DefaultDurationMinutes = 30, ColorHex = "#4F83CC",
            }, CancellationToken.None);

        Assert.Equal(ProcedureTypeRefusals.ArchivedNameCode, result.Code);
        Assert.Contains("Détartrage", result.Error);
    }

    [Fact]
    public async Task I2_A_Rename_Is_Version_Checked_Before_Anything_Is_Saved_And_Tells_The_Agendas()
    {
        var act = Act();
        var visit = Visit(DateTime.UtcNow.AddDays(3));
        visit.SetProcedureType(act.Id, act.DefaultDurationMinutes, act.Color.Value, act.Name);
        var repo = new Mock<IProcedureTypeRepository>();
        repo.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByProcedureTypeIdAsync(act.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });
        var order = new List<string>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SetExpectedVersion(It.IsAny<object>(), 7u)).Callback(() => order.Add("version"));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(1);
        var realtime = new Mock<IRealtimeNotifier>();

        var result = await new UpdateProcedureTypeCommandHandler(
                repo.Object, appointments.Object, Clinic().Object, uow.Object,
                NullLogger<UpdateProcedureTypeCommandHandler>.Instance, realtime.Object)
            .Handle(new UpdateProcedureTypeCommand { Id = act.Id, Name = "Détartrage complet", Version = 7 },
                CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "version", "save" }, order);
        realtime.Verify(r => r.NotifyEntityChangedAsync(ClinicId, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task I4_An_Act_A_Devis_Line_Names_Is_Archived_Not_Deleted_And_Only_Its_Own_Visits_Are_Read()
    {
        var act = Act();
        var repo = new Mock<IProcedureTypeRepository>();
        repo.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByProcedureTypeIdAsync(act.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.CountItemsUsingProcedureTypeAsync(ClinicId, act.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await new DeleteProcedureTypeCommandHandler(
                repo.Object, appointments.Object, Clinic().Object, new Mock<IUnitOfWork>().Object,
                NullLogger<DeleteProcedureTypeCommandHandler>.Instance, plans.Object)
            .Handle(new DeleteProcedureTypeCommand { Id = act.Id }, CancellationToken.None);

        Assert.Equal(new ProcedureTypeDeletion(true, 0, 2), result.Value);
        Assert.False(act.IsActive);
        repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        appointments.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── I3: a practitioner is retired, never deleted ─────────────────────────────────────────────────

    [Fact]
    public async Task I3_A_Practitioner_Left_Off_The_Roster_Is_Retired_And_Comes_Back_When_Sent_Again()
    {
        var kept = new Doctor(Guid.NewGuid(), ClinicId, "Amel", "Hamdane", "GeneralDentist");
        var leaving = new Doctor(Guid.NewGuid(), ClinicId, "Sami", "Trabelsi", "Orthodontist");
        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { kept, leaving });
        var user = User.CreateLocalUser(ClinicId, User.RoleAdmin, "admin@test.tn", "hash", "Admin");
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync("sub", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("sub");
        var handler = new UpdateDoctorsCommandHandler(doctors.Object, users.Object, context.Object,
            new Mock<IUnitOfWork>().Object);
        DoctorDto Entry(Doctor d) => new() { Id = d.Id, FirstName = d.FirstName, LastName = d.LastName, Specialty = d.Specialty };

        await handler.Handle(new UpdateDoctorsCommand { Doctors = new List<DoctorDto> { Entry(kept) } }, CancellationToken.None);
        Assert.False(leaving.IsActive);
        doctors.Verify(r => r.Remove(It.IsAny<Doctor>()), Times.Never);

        await handler.Handle(new UpdateDoctorsCommand { Doctors = new List<DoctorDto> { Entry(kept), Entry(leaving) } },
            CancellationToken.None);
        Assert.True(leaving.IsActive);
        Assert.True(kept.IsActive);
    }
}
