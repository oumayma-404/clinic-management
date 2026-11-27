using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Features.Patients;
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
/// `salle-attente` round 2 (`features/salle-attente/round-2.md`) — the cases a real clinic day found: one live card
/// per patient (C4), « Quel praticien ? » before seating an unassigned patient (C7), « Annuler » refused once the card
/// moved (C9), « 2 fauteuils » (Q3), « Pas là » (E1), « Passer en premier » (E2), « Changer de praticien » (C10),
/// « Le recevoir aujourd'hui » (C6), the average wait (Q2) and the fiche that must not end a seated visit (C1).
/// Fixtures are anchored on the real clock, like <see cref="WaitingRoomCommandsTests"/>, because the handlers read
/// « today » from it.
/// </summary>
public class WaitingRoomRound2Tests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = DateTime.UtcNow;

    private sealed class Harness
    {
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IDoctorLeaveRepository> Leaves { get; } = new();
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
        public List<Appointment> OtherDays { get; } = new();
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
                .ReturnsAsync((Guid id, CancellationToken _) => Day.Concat(OtherDays).FirstOrDefault(a => a.Id == id));
            Doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Dentists.ToList());
            Doctors.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Dentists.FirstOrDefault(d => d.Id == id));
            Leaves.Setup(r => r.GetOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<DoctorLeave>());
            Mediator.Setup(m => m.Send(It.IsAny<SendArrivalNoticeCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<StaffMessageDto>.Failure("pas de praticien", "no-dentist"));
            SignedInAs(User.RoleSecretary);
        }

        public Appointment Booked(Doctor? dentist = null, int slotMinutesAgo = 10, Guid? patientId = null)
        {
            var appointment = new Appointment(
                Guid.NewGuid(), ClinicId, patientId ?? Guid.NewGuid(), dentist?.Id, Now.AddMinutes(-slotMinutesAgo), TimeSpan.FromMinutes(30));
            Day.Add(appointment);
            return appointment;
        }

        public Appointment At(WaitingRoomLane lane, Doctor? dentist = null, int minutesAgo = 20, Guid? patientId = null)
        {
            var appointment = Booked(dentist, patientId: patientId);
            if (lane == WaitingRoomLane.InChair)
            {
                appointment.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now.AddMinutes(-minutesAgo - 5));
            }

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
            NullLogger<MoveInWaitingRoomCommandHandler>.Instance, GoogleSync.Object);

        public RestoreWaitingRoomCommandHandler Restore() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object, Context.Object);

        public RegisterWalkInCommandHandler WalkIn() => new(
            Appointments.Object, Doctors.Object, Patients.Object, UnitOfWork.Object, ClinicResolver.Object,
            Notifications.Object, GoogleSync.Object, Mediator.Object, NullLogger<RegisterWalkInCommandHandler>.Instance);

        public CallNextPatientCommandHandler CallNext() => new(
            Appointments.Object, Doctors.Object, Users.Object, Patients.Object, Messages.Object, UnitOfWork.Object,
            ClinicResolver.Object, Context.Object, Realtime.Object, Push.Object, NullLogger<CallNextPatientCommandHandler>.Instance);

        public SkipCalledPatientCommandHandler Skip() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object);

        public SetWaitingRoomUrgentCommandHandler Urgent() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object);

        public SetWaitingRoomDentistCommandHandler ChangeDentist() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object);

        public SetDentistChairsCommandHandler Chairs() => new(
            Appointments.Object, Doctors.Object, UnitOfWork.Object, ClinicResolver.Object, Context.Object);

        public GetWaitingRoomQueryHandler Board() => new(
            Appointments.Object, Doctors.Object, ClinicResolver.Object, Context.Object, Leaves.Object);
    }

    private static Doctor Dentist(string name, bool twoChairs = false)
    {
        var dentist = new Doctor(Guid.NewGuid(), ClinicId, name, "Dentiste", "General");
        dentist.SetWorksTwoChairs(twoChairs);
        return dentist;
    }

    private static Task<Result<WaitingRoomChangeDto>> MoveAsync(Harness harness, Appointment visit, string lane, Guid? doctorId = null) =>
        harness.Move().Handle(
            new MoveInWaitingRoomCommand { AppointmentId = visit.Id, Lane = lane, DoctorId = doctorId }, CancellationToken.None);

    // ── C4 — one live card per patient ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Patients_Second_Rdv_Cannot_Arrive_While_He_Waits_For_The_First()
    {
        var harness = new Harness(Dentist("Sami"));
        var patientId = Guid.NewGuid();
        harness.At(WaitingRoomLane.Waiting, patientId: patientId);
        var afternoon = harness.Booked(slotMinutesAgo: -300, patientId: patientId);

        var result = await MoveAsync(harness, afternoon, "Waiting");

        Assert.True(result.IsFailure);
        Assert.Equal(WaitingRoomRefusals.AlreadyHereCode, result.Code);
        Assert.Equal(WaitingRoomLane.Upcoming, afternoon.WaitingRoomLane);
    }

    // ── C7 — « Quel praticien ? » before seating an unassigned patient in a cabinet de groupe ───────────────

    [Fact]
    public async Task A_Group_Practice_Will_Not_Seat_An_Unassigned_Patient_In_Nobodys_Chair()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami, Dentist("Lina"));
        var unassigned = harness.At(WaitingRoomLane.Waiting);
        var otherUnassignedSeated = harness.At(WaitingRoomLane.InChair);

        var refused = await MoveAsync(harness, unassigned, "InChair");

        Assert.Equal(WaitingRoomRefusals.NeedsDentistCode, refused.Code);
        Assert.Equal(WaitingRoomLane.Waiting, unassigned.WaitingRoomLane);
        // Nobody else's visit was ended by a refused seat.
        Assert.Equal(WaitingRoomLane.InChair, otherUnassignedSeated.WaitingRoomLane);

        var seated = await MoveAsync(harness, unassigned, "InChair", sami.Id);

        Assert.True(seated.IsSuccess);
        Assert.Equal(sami.Id, unassigned.DoctorId);
        Assert.True(unassigned.BookedWithOverlap);
        Assert.Equal(WaitingRoomLane.InChair, otherUnassignedSeated.WaitingRoomLane);
    }

    // ── Q3 — « 2 fauteuils » ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task With_Two_Chairs_Seating_A_Second_Patient_Ends_Nobody_And_A_Third_Is_Refused()
    {
        var sami = Dentist("Sami", twoChairs: true);
        var harness = new Harness(sami);
        var first = harness.At(WaitingRoomLane.InChair, sami);
        var second = harness.At(WaitingRoomLane.Waiting, sami);
        var third = harness.At(WaitingRoomLane.Waiting, sami);

        Assert.True((await MoveAsync(harness, second, "InChair")).IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, first.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.InChair, second.WaitingRoomLane);

        var refused = await MoveAsync(harness, third, "InChair");
        Assert.Equal(WaitingRoomRefusals.ChairsFullCode, refused.Code);
        Assert.Equal(WaitingRoomLane.Waiting, third.WaitingRoomLane);
    }

    [Fact]
    public async Task With_Two_Chairs_Patient_Suivant_Calls_The_Next_And_Ends_Nobody()
    {
        var sami = Dentist("Sami", twoChairs: true);
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var anaesthetised = harness.At(WaitingRoomLane.InChair, sami);
        var next = harness.At(WaitingRoomLane.Waiting, sami);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, anaesthetised.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.InChair, next.WaitingRoomLane);
    }

    [Fact]
    public async Task With_Two_Chairs_And_Nobody_Waiting_Patient_Suivant_Ends_Nobody()
    {
        var sami = Dentist("Sami", twoChairs: true);
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var seated = harness.At(WaitingRoomLane.InChair, sami);

        var result = await harness.CallNext().Handle(new CallNextPatientCommand(), CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NobodyCode, result.Code);
        Assert.Equal(WaitingRoomLane.InChair, seated.WaitingRoomLane);
    }

    [Fact]
    public async Task A_Dentist_Sets_His_Own_Chairs_And_A_Secretary_Cannot()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami, Dentist("Lina"));

        var refused = await harness.Chairs().Handle(
            new SetDentistChairsCommand { DoctorId = sami.Id, TwoChairs = true }, CancellationToken.None);
        Assert.Equal(WaitingRoomRefusals.NotYourChairsCode, refused.Code);
        Assert.False(sami.WorksTwoChairs);

        harness.SignedInAs(User.RoleDoctor, sami);
        var set = await harness.Chairs().Handle(new SetDentistChairsCommand { TwoChairs = true }, CancellationToken.None);

        Assert.True(set.IsSuccess);
        Assert.True(sami.WorksTwoChairs);
        harness.Doctors.Verify(r => r.Update(sami), Times.Once);
    }

    // ── C9 — « Annuler » never overwrites a later move ───────────────────────────────────────────────────────

    [Fact]
    public async Task Annuler_Is_Refused_Once_The_Card_Has_Moved_Since()
    {
        var harness = new Harness(Dentist("Sami"));
        var visit = harness.Booked();
        var arrived = (await MoveAsync(harness, visit, "Waiting")).Value!;

        // A colleague brings him in before the undo.
        await MoveAsync(harness, visit, "InChair");

        var result = await harness.Restore().Handle(new RestoreWaitingRoomCommand
        {
            Before = arrived.Before,
            After = arrived.After,
            RemoveWalkInIds = arrived.RemoveWalkInIds,
        }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.MovedSinceCode, result.Code);
        Assert.Equal(WaitingRoomLane.InChair, visit.WaitingRoomLane);
    }

    [Fact]
    public async Task Annuler_Applies_When_Nothing_Moved_Since()
    {
        var harness = new Harness(Dentist("Sami"));
        var visit = harness.Booked();
        var arrived = (await MoveAsync(harness, visit, "Waiting")).Value!;
        Assert.Single(arrived.After);

        var result = await harness.Restore().Handle(new RestoreWaitingRoomCommand
        {
            Before = arrived.Before,
            After = arrived.After,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.Upcoming, visit.WaitingRoomLane);
    }

    // Q1 — the undo of « Terminer » gives the RDV back its booked time as well as its lane.
    [Fact]
    public async Task Annuler_Of_Terminer_Puts_The_Booked_Time_Back()
    {
        var harness = new Harness(Dentist("Sami"));
        var visit = harness.At(WaitingRoomLane.InChair, minutesAgo: 6);
        var booked = (visit.AppointmentDateTime, visit.Duration);

        var ended = (await MoveAsync(harness, visit, "Done")).Value!;
        Assert.NotEqual(booked.AppointmentDateTime, visit.AppointmentDateTime);
        Assert.Equal(booked.AppointmentDateTime, visit.PlannedStartUtc);
        harness.GoogleSync.Verify(g => g.Dispatch(visit.Id, ClinicId), Times.Once);

        var undone = await harness.Restore().Handle(new RestoreWaitingRoomCommand
        {
            Before = ended.Before,
            After = ended.After,
        }, CancellationToken.None);

        Assert.True(undone.IsSuccess);
        Assert.Equal(booked, (visit.AppointmentDateTime, visit.Duration));
        Assert.Null(visit.PlannedStartUtc);
        Assert.Equal(WaitingRoomLane.InChair, visit.WaitingRoomLane);
    }

    // ── E1 — « Pas là » ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pas_La_Sends_The_Called_Patient_Back_And_Calls_The_Next_Of_That_Chair()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        var absent = harness.At(WaitingRoomLane.InChair, sami, minutesAgo: 2);
        var next = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 10);

        var result = await harness.Skip().Handle(
            new SkipCalledPatientCommand { AppointmentId = absent.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.Waiting, absent.WaitingRoomLane);
        Assert.NotNull(absent.CallSkippedAtUtc);
        Assert.Equal(WaitingRoomLane.InChair, next.WaitingRoomLane);
        Assert.Equal(next.Id, result.Value!.AppointmentId);
        Assert.Equal(2, result.Value.Before.Count);
    }

    [Fact]
    public async Task Pas_La_For_A_Patient_No_Longer_In_The_Chair_Is_A_Stale_Screen()
    {
        var harness = new Harness(Dentist("Sami"));
        var waiting = harness.At(WaitingRoomLane.Waiting);

        var result = await harness.Skip().Handle(
            new SkipCalledPatientCommand { AppointmentId = waiting.Id }, CancellationToken.None);

        Assert.Equal(WaitingRoomRefusals.NotInChairCode, result.Code);
        Assert.True(WaitingRoomRefusals.IsConflict(result.Code));
    }

    // ── E2 — « Passer en premier » ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Passer_En_Premier_Puts_Him_First_In_The_Dentists_Queue()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        harness.SignedInAs(User.RoleDoctor, sami);
        var first = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 30);
        var toothache = harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 1);

        var result = await harness.Urgent().Handle(
            new SetWaitingRoomUrgentCommand { AppointmentId = toothache.Id }, CancellationToken.None);
        var board = await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None);

        Assert.True(result.Value!.Changed);
        Assert.Equal(new[] { toothache.Id, first.Id }, board.Value!.Waiting.Select(v => v.AppointmentId));
        Assert.NotNull(board.Value.Waiting[0].UrgentAtUtc);
    }

    // ── C10 — « Changer de praticien » ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Dentist_On_Conge_Is_Named_On_His_Cards_And_His_Patient_Can_Change_Dentist()
    {
        var sami = Dentist("Sami");
        var lina = Dentist("Lina");
        var harness = new Harness(sami, lina);
        var today = DateOnly.FromDateTime(ClinicManagement.Application.Common.ClinicClock.ClinicToday());
        harness.Leaves.Setup(r => r.GetOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new DoctorLeave(Guid.NewGuid(), ClinicId, sami.Id, today, today, null) });
        var visit = harness.At(WaitingRoomLane.Waiting, sami);

        var board = await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None);
        Assert.Equal("OnLeave", Assert.Single(board.Value!.Waiting).DoctorUnavailable);
        Assert.True(board.Value.Dentists.Single(d => d.DoctorId == sami.Id).AbsentToday);

        var changed = await harness.ChangeDentist().Handle(
            new SetWaitingRoomDentistCommand { AppointmentId = visit.Id, DoctorId = lina.Id }, CancellationToken.None);

        Assert.True(changed.IsSuccess);
        Assert.Equal(lina.Id, visit.DoctorId);
        Assert.Equal(sami.Id, Assert.Single(changed.Value!.Before).DoctorId);
    }

    [Fact]
    public async Task A_Dentist_Gone_From_The_Team_Still_Names_His_Cards()
    {
        var retired = Dentist("Karim");
        retired.Retire();
        var harness = new Harness(retired, Dentist("Lina"));
        harness.At(WaitingRoomLane.Waiting, retired);

        var board = await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None);
        var card = Assert.Single(board.Value!.Waiting);

        Assert.Equal("NotInTeam", card.DoctorUnavailable);
        Assert.Equal(retired.FullName, card.DoctorName);
    }

    // ── Q2 — « Attente moyenne » ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Average_Wait_Is_Arrival_To_Call_Over_The_Patients_Called_In()
    {
        var sami = Dentist("Sami");
        var harness = new Harness(sami);
        var a = harness.Booked(sami);
        a.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now.AddMinutes(-40));
        a.MoveInWaitingRoom(WaitingRoomLane.InChair, Now.AddMinutes(-28));   // waited 12
        a.MoveInWaitingRoom(WaitingRoomLane.Done, Now.AddMinutes(-10));
        var b = harness.Booked(sami);
        b.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now.AddMinutes(-9));
        b.MoveInWaitingRoom(WaitingRoomLane.InChair, Now.AddMinutes(-5));    // waited 4
        harness.At(WaitingRoomLane.Waiting, sami, minutesAgo: 60);           // still waiting: not counted
        harness.Booked(sami).MoveInWaitingRoom(WaitingRoomLane.Done, Now);   // dragged to « Terminé »: no times

        var board = await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None);

        Assert.Equal(8, board.Value!.AverageWaitMinutes);
    }

    [Fact]
    public async Task Nobody_Called_In_Yet_Has_No_Average()
    {
        var harness = new Harness(Dentist("Sami"));
        harness.At(WaitingRoomLane.Waiting, minutesAgo: 30);

        var board = await harness.Board().Handle(new GetWaitingRoomQuery(), CancellationToken.None);

        Assert.Null(board.Value!.AverageWaitMinutes);
    }

    // ── C6 — « Le recevoir aujourd'hui » ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Patient_Who_Comes_Today_For_Thursdays_Rdv_Is_Received_On_It()
    {
        var harness = new Harness(Dentist("Sami"));
        var patient = new Patient(Guid.NewGuid(), ClinicId, "Amira", "Ben Salah", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "F");
        harness.Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var thursday = new Appointment(Guid.NewGuid(), ClinicId, patient.Id, null, Now.AddDays(3), TimeSpan.FromMinutes(30));
        harness.OtherDays.Add(thursday);

        var result = await harness.WalkIn().Handle(
            new RegisterWalkInCommand { PatientId = patient.Id, AppointmentId = thursday.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.Waiting, thursday.WaitingRoomLane);
        Assert.True(thursday.AppointmentDateTime <= Now.AddMinutes(1));
        Assert.Empty(result.Value!.RemoveWalkInIds);
        harness.Appointments.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.GoogleSync.Verify(g => g.Dispatch(thursday.Id, ClinicId), Times.Once);
    }

    // ── C1 — a fiche never ends a seated visit by the clock ──────────────────────────────────────────────────

    [Fact]
    public void A_Fiche_Saved_After_The_Slot_Does_Not_End_A_Patient_Still_In_The_Chair()
    {
        var late = new Appointment(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now.AddMinutes(-60), TimeSpan.FromMinutes(30));
        late.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now.AddMinutes(-55));
        late.MoveInWaitingRoom(WaitingRoomLane.InChair, Now.AddMinutes(-20));

        Assert.False(FicheVisitCompletion.ShouldComplete(late, finishSeance: false, Now));
        Assert.True(FicheVisitCompletion.ShouldComplete(late, finishSeance: true, Now));

        var notOnTheBoard = new Appointment(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now.AddMinutes(-60), TimeSpan.FromMinutes(30));
        Assert.True(FicheVisitCompletion.ShouldComplete(notOnTheBoard, finishSeance: false, Now));
    }
}
