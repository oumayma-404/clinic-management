using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// [AC-10][AC-10a][AC-11][AC-12] Every nullable field on <c>PUT /api/appointments/{id}</c> is tri-state:
/// omitting it leaves the field untouched, an explicit <c>null</c> clears it.
///
/// <para>
/// The regression that matters is <c>procedureTypeId</c>. It used to be compared against the stored value with
/// no notion of "provided", so an omitted key bound to <c>null</c>, read as "different from the current act",
/// and wiped the procedure type together with its snapshot duration and colour. Cancelling an appointment posts
/// <c>{ status }</c> alone — from the edit dialog <i>and</i> from the AI assistant, which builds the command
/// directly and bypasses the controller — so every cancellation destroyed the act it was cancelling.
/// </para>
/// <para>
/// The mirror-image defect is covered too: <c>notes</c>, <c>doctorName</c> and <c>doctorId</c> treated
/// <c>null</c> as "not provided", so they could never be <i>cleared</i>.
/// </para>
/// </summary>
public class AppointmentPartialUpdateTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ProcedureTypeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid DoctorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateTime At = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    private const int SnapshotDuration = 45;
    private const string SnapshotColour = "#4F83CC";

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public AppointmentPartialUpdateTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    private UpdateAppointmentCommandHandler CreateHandler() => new(
        _appointments.Object,
        _procedureTypes.Object,
        _doctors.Object,
        new Mock<ITreatmentPlanRepository>().Object,
        _clinicResolver.Object,
        new Mock<IClinicContext>().Object,
        _uow.Object,
        new Mock<IAppointmentGoogleSyncDispatcher>().Object,
        new Mock<INotificationGenerator>().Object,
        new Mock<IReminderScheduler>().Object,
        NullLogger<UpdateAppointmentCommandHandler>.Instance);

    /// <summary>An appointment that HAS a booked act — the fixture the old tests were missing.</summary>
    private Appointment AppointmentWithAnAct(string? notes = "Contrôle annuel", Guid? doctorId = null)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, doctorId, At, TimeSpan.FromMinutes(30));
        appointment.SetProcedureType(ProcedureTypeId, SnapshotDuration, SnapshotColour);
        appointment.UpdateNotes(notes);

        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    // [AC-10] THE regression. Cancelling posts { status } alone — the act, its snapshot duration and its
    // colour must all survive. Before the tri-state guard every one of these was nulled.
    [Fact]
    public async Task Cancelling_Does_Not_Wipe_The_Booked_Act()
    {
        var appointment = AppointmentWithAnAct();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Cancelled" }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(ProcedureTypeId, appointment.ProcedureTypeId);
        Assert.Equal(SnapshotDuration, appointment.ProcedureDurationMinutes);
        Assert.Equal(SnapshotColour, appointment.ProcedureColorHex);
    }

    // [AC-10] The same holds for any unrelated edit — a notes-only change must not touch the act.
    [Fact]
    public async Task An_Unrelated_Edit_Leaves_The_Booked_Act_Untouched()
    {
        var appointment = AppointmentWithAnAct();

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "Nouvelle note" }, default);

        Assert.Equal(ProcedureTypeId, appointment.ProcedureTypeId);
        Assert.Equal(SnapshotDuration, appointment.ProcedureDurationMinutes);
        // The procedure-type repository is never even consulted when the field was not sent.
        _procedureTypes.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-10a] An EXPLICIT null still clears the act and both snapshots — the capability is preserved.
    [Fact]
    public async Task An_Explicit_Null_Clears_The_Booked_Act()
    {
        var appointment = AppointmentWithAnAct();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, ProcedureTypeId = null }, default);

        Assert.True(result.IsSuccess);
        Assert.Null(appointment.ProcedureTypeId);
        Assert.Null(appointment.ProcedureDurationMinutes);
        Assert.Null(appointment.ProcedureColorHex);
    }

    // [AC-11] Notes: explicit null clears. Emptying the notes box used to be a silent no-op.
    [Fact]
    public async Task An_Explicit_Null_Clears_The_Notes()
    {
        var appointment = AppointmentWithAnAct(notes: "Contrôle annuel");

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = null }, default);

        Assert.Null(appointment.Notes);
    }

    // [AC-11] …and omitting notes leaves them alone.
    [Fact]
    public async Task Omitting_Notes_Leaves_Them_Untouched()
    {
        var appointment = AppointmentWithAnAct(notes: "Contrôle annuel");

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Confirmed" }, default);

        Assert.Equal("Contrôle annuel", appointment.Notes);
    }

    // [AC-11] Practitioner: explicit null unassigns. Appointment.SetDoctorId(null) was unreachable before —
    // the old guard was `DoctorId.HasValue`, so the clearing branch was dead code.
    [Fact]
    public async Task An_Explicit_Null_Unassigns_The_Practitioner()
    {
        var appointment = AppointmentWithAnAct(doctorId: DoctorId);

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, DoctorId = null }, default);

        Assert.Null(appointment.DoctorId);
        // Clearing needs no lookup — there is no doctor to validate.
        _doctors.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-11] …and omitting the practitioner leaves the assignment alone.
    [Fact]
    public async Task Omitting_The_Practitioner_Leaves_The_Assignment_Untouched()
    {
        var appointment = AppointmentWithAnAct(doctorId: DoctorId);

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "x" }, default);

        Assert.Equal(DoctorId, appointment.DoctorId);
    }

    // [AC-12] An unparseable status is refused rather than silently ignored. It used to return 200 having
    // changed nothing, so a typo read as success.
    [Fact]
    public async Task An_Unparseable_Status_Is_Refused()
    {
        var appointment = AppointmentWithAnAct();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Annulé" }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("Statut de rendez-vous invalide", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-12] A non-positive duration is refused rather than falling through the guard.
    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public async Task A_Non_Positive_Duration_Is_Refused(int minutes)
    {
        var appointment = AppointmentWithAnAct();

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, DurationMinutes = minutes }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("durée", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-10] Setting a real act still works end to end, snapshots included.
    [Fact]
    public async Task Setting_An_Act_Snapshots_Its_Duration_And_Colour()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, doctorId: null, At, TimeSpan.FromMinutes(30));
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);

        var procedureType = new ProcedureType(
            ProcedureTypeId, ClinicId, "Détartrage", SnapshotDuration,
            ColorHex.FromString(SnapshotColour), defaultCost: 60m);
        _procedureTypes.Setup(r => r.GetByIdAsync(ProcedureTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(procedureType);

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, ProcedureTypeId = ProcedureTypeId }, default);

        Assert.Equal(ProcedureTypeId, appointment.ProcedureTypeId);
        Assert.Equal(SnapshotDuration, appointment.ProcedureDurationMinutes);
        Assert.Equal(SnapshotColour, appointment.ProcedureColorHex);
    }
}
