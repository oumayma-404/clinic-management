using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.DoctorLeaves;
using ClinicManagement.Application.Features.DoctorLeaves.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.DoctorLeaves;

/// <summary>
/// Create / update / delete a congé: an admin or a secretary any practitioner's, a doctor their own only; an overlap
/// is refused naming the congé already recorded; visits already booked inside it are counted and never touched.
/// </summary>
public class DoctorLeaveCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly Aug12 = new(2026, 8, 12);
    private static readonly DateOnly Aug20 = new(2026, 8, 20);

    private readonly Mock<IDoctorLeaveRepository> _leaves = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public DoctorLeaveCommandTests()
    {
        _appointments.Setup(r => r.GetByClinicIdAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
    }

    private CreateDoctorLeaveCommandHandler Create() =>
        new(_leaves.Object, _doctors.Object, _appointments.Object, _users.Object, _context.Object, _uow.Object);

    private UpdateDoctorLeaveCommandHandler Update() =>
        new(_leaves.Object, _doctors.Object, _appointments.Object, _users.Object, _context.Object, _uow.Object);

    private DeleteDoctorLeaveCommandHandler Delete() =>
        new(_leaves.Object, _doctors.Object, _users.Object, _context.Object, _uow.Object);

    private User SignedIn(string role)
    {
        var user = User.CreateLocalUser(ClinicId, role, $"{role}@cabinet.tn", "HASH", role);
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private Doctor Practitioner(Guid clinicId, string? linkedUserId = null, string firstName = "Salma")
    {
        var doctor = new Doctor(Guid.NewGuid(), clinicId, firstName, "Ben Youssef", "General");
        if (linkedUserId != null) doctor.LinkToUser(linkedUserId);
        _doctors.Setup(r => r.GetByIdAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
        return doctor;
    }

    private DoctorLeave Existing(Doctor doctor, DateOnly start, DateOnly end)
    {
        var leave = new DoctorLeave(Guid.NewGuid(), doctor.ClinicId, doctor.Id, start, end, "Vacances");
        _leaves.Setup(r => r.GetByIdAsync(leave.Id, It.IsAny<CancellationToken>())).ReturnsAsync(leave);
        return leave;
    }

    private static CreateDoctorLeaveCommand NewLeave(Doctor doctor, DateOnly? start = null, DateOnly? end = null) => new()
    {
        DoctorId = doctor.Id,
        StartDate = start ?? Aug12,
        EndDate = end ?? Aug20,
        Note = "Vacances",
    };

    [Theory]
    [InlineData(User.RoleAdmin)]
    [InlineData(User.RoleSecretary)]
    public async Task Admin_And_Secretary_Record_A_Colleagues_Congé(string role)
    {
        SignedIn(role);
        var doctor = Practitioner(ClinicId);

        var result = await Create().Handle(NewLeave(doctor), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Salma Ben Youssef", result.Value!.Leave.DoctorName);
        Assert.Equal(9, result.Value.Leave.DayCount);
        _leaves.Verify(r => r.AddAsync(It.Is<DoctorLeave>(l => l.DoctorId == doctor.Id && l.ClinicId == ClinicId), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Doctor_Records_Their_Own_Congé()
    {
        var user = SignedIn(User.RoleDoctor);
        var own = Practitioner(ClinicId, linkedUserId: user.Id);

        var result = await Create().Handle(NewLeave(own), default);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task A_Doctor_Cannot_Record_A_Colleagues_Congé()
    {
        SignedIn(User.RoleDoctor);
        var colleague = Practitioner(ClinicId, linkedUserId: "local|someone-else");

        var result = await Create().Handle(NewLeave(colleague), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Vous ne pouvez modifier que vos propres congés.", result.Error);
        _leaves.Verify(r => r.AddAsync(It.IsAny<DoctorLeave>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Practitioner_Of_Another_Clinic_Reads_As_Not_Found()
    {
        SignedIn(User.RoleAdmin);
        var foreign = Practitioner(OtherClinicId);

        var result = await Create().Handle(NewLeave(foreign), default);

        Assert.Equal("Praticien introuvable.", result.Error);
        Assert.Equal(DoctorLeaveRules.NotFoundCode, result.Code);
        _leaves.Verify(r => r.AddAsync(It.IsAny<DoctorLeave>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Retired_Practitioner_Gets_No_New_Congé()
    {
        SignedIn(User.RoleAdmin);
        var retired = Practitioner(ClinicId);
        retired.Retire();

        var result = await Create().Handle(NewLeave(retired), default);

        Assert.True(result.IsFailure);
        Assert.Contains("a quitté le cabinet", result.Error);
        _leaves.Verify(r => r.AddAsync(It.IsAny<DoctorLeave>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Overlap_Is_Refused_Naming_The_Congé_Already_Recorded()
    {
        SignedIn(User.RoleAdmin);
        var doctor = Practitioner(ClinicId);
        var existing = Existing(doctor, Aug12, Aug20);
        _leaves.Setup(r => r.FindOverlappingAsync(ClinicId, doctor.Id, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 25), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await Create().Handle(NewLeave(doctor, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 25)), default);

        Assert.Equal("Salma Ben Youssef est déjà en congé du 12/08 au 20/08.", result.Error);
        _leaves.Verify(r => r.AddAsync(It.IsAny<DoctorLeave>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Invalid_Period_Is_Refused_Before_Anything_Is_Read()
    {
        SignedIn(User.RoleAdmin);
        var doctor = Practitioner(ClinicId);

        var result = await Create().Handle(NewLeave(doctor, Aug20, Aug12), default);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saving_Counts_Only_That_Practitioners_Patient_Visits_Still_Holding_Their_Slot()
    {
        SignedIn(User.RoleAdmin);
        var doctor = Practitioner(ClinicId);
        var colleague = Practitioner(ClinicId, firstName: "Nadia");
        // 23:30 UTC on 11/08 is 00:30 on 12/08 in Tunis: the first visit falls on the congé's first clinic day.
        var midnightVisit = new Appointment(Guid.NewGuid(), ClinicId, PatientId, doctor.Id, new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));
        var laterVisit = new Appointment(Guid.NewGuid(), ClinicId, PatientId, doctor.Id, new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));
        var cancelled = new Appointment(Guid.NewGuid(), ClinicId, PatientId, doctor.Id, new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));
        cancelled.Cancel("Annulé");
        var colleagues = new Appointment(Guid.NewGuid(), ClinicId, PatientId, colleague.Id, new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));
        // « Créneau occupé » blocked for her: no patient, so not a rendez-vous to warn about.
        var blockedSlot = new Appointment(Guid.NewGuid(), ClinicId, null, doctor.Id, new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(8));
        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), doctor.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { laterVisit, cancelled, colleagues, blockedSlot, midnightVisit });

        var result = await Create().Handle(NewLeave(doctor), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.AppointmentsDuring);
        Assert.Equal(Aug12, result.Value.FirstAppointmentDay);
        Assert.Equal(AppointmentStatus.Scheduled, laterVisit.Status);
    }

    [Fact]
    public async Task Changing_A_Congé_Does_Not_Collide_With_Itself_And_Checks_The_Version()
    {
        SignedIn(User.RoleSecretary);
        var doctor = Practitioner(ClinicId);
        var leave = Existing(doctor, Aug12, Aug20);

        var result = await Update().Handle(new UpdateDoctorLeaveCommand
        {
            DoctorId = doctor.Id, LeaveId = leave.Id, StartDate = Aug12, EndDate = new DateOnly(2026, 8, 15), Note = "Formation", Version = 42,
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new DateOnly(2026, 8, 15), leave.EndDate);
        Assert.Equal("Formation", leave.Note);
        _leaves.Verify(r => r.FindOverlappingAsync(ClinicId, doctor.Id, Aug12, new DateOnly(2026, 8, 15), leave.Id, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SetExpectedVersion(leave, 42u), Times.Once);
    }

    [Fact]
    public async Task Changing_A_Congé_Into_Another_One_Is_Refused()
    {
        SignedIn(User.RoleAdmin);
        var doctor = Practitioner(ClinicId);
        var leave = Existing(doctor, Aug12, Aug20);
        var september = Existing(doctor, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5));
        _leaves.Setup(r => r.FindOverlappingAsync(ClinicId, doctor.Id, Aug12, new DateOnly(2026, 9, 2), leave.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(september);

        var result = await Update().Handle(new UpdateDoctorLeaveCommand
        {
            DoctorId = doctor.Id, LeaveId = leave.Id, StartDate = Aug12, EndDate = new DateOnly(2026, 9, 2),
        }, default);

        Assert.Equal("Salma Ben Youssef est déjà en congé du 01/09 au 05/09.", result.Error);
        Assert.Equal(Aug20, leave.EndDate);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Congé_Addressed_Through_Another_Practitioner_Reads_As_Not_Found()
    {
        SignedIn(User.RoleAdmin);
        var doctor = Practitioner(ClinicId);
        var colleague = Practitioner(ClinicId, firstName: "Nadia");
        var colleaguesLeave = Existing(colleague, Aug12, Aug20);

        var update = await Update().Handle(new UpdateDoctorLeaveCommand
        {
            DoctorId = doctor.Id, LeaveId = colleaguesLeave.Id, StartDate = Aug12, EndDate = Aug12,
        }, default);
        var delete = await Delete().Handle(new DeleteDoctorLeaveCommand { DoctorId = doctor.Id, LeaveId = colleaguesLeave.Id }, default);

        Assert.Equal("Congé introuvable.", update.Error);
        Assert.Equal("Congé introuvable.", delete.Error);
        Assert.Equal(DoctorLeaveRules.NotFoundCode, update.Code);
        Assert.Equal(DoctorLeaveRules.NotFoundCode, delete.Code);
        _leaves.Verify(r => r.Remove(It.IsAny<DoctorLeave>()), Times.Never);
    }

    [Fact]
    public async Task A_Doctor_Deletes_Their_Own_Congé_But_Not_A_Colleagues()
    {
        var user = SignedIn(User.RoleDoctor);
        var own = Practitioner(ClinicId, linkedUserId: user.Id);
        var colleague = Practitioner(ClinicId, firstName: "Nadia");
        var ownLeave = Existing(own, Aug12, Aug20);
        var colleaguesLeave = Existing(colleague, Aug12, Aug20);

        var mine = await Delete().Handle(new DeleteDoctorLeaveCommand { DoctorId = own.Id, LeaveId = ownLeave.Id }, default);
        var theirs = await Delete().Handle(new DeleteDoctorLeaveCommand { DoctorId = colleague.Id, LeaveId = colleaguesLeave.Id }, default);

        Assert.True(mine.IsSuccess, mine.Error);
        Assert.Equal("Vous ne pouvez modifier que vos propres congés.", theirs.Error);
        _leaves.Verify(r => r.Remove(ownLeave), Times.Once);
        _leaves.Verify(r => r.Remove(colleaguesLeave), Times.Never);
    }
}
