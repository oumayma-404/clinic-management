using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// A practitioner's congé closes their agenda like a closed day: booking or moving them into it is refused with the
/// <see cref="AppointmentScheduling.OutsideWorkingHoursCode"/> code, so « Continuer quand même » still books it.
/// </summary>
public class DoctorLeaveBookingGuardTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly Aug12 = new(2026, 8, 12);
    private static readonly DateOnly Aug20 = new(2026, 8, 20);
    private static readonly DateTime OnLeave = new(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
    private const string Auth0Sub = "auth0|booking-user";

    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IDoctorLeaveRepository> _leaves = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Doctor _salma;
    private readonly Doctor _nadia;
    private readonly DoctorLeave _leave;

    public DoctorLeaveBookingGuardTests()
    {
        _salma = Practitioner("Salma", "Ben Youssef");
        _nadia = Practitioner("Nadia", "Trabelsi");
        _leave = new DoctorLeave(Guid.NewGuid(), ClinicId, _salma.Id, Aug12, Aug20, "Vacances");
        // No working hours anywhere: the cabinet is open, so only the congé can refuse.
        _clinics.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);
        _leaves.Setup(r => r.FindOverlappingAsync(
                ClinicId, _salma.Id, It.Is<DateOnly>(d => d <= Aug20), It.Is<DateOnly>(d => d >= Aug12), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_leave);
        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        _appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => a);
    }

    private Doctor Practitioner(string first, string last)
    {
        var doctor = new Doctor(Guid.NewGuid(), ClinicId, first, last, "General");
        _doctors.Setup(r => r.GetByIdAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
        return doctor;
    }

    private Task<Result<bool>> Check(Guid? doctorId, DateTime startUtc, int minutes = 30) =>
        AppointmentScheduling.CheckWorkingHoursAsync(
            _doctors.Object, _clinics.Object, _leaves.Object, ClinicId, doctorId, startUtc, TimeSpan.FromMinutes(minutes), default);

    [Fact]
    public async Task A_Visit_On_A_Congé_Day_Is_Refused_With_The_Override_Code_And_The_Period()
    {
        var result = await Check(_salma.Id, OnLeave);

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentScheduling.OutsideWorkingHoursCode, result.Code);
        Assert.Equal("Salma Ben Youssef est en congé du 12/08 au 20/08.", result.Error);
    }

    [Fact]
    public async Task Another_Practitioner_The_Same_Day_Is_Unaffected()
    {
        var result = await Check(_nadia.Id, OnLeave);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task A_Visit_With_No_Practitioner_Never_Asks_About_Congés()
    {
        var result = await Check(null, OnLeave);

        Assert.True(result.IsSuccess, result.Error);
        _leaves.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_Congé_Day_Is_The_Clinic_Local_Day_Not_The_Utc_One()
    {
        // 23:30 UTC on 11/08 is 00:30 on 12/08 in Tunis: the congé's first day, while UTC still says the 11th.
        var result = await Check(_salma.Id, new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc));

        Assert.Equal(AppointmentScheduling.OutsideWorkingHoursCode, result.Code);
        _leaves.Verify(r => r.FindOverlappingAsync(ClinicId, _salma.Id, Aug12, Aug12, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Practitioner_Of_Another_Clinic_Is_Not_Looked_Up_For_Congés()
    {
        var foreign = new Doctor(Guid.NewGuid(), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Karim", "Hamdi", "General");
        _doctors.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        await Check(foreign.Id, OnLeave);

        _leaves.VerifyNoOtherCalls();
    }

    private CreateAppointmentCommandHandler CreateHandler()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>())).ReturnsAsync(new User(Auth0Sub, ClinicId, User.RoleSecretary));
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));
        return new CreateAppointmentCommandHandler(
            _appointments.Object, patients.Object, _doctors.Object, _leaves.Object, _clinics.Object,
            new Mock<IProcedureTypeRepository>().Object, new Mock<ITreatmentPlanRepository>().Object, users.Object,
            context.Object, _uow.Object, new Mock<INotificationGenerator>().Object, new Mock<IReminderScheduler>().Object,
            new Mock<IAppointmentGoogleSyncDispatcher>().Object, NullLogger<CreateAppointmentCommandHandler>.Instance);
    }

    private static CreateAppointmentCommand Booking(Guid doctorId, bool allowOutsideWorkingHours = false) => new()
    {
        PatientId = PatientId,
        DoctorId = doctorId,
        AppointmentDateTime = OnLeave,
        DurationMinutes = 30,
        AllowOutsideWorkingHours = allowOutsideWorkingHours,
    };

    [Fact]
    public async Task Booking_A_Practitioner_On_Congé_Is_Refused_Until_Confirmed()
    {
        var refused = await CreateHandler().Handle(Booking(_salma.Id), default);
        _appointments.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);

        var confirmed = await CreateHandler().Handle(Booking(_salma.Id, allowOutsideWorkingHours: true), default);

        Assert.Equal(AppointmentScheduling.OutsideWorkingHoursCode, refused.Code);
        Assert.Contains("est en congé du 12/08 au 20/08", refused.Error);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        _appointments.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private UpdateAppointmentCommandHandler UpdateHandler()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        return new UpdateAppointmentCommandHandler(
            _appointments.Object, new Mock<IProcedureTypeRepository>().Object, _doctors.Object, _leaves.Object,
            _clinics.Object, new Mock<ITreatmentPlanRepository>().Object, resolver.Object, new Mock<IClinicContext>().Object,
            _uow.Object, new Mock<IAppointmentGoogleSyncDispatcher>().Object, new Mock<INotificationGenerator>().Object,
            new Mock<IReminderScheduler>().Object, NullLogger<UpdateAppointmentCommandHandler>.Instance);
    }

    private Appointment Booked(DateTime at)
    {
        var appointment = new Appointment(Guid.NewGuid(), ClinicId, PatientId, _salma.Id, at, TimeSpan.FromMinutes(30));
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        return appointment;
    }

    [Fact]
    public async Task Moving_A_Visit_Into_The_Congé_Is_Refused_And_Leaves_It_Where_It_Was()
    {
        var before = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);
        var visit = Booked(before);

        var result = await UpdateHandler().Handle(new UpdateAppointmentCommand { Id = visit.Id, AppointmentDateTime = OnLeave }, default);

        Assert.Equal(AppointmentScheduling.OutsideWorkingHoursCode, result.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Editing_The_Notes_Of_A_Visit_Already_Inside_The_Congé_Is_Not_Refused()
    {
        var visit = Booked(OnLeave);

        var result = await UpdateHandler().Handle(new UpdateAppointmentCommand { Id = visit.Id, Notes = "Rappeler le patient" }, default);

        Assert.True(result.IsSuccess, result.Error);
    }
}
