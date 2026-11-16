using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// [cabinet-de-groupe US-1] A patient RDV names its dentist: filled in when the cabinet has exactly one, refused with
/// <see cref="AppointmentPractitioner.RequiredCode"/> when it has several, left alone for a « créneau bloqué ».
///
/// <para>Most of the update cases are about what must NOT be refused: a drag, « Reporter » or a status change never
/// sends the dentist field, and on an old RDV that has none they must keep working in a two-dentist cabinet.</para>
/// </summary>
public class AppointmentPractitionerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime At = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
    private const string Auth0Sub = "local|secretary";

    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IDoctorLeaveRepository> _leaves = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<Doctor> _roster = new();

    public AppointmentPractitionerTests()
    {
        _doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _roster.ToList());
        _clinics.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);
        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        _appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => a);
    }

    private Doctor Dentist(string first, Guid? clinicId = null, bool retired = false)
    {
        var doctor = new Doctor(Guid.NewGuid(), clinicId ?? ClinicId, first, "Ben Salah", "General");
        if (retired)
        {
            doctor.Retire();
        }
        _roster.Add(doctor);
        _doctors.Setup(r => r.GetByIdAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
        return doctor;
    }

    private Task<Result<Guid?>> Resolve(Guid? patientId, Guid? doctorId) =>
        AppointmentPractitioner.ResolveAsync(_doctors.Object, ClinicId, patientId, doctorId, default);

    // ---- The rule ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Named_Dentist_Is_Kept_Without_Reading_The_Roster()
    {
        var chosen = Guid.NewGuid();

        var result = await Resolve(PatientId, chosen);

        Assert.Equal(chosen, result.Value);
        _doctors.Verify(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Blocked_Slot_Stays_Cabinet_Wide_Even_With_Several_Dentists()
    {
        Dentist("Salma");
        Dentist("Nadia");

        var result = await Resolve(patientId: null, doctorId: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_Cabinet_With_One_Dentist_Gets_That_Dentist()
    {
        var salma = Dentist("Salma");

        var result = await Resolve(PatientId, null);

        Assert.Equal(salma.Id, result.Value);
    }

    [Fact]
    public async Task A_Cabinet_With_Several_Dentists_Refuses_With_The_Code()
    {
        Dentist("Salma");
        Dentist("Nadia");

        var result = await Resolve(PatientId, null);

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentPractitioner.RequiredCode, result.Code);
        Assert.Equal(AppointmentPractitioner.RequiredMessage, result.Error);
    }

    [Fact]
    public async Task A_Cabinet_With_No_Dentist_Books_With_None()
    {
        var result = await Resolve(PatientId, null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_Retired_Dentist_Does_Not_Count()
    {
        var salma = Dentist("Salma");
        Dentist("Nadia", retired: true);

        var result = await Resolve(PatientId, null);

        Assert.Equal(salma.Id, result.Value);
    }

    [Fact]
    public async Task Another_Clinics_Dentist_Does_Not_Count()
    {
        var salma = Dentist("Salma");
        Dentist("Karim", clinicId: OtherClinicId);

        var result = await Resolve(PatientId, null);

        Assert.Equal(salma.Id, result.Value);
    }

    // ---- Booking ----------------------------------------------------------------------------------------------

    private CreateAppointmentCommandHandler CreateHandler(Mock<INotificationGenerator>? notifications = null)
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, ClinicId, User.RoleSecretary));
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));
        return new CreateAppointmentCommandHandler(
            _appointments.Object, patients.Object, _doctors.Object, _leaves.Object, _clinics.Object,
            new Mock<IProcedureTypeRepository>().Object, new Mock<ITreatmentPlanRepository>().Object, users.Object,
            context.Object, _uow.Object, (notifications ?? new Mock<INotificationGenerator>()).Object,
            new Mock<IReminderScheduler>().Object, new Mock<IAppointmentGoogleSyncDispatcher>().Object,
            NullLogger<CreateAppointmentCommandHandler>.Instance);
    }

    private static CreateAppointmentCommand Booking(Guid? patientId = null, Guid? doctorId = null) => new()
    {
        PatientId = patientId,
        DoctorId = doctorId,
        AppointmentDateTime = At,
        DurationMinutes = 30,
    };

    [Fact]
    public async Task A_Secretary_Booking_A_Patient_With_No_Dentist_Is_Refused_When_There_Are_Two()
    {
        Dentist("Salma");
        Dentist("Nadia");

        var result = await CreateHandler().Handle(Booking(PatientId), default);

        Assert.Equal(AppointmentPractitioner.RequiredCode, result.Code);
        _appointments.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Secretary_Booking_A_Patient_In_A_One_Dentist_Cabinet_Gets_The_Dentist()
    {
        var salma = Dentist("Salma");
        Appointment? saved = null;
        _appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .Callback((Appointment a, CancellationToken _) => saved = a)
            .ReturnsAsync((Appointment a, CancellationToken _) => a);

        var result = await CreateHandler().Handle(Booking(PatientId), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(salma.Id, saved!.DoctorId);
        Assert.Equal(salma.Id, result.Value!.DoctorId);
    }

    [Fact]
    public async Task A_Blocked_Slot_Is_Booked_For_The_Whole_Cabinet()
    {
        Dentist("Salma");
        Dentist("Nadia");

        var result = await CreateHandler().Handle(Booking(patientId: null), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.DoctorId);
    }

    [Fact]
    public async Task The_Visit_Prompt_Names_The_Person_Who_Booked()
    {
        var salma = Dentist("Salma");
        var notifications = new Mock<INotificationGenerator>();

        await CreateHandler(notifications).Handle(Booking(PatientId), default);

        notifications.Verify(n => n.EnsurePostVisitReviewAsync(
            ClinicId, It.IsAny<Guid>(), salma.Id, It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>(), Auth0Sub), Times.Once);
    }

    // ---- Update -----------------------------------------------------------------------------------------------

    private UpdateAppointmentCommandHandler UpdateHandler()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        return new UpdateAppointmentCommandHandler(
            _appointments.Object, new Mock<IProcedureTypeRepository>().Object, _doctors.Object, _leaves.Object,
            _clinics.Object, new Mock<ITreatmentPlanRepository>().Object, resolver.Object,
            new Mock<IClinicContext>().Object, _uow.Object, new Mock<IAppointmentGoogleSyncDispatcher>().Object,
            new Mock<INotificationGenerator>().Object, new Mock<IReminderScheduler>().Object,
            NullLogger<UpdateAppointmentCommandHandler>.Instance);
    }

    /// <summary>An RDV booked before this rule: a patient, and no dentist.</summary>
    private Appointment UnassignedPatientVisit()
    {
        var appointment = new Appointment(Guid.NewGuid(), ClinicId, PatientId, null, At, TimeSpan.FromMinutes(30));
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        return appointment;
    }

    [Fact]
    public async Task A_Status_Change_On_An_Old_Unassigned_Visit_Still_Works_With_Two_Dentists()
    {
        Dentist("Salma");
        Dentist("Nadia");
        var appointment = UnassignedPatientVisit();

        var result = await UpdateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Cancelled" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Null(appointment.DoctorId);
    }

    [Fact]
    public async Task A_Drag_On_An_Old_Unassigned_Visit_Still_Works_With_Two_Dentists()
    {
        Dentist("Salma");
        Dentist("Nadia");
        var appointment = UnassignedPatientVisit();

        var result = await UpdateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, AppointmentDateTime = At.AddHours(2) }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(appointment.DoctorId);
    }

    [Fact]
    public async Task Saving_The_Dialog_Without_A_Dentist_Is_Refused_With_Two()
    {
        Dentist("Salma");
        Dentist("Nadia");
        var appointment = UnassignedPatientVisit();

        var result = await UpdateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, DoctorId = null, Notes = "x" }, default);

        Assert.Equal(AppointmentPractitioner.RequiredCode, result.Code);
        Assert.Null(appointment.Notes);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saving_The_Dialog_Without_A_Dentist_Fills_The_Only_One()
    {
        var salma = Dentist("Salma");
        var appointment = UnassignedPatientVisit();

        var result = await UpdateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, DoctorId = null }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(salma.Id, appointment.DoctorId);
    }

    // ---- Recurring series -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Series_With_No_Dentist_Is_Refused_With_Two()
    {
        Dentist("Salma");
        Dentist("Nadia");
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));
        var recurring = new Mock<IRecurringAppointmentRepository>();
        var handler = new CreateRecurringSeriesCommandHandler(
            recurring.Object, _appointments.Object, patients.Object, _doctors.Object, _leaves.Object, _clinics.Object,
            new Mock<IProcedureTypeRepository>().Object, resolver.Object, new Mock<IClinicContext>().Object, _uow.Object,
            new Mock<INotificationGenerator>().Object, new Mock<IReminderScheduler>().Object,
            new Mock<IAppointmentGoogleSyncDispatcher>().Object, NullLogger<CreateRecurringSeriesCommandHandler>.Instance);

        var result = await handler.Handle(new CreateRecurringSeriesCommand
        {
            PatientId = PatientId,
            StartDateTime = At,
            DurationMinutes = 30,
            Frequency = "Weekly",
            OccurrenceCount = 3,
        }, default);

        Assert.Equal(AppointmentPractitioner.RequiredCode, result.Code);
        recurring.Verify(r => r.AddAsync(It.IsAny<RecurringAppointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
