using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// `salle-attente` — the reception commands and « Patient suivant » over a mocked day. Fixtures are anchored on the
/// real clock because the handlers read « today » from it and a restore refuses a time in the future; the repository
/// mock applies no date filter, so « today » here is whatever the fixture hands it.
/// </summary>
public class WaitingRoomCommandsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = DateTime.UtcNow;

    private sealed class Harness
    {
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IStaffMessageRepository> Messages { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ICurrentClinicResolver> ClinicResolver { get; } = new();
        public Mock<IClinicContext> Context { get; } = new();
        public Mock<IMediator> Mediator { get; } = new();
        public Mock<INotificationGenerator> Notifications { get; } = new();
        public Mock<IAppointmentGoogleSyncDispatcher> GoogleSync { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();
        public Mock<IStaffChatPushQueue> Push { get; } = new();

        public List<Appointment> Day { get; } = new();
        public List<Doctor> Dentists { get; } = new();

        public Harness(params Doctor[] dentists)
        {
            Dentists.AddRange(dentists);
            ClinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            Appointments
                .Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Day.ToList());
            Appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Day.FirstOrDefault(a => a.Id == id));
            Doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Dentists.ToList());
            Mediator.Setup(m => m.Send(It.IsAny<SendArrivalNoticeCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<StaffMessageDto>.Failure("pas de praticien", "no-dentist"));
        }

        public Appointment Booked(Doctor? dentist = null, int slotMinutesAgo = 10)
        {
            var appointment = new Appointment(
                Guid.NewGuid(), ClinicId, Guid.NewGuid(), dentist?.Id, Now.AddMinutes(-slotMinutesAgo), TimeSpan.FromMinutes(30));
            Day.Add(appointment);
            return appointment;
        }

        public Appointment At(WaitingRoomLane lane, Doctor? dentist = null, int minutesAgo = 20)
        {
            var appointment = Booked(dentist);
            appointment.MoveInWaitingRoom(lane, Now.AddMinutes(-minutesAgo));
            return appointment;
        }

        public void SignedInAs(string role, Doctor? linked = null)
        {
            var userId = "local|" + Guid.NewGuid();
            Context.Setup(c => c.GetUserRole()).Returns(role);
            Context.Setup(c => c.GetUserId()).Returns(userId);
            Doctors.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(linked);
        }

        public MoveInWaitingRoomCommandHandler Move() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object, Mediator.Object,
            NullLogger<MoveInWaitingRoomCommandHandler>.Instance);

        public RestoreWaitingRoomCommandHandler Restore() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object, Context.Object);

        public RegisterWalkInCommandHandler WalkIn() => new(
            Appointments.Object, Doctors.Object, Patients.Object, UnitOfWork.Object, ClinicResolver.Object,
            Notifications.Object, GoogleSync.Object, Mediator.Object, NullLogger<RegisterWalkInCommandHandler>.Instance);

        public CallNextPatientCommandHandler CallNext() => new(
            Appointments.Object, Doctors.Object, Users.Object, Patients.Object, Messages.Object, UnitOfWork.Object,
            ClinicResolver.Object, Context.Object, Realtime.Object, Push.Object, NullLogger<CallNextPatientCommandHandler>.Instance);

        public GetWaitingRoomQueryHandler Board() => new(
            Appointments.Object, Doctors.Object, ClinicResolver.Object, Context.Object);
    }

    private static Doctor Dentist(string name) => new(Guid.NewGuid(), ClinicId, name, "Dentiste", "General");

    // ── « Arrivé », « Faire entrer », « Terminer » ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Arrived_Moves_The_Visit_Saves_And_Asks_For_The_Dentists_Notice()
    {
        var harness = new Harness(Dentist("Sami"));
        var visit = harness.Booked();

        var result = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = visit.Id, Lane = "Waiting" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Changed);
        Assert.Equal(WaitingRoomLane.Waiting, visit.WaitingRoomLane);
        Assert.Equal("Upcoming", Assert.Single(result.Value.Before).Lane);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        harness.Mediator.Verify(m => m.Send(
            It.Is<SendArrivalNoticeCommand>(c => c.AppointmentId == visit.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The notice is best-effort: a dentist with no account, or a mediator that throws, never costs the arrival.
    [Fact]
    public async Task A_Notice_That_Cannot_Go_Does_Not_Fail_The_Arrival()
    {
        var harness = new Harness(Dentist("Sami"));
        harness.Mediator.Setup(m => m.Send(It.IsAny<SendArrivalNoticeCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var visit = harness.Booked();

        var result = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = visit.Id, Lane = "Waiting" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.Waiting, visit.WaitingRoomLane);
    }

    [Fact]
    public async Task Bringing_A_Patient_In_Ends_Only_His_Own_Dentists_Chair()
    {
        var sami = Dentist("Sami");
        var leila = Dentist("Leila");
        var harness = new Harness(sami, leila);
        var samisChair = harness.At(WaitingRoomLane.InChair, sami);
        var leilasChair = harness.At(WaitingRoomLane.InChair, leila);
        var next = harness.At(WaitingRoomLane.Waiting, sami);

        var result = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = next.Id, Lane = "InChair" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, next.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.Done, samisChair.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.InChair, leilasChair.WaitingRoomLane);
        Assert.Equal(2, result.Value!.Before.Count);
    }

    // One dentist, and a RDV booked at reception naming nobody: still one chair (90 of 121 prod bookings name nobody).
    [Fact]
    public async Task In_A_One_Dentist_Cabinet_An_Unassigned_Visit_Shares_The_Dentists_Chair()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        var seated = harness.At(WaitingRoomLane.InChair, sami);
        var unassigned = harness.At(WaitingRoomLane.Waiting, dentist: null);

        await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = unassigned.Id, Lane = "InChair" }, CancellationToken.None);

        Assert.Equal(WaitingRoomLane.Done, seated.WaitingRoomLane);
    }

    [Fact]
    public async Task Moving_To_The_Lane_It_Is_Already_In_Saves_Nothing()
    {
        var harness = new Harness(Dentist("Sami"));
        var visit = harness.At(WaitingRoomLane.Waiting);

        var result = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = visit.Id, Lane = "Waiting" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Changed);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Salon")]
    [InlineData("")]
    public async Task An_Unknown_Lane_Is_Refused_By_Code(string lane)
    {
        var harness = new Harness(Dentist("Sami"));
        var visit = harness.Booked();

        var result = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = visit.Id, Lane = lane }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.UnknownLaneCode, result.Code);
    }

    [Fact]
    public async Task A_Visit_Not_On_Todays_Board_Is_Refused_And_Another_Clinics_Reads_As_Not_Found()
    {
        var harness = new Harness(Dentist("Sami"));
        var yesterday = new Appointment(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now.AddDays(-1), TimeSpan.FromMinutes(30));
        var foreign = new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now, TimeSpan.FromMinutes(30));
        harness.Appointments.Setup(r => r.GetByIdAsync(yesterday.Id, It.IsAny<CancellationToken>())).ReturnsAsync(yesterday);
        harness.Appointments.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var notToday = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = yesterday.Id, Lane = "Waiting" }, CancellationToken.None);
        var notFound = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = foreign.Id, Lane = "Waiting" }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NotTodayCode, notToday.Code);
        Assert.Equal(WaitingRoomRefusals.NotFoundCode, notFound.Code);
        Assert.Equal(WaitingRoomLane.Upcoming, foreign.WaitingRoomLane);
    }

    [Fact]
    public async Task A_Walk_In_Cannot_Be_Sent_Back_To_A_Slot_He_Never_Had()
    {
        var harness = new Harness(Dentist("Sami"));
        var walkIn = Appointment.WalkIn(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now.AddMinutes(-5), TimeSpan.FromMinutes(30));
        harness.Day.Add(walkIn);

        var result = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = walkIn.Id, Lane = "Upcoming" }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.WalkInHasNoSlotCode, result.Code);
        Assert.Equal(WaitingRoomLane.Waiting, walkIn.WaitingRoomLane);
    }

    // ── « Annuler » ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Annuler_Puts_Back_Both_Visits_A_Call_In_Touched()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        var seated = harness.At(WaitingRoomLane.InChair, sami, minutesAgo: 30);
        var next = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 25);
        var seatedBefore = (seated.WaitingRoomLane, seated.CalledInAtUtc, seated.Status);
        var nextBefore = (next.WaitingRoomLane, next.ArrivedAtUtc, next.Status);

        var moved = await harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = next.Id, Lane = "InChair" }, CancellationToken.None);
        var undone = await harness.Restore().Handle(
            new RestoreWaitingRoomCommand { Before = moved.Value!.Before }, CancellationToken.None);

        Assert.True(undone.IsSuccess);
        Assert.Equal(seatedBefore, (seated.WaitingRoomLane, seated.CalledInAtUtc, seated.Status));
        Assert.Equal(nextBefore, (next.WaitingRoomLane, next.ArrivedAtUtc, next.Status));
    }

    [Fact]
    public async Task Annuler_Takes_Away_A_Walk_In_Nobody_Has_Seen_Yet_Without_Deleting_It()
    {
        var harness = new Harness(Dentist("Sami"));
        harness.SignedInAs(User.RoleSecretary);
        var walkIn = Appointment.WalkIn(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now.AddMinutes(-1), TimeSpan.FromMinutes(30));
        harness.Day.Add(walkIn);

        var result = await harness.Restore().Handle(
            new RestoreWaitingRoomCommand { RemoveWalkInIds = { walkIn.Id } }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(walkIn.IsDisregarded);
        Assert.Equal(WaitingRoomLane.None, walkIn.WaitingRoomLane);
        harness.Appointments.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Walk_In_Already_Seen_Is_Not_Taken_Away()
    {
        var harness = new Harness(Dentist("Sami"));
        var walkIn = Appointment.WalkIn(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now.AddMinutes(-10), TimeSpan.FromMinutes(30));
        walkIn.MoveInWaitingRoom(WaitingRoomLane.InChair, Now.AddMinutes(-5));
        harness.Day.Add(walkIn);

        var result = await harness.Restore().Handle(
            new RestoreWaitingRoomCommand { RemoveWalkInIds = { walkIn.Id } }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.WalkInHasWorkCode, result.Code);
        Assert.False(walkIn.IsDisregarded);
    }

    // ── « Sans RDV » ───────────────────────────────────────────────────────────────────────────────────────────

    private static Patient Patient(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), ClinicId, "Amira", "Ben Salah", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "F");

    [Fact]
    public async Task A_Walk_In_Is_Booked_Now_With_The_Only_Dentist_And_Already_Waiting()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        var patient = Patient();
        harness.Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        Appointment? added = null;
        harness.Appointments.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .Callback<Appointment, CancellationToken>((a, _) => added = a)
            .ReturnsAsync((Appointment a, CancellationToken _) => a);

        var result = await harness.WalkIn().Handle(new RegisterWalkInCommand { PatientId = patient.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.True(added!.IsWalkIn);
        Assert.Equal(sami.Id, added.DoctorId);
        Assert.Equal(WaitingRoomLane.Waiting, added.WaitingRoomLane);
        Assert.Equal(new[] { added.Id }, result.Value!.RemoveWalkInIds);
        harness.GoogleSync.Verify(g => g.Dispatch(added.Id, ClinicId), Times.Once);
    }

    // The deduction: « Sans RDV » for somebody who has a RDV today marks THAT RDV arrived, never a second visit.
    [Fact]
    public async Task A_Patient_With_A_Rdv_Today_Is_Marked_Arrived_On_It_Instead_Of_Booking_A_Second_Visit()
    {
        var harness = new Harness(Dentist("Sami"));
        var patient = Patient();
        harness.Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var rdv = new Appointment(Guid.NewGuid(), ClinicId, patient.Id, null, Now.AddMinutes(30), TimeSpan.FromMinutes(30));
        harness.Day.Add(rdv);

        var result = await harness.WalkIn().Handle(new RegisterWalkInCommand { PatientId = patient.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.UsedTodaysAppointment);
        Assert.Equal(rdv.Id, result.Value.AppointmentId);
        Assert.Equal(WaitingRoomLane.Waiting, rdv.WaitingRoomLane);
        Assert.Empty(result.Value.RemoveWalkInIds);
        harness.Appointments.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Patient_Already_Waiting_Is_Refused_By_Name()
    {
        var harness = new Harness(Dentist("Sami"));
        var patient = Patient();
        harness.Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var rdv = new Appointment(Guid.NewGuid(), ClinicId, patient.Id, null, Now, TimeSpan.FromMinutes(30));
        rdv.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now.AddMinutes(-3));
        harness.Day.Add(rdv);

        var result = await harness.WalkIn().Handle(new RegisterWalkInCommand { PatientId = patient.Id }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.AlreadyHereCode, result.Code);
        Assert.Contains("Amira Ben Salah", result.Error);
    }

    [Fact]
    public async Task In_A_Cabinet_De_Groupe_A_Walk_In_Needs_A_Dentist_And_Only_One_Of_Its_Own()
    {
        var harness = new Harness(Dentist("Sami"), Dentist("Leila"));
        var patient = Patient();
        harness.Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var none = await harness.WalkIn().Handle(new RegisterWalkInCommand { PatientId = patient.Id }, CancellationToken.None);
        var foreign = await harness.WalkIn().Handle(
            new RegisterWalkInCommand { PatientId = patient.Id, DoctorId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NeedsDentistCode, none.Code);
        Assert.Equal(WaitingRoomRefusals.UnknownDentistCode, foreign.Code);
    }

    [Fact]
    public async Task Another_Clinics_Patient_Reads_As_Not_Found()
    {
        var harness = new Harness(Dentist("Sami"));
        var foreign = new Patient(Guid.NewGuid(), Guid.NewGuid(), "X", "Y", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M");
        harness.Patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await harness.WalkIn().Handle(new RegisterWalkInCommand { PatientId = foreign.Id }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NotFoundCode, result.Code);
    }

    // ── « Patient suivant » ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Patient_Suivant_Ends_The_Chair_And_Calls_The_First_Of_The_Queue()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var seated = harness.At(WaitingRoomLane.InChair, sami, minutesAgo: 40);
        var later = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 5);
        var first = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 15);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand { ExpectedNextId = first.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.Done, seated.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.InChair, first.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.Waiting, later.WaitingRoomLane);
        Assert.Equal(first.Id, result.Value!.AppointmentId);
        Assert.Equal(2, result.Value.Before.Count);
    }

    // Two devices, or a double tap: the second press names a patient who is no longer waiting.
    [Fact]
    public async Task A_Second_Press_For_The_Same_Patient_Is_Refused_And_Calls_Nobody_Else()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var first = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 15);
        var second = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 5);

        await harness.CallNext().Handle(new CallNextPatientCommand { ExpectedNextId = first.Id }, CancellationToken.None);
        var again = await harness.CallNext().Handle(new CallNextPatientCommand { ExpectedNextId = first.Id }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.AlreadyCalledCode, again.Code);
        Assert.Equal(WaitingRoomLane.InChair, first.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.Waiting, second.WaitingRoomLane);
    }

    [Fact]
    public async Task Appeler_Takes_The_Named_Patient_Out_Of_Order()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var first = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 15);
        var chosen = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 5);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand { AppointmentId = chosen.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, chosen.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.Waiting, first.WaitingRoomLane);
    }

    [Fact]
    public async Task In_A_Cabinet_De_Groupe_A_Dentist_Takes_An_Unassigned_Patient_But_Not_A_Colleagues()
    {
        var sami = Dentist("Sami");
        var leila = Dentist("Leila");
        var harness = new Harness(sami, leila);
        harness.SignedInAs(User.RoleDoctor, sami);
        var leilas = harness.At(WaitingRoomLane.Waiting, leila, minutesAgo: 30);
        var unassigned = harness.At(WaitingRoomLane.Waiting, dentist: null, minutesAgo: 10);

        var takesColleagues = await harness.CallNext().Handle(
            new CallNextPatientCommand { AppointmentId = leilas.Id }, CancellationToken.None);
        var next = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.OtherDentistCode, takesColleagues.Code);
        Assert.Equal(unassigned.Id, next.Value!.AppointmentId);
        Assert.Equal(sami.Id, unassigned.DoctorId);
        Assert.True(unassigned.BookedWithOverlap);
        Assert.Equal(WaitingRoomLane.Waiting, leilas.WaitingRoomLane);
    }

    [Fact]
    public async Task With_Nobody_Waiting_The_Button_Only_Ends_The_Chair()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var seated = harness.At(WaitingRoomLane.InChair, sami);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.Done, seated.WaitingRoomLane);
        Assert.Equal("Done", result.Value!.Lane);
    }

    [Fact]
    public async Task With_Nobody_Waiting_And_The_Chair_Free_It_Says_So()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NobodyCode, result.Code);
    }

    [Fact]
    public async Task A_Secretary_Is_Never_The_Dentist_Even_In_A_One_Dentist_Cabinet()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleSecretary);
        harness.At(WaitingRoomLane.Waiting, sami);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NotADentistCode, result.Code);
    }

    // An admin who is not linked to a practitioner record stands for the only dentist; with two he cannot.
    [Fact]
    public async Task An_Unlinked_Admin_Stands_For_The_Only_Dentist()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleAdmin);
        var waiting = harness.At(WaitingRoomLane.Waiting, sami);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, waiting.WaitingRoomLane);

        var group = new Harness(Dentist("A"), Dentist("B"));
        group.SignedInAs(User.RoleAdmin);
        var refused = await group.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);
        Assert.Equal(WaitingRoomRefusals.NotADentistCode, refused.Code);
    }

    // ── The board read ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Board_Serves_Each_Lane_In_Its_Order_And_Numbers_The_Queue()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleSecretary);
        var upcomingLate = harness.Booked(sami, slotMinutesAgo: -60);
        var upcomingSoon = harness.Booked(sami, slotMinutesAgo: -30);
        var waitingSecond = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 5);
        var waitingFirst = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 15);
        var seated = harness.At(WaitingRoomLane.InChair, sami);
        var left = harness.At(WaitingRoomLane.Left, sami);
        var blocked = new Appointment(Guid.NewGuid(), ClinicId, null, null, Now, TimeSpan.FromMinutes(30));
        harness.Day.Add(blocked);

        var board = (await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None)).Value!;

        Assert.Equal(new[] { upcomingSoon.Id, upcomingLate.Id }, board.Upcoming.Select(v => v.AppointmentId));
        Assert.Equal(new[] { waitingFirst.Id, waitingSecond.Id }, board.Waiting.Select(v => v.AppointmentId));
        Assert.Equal(new int?[] { 1, 2 }, board.Waiting.Select(v => v.Position));
        Assert.Equal(seated.Id, Assert.Single(board.InChair).AppointmentId);
        Assert.Equal("Left", Assert.Single(board.Done, v => v.AppointmentId == left.Id).Lane);
        Assert.DoesNotContain(board.Upcoming.Concat(board.Waiting).Concat(board.InChair).Concat(board.Done),
            v => v.AppointmentId == blocked.Id);
        Assert.False(board.CanCallNext);
    }

    [Fact]
    public async Task A_Dentist_Reads_Can_Call_Next_And_His_Own_Id()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);

        var board = (await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None)).Value!;

        Assert.True(board.CanCallNext);
        Assert.Equal(sami.Id, board.CallerDoctorId);
    }
}
