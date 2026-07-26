using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// [AC-17] <c>PUT /api/appointments/{id}</c> can move an appointment's treatment-plan act link and can clear
/// it. Until now the link was write-once at creation and <c>Appointment.SetTreatmentPlanItem</c> had zero
/// callers, so rescheduling an appointment onto a different act left a stale link behind.
/// <para>
/// The field is tri-state, and that is the load-bearing part: <b>omitting</b> it must leave the link
/// untouched. Every existing caller (the edit dialog, status flips, drag-to-reschedule) sends neither field,
/// and <c>Appointment.TreatmentPlanItemId</c> has no FK — so if "absent" meant "clear", any unrelated edit
/// would silently orphan the link with nothing at the database level to catch it.
/// </para>
/// </summary>
public class AppointmentPlanLinkUpdateTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherPatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime At = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public AppointmentPlanLinkUpdateTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    private UpdateAppointmentCommandHandler CreateHandler() => new(
        _appointments.Object,
        new Mock<IProcedureTypeRepository>().Object,
        new Mock<IDoctorRepository>().Object,
        _plans.Object,
        _clinicResolver.Object,
        new Mock<IClinicContext>().Object,
        _uow.Object,
        new Mock<IAppointmentGoogleSyncDispatcher>().Object,
        new Mock<INotificationGenerator>().Object,
        new Mock<IReminderScheduler>().Object,
        NullLogger<UpdateAppointmentCommandHandler>.Instance);

    private Appointment AppointmentLinkedTo(Guid? itemId)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, doctorId: null, At, TimeSpan.FromMinutes(30),
            treatmentPlanItemId: itemId);
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    /// <summary>An accepted plan for the given clinic/patient, registered with the repository mock.</summary>
    private TreatmentPlan PlanWithTwoActs(Guid clinicId, Guid patientId)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), clinicId, patientId, "Plan");
        plan.SetItems(new[]
        {
            ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }),
            ("Détartrage", 60m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 12 }),
        });
        plan.Accept("2026-0001");
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plan;
    }

    // [AC-17] Moving the link onto another act of the plan persists the new act.
    [Fact]
    public async Task Update_Moves_The_Plan_Act_Link()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var acts = plan.Items.ToList();
        var appointment = AppointmentLinkedTo(acts[0].Id);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanId = plan.Id,
            TreatmentPlanItemId = acts[1].Id,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(acts[1].Id, appointment.TreatmentPlanItemId);
        Assert.Equal(acts[1].Id, result.Value!.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-17] An explicit null clears the link — the appointment stays, it just no longer speaks for an act.
    [Fact]
    public async Task Update_Clears_The_Plan_Act_Link_On_An_Explicit_Null()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var appointment = AppointmentLinkedTo(plan.Items.First().Id);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanItemId = null,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(appointment.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-17] THE regression guard: an edit that doesn't mention the plan fields leaves the link alone.
    // Every pre-existing caller looks like this, so a tri-state slip here would orphan links app-wide.
    [Fact]
    public async Task An_Unrelated_Edit_Leaves_The_Plan_Act_Link_Untouched()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var linkedActId = plan.Items.First().Id;
        var appointment = AppointmentLinkedTo(linkedActId);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            Notes = "Le patient a appelé pour confirmer",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(linkedActId, appointment.TreatmentPlanItemId);
        // Nothing to validate, so the plan repository is never even consulted.
        _plans.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-17][AC-24] An act on another clinic's plan is rejected and nothing is written.
    [Fact]
    public async Task Linking_A_Cross_Clinic_Act_Is_Rejected()
    {
        var foreignPlan = PlanWithTwoActs(OtherClinicId, PatientId);
        var appointment = AppointmentLinkedTo(null);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanId = foreignPlan.Id,
            TreatmentPlanItemId = foreignPlan.Items.First().Id,
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(appointment.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-17] A plan belonging to a *different patient* of the same clinic is rejected too — the link must
    // never cross a patient, or one patient's appointment would mark another's act as scheduled.
    [Fact]
    public async Task Linking_An_Act_Of_Another_Patients_Plan_Is_Rejected()
    {
        var otherPatientPlan = PlanWithTwoActs(ClinicId, OtherPatientId);
        var appointment = AppointmentLinkedTo(null);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanId = otherPatientPlan.Id,
            TreatmentPlanItemId = otherPatientPlan.Items.First().Id,
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(appointment.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-17] An act id that isn't on the named plan is rejected rather than stored blindly.
    [Fact]
    public async Task Linking_An_Unknown_Act_Is_Rejected()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var appointment = AppointmentLinkedTo(null);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanId = plan.Id,
            TreatmentPlanItemId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(appointment.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-17] Sending an act without its plan is rejected — AppointmentPlanLink needs the plan to scope the
    // lookup, and guessing it would defeat the tenant/patient check.
    [Fact]
    public async Task Linking_An_Act_Without_Its_Plan_Is_Rejected()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var appointment = AppointmentLinkedTo(null);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanItemId = plan.Items.First().Id,
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(appointment.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-17] A patient-less "busy slot" cannot carry a plan act — there is no patient to scope it to.
    [Fact]
    public async Task Linking_An_Act_To_A_Patientless_Slot_Is_Rejected()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var slot = new Appointment(
            Guid.NewGuid(), ClinicId, patientId: null, doctorId: null, At, TimeSpan.FromMinutes(30));
        _appointments.Setup(r => r.GetByIdAsync(slot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(slot);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = slot.Id,
            TreatmentPlanId = plan.Id,
            TreatmentPlanItemId = plan.Items.First().Id,
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(slot.TreatmentPlanItemId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-17] Re-sending the act the appointment already carries is a no-op, not a redundant re-validation.
    [Fact]
    public async Task Re_Sending_The_Same_Act_Does_Not_Re_Validate()
    {
        var plan = PlanWithTwoActs(ClinicId, PatientId);
        var actId = plan.Items.First().Id;
        var appointment = AppointmentLinkedTo(actId);

        var result = await CreateHandler().Handle(new UpdateAppointmentCommand
        {
            Id = appointment.Id,
            TreatmentPlanId = plan.Id,
            TreatmentPlanItemId = actId,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(actId, appointment.TreatmentPlanItemId);
        _plans.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
