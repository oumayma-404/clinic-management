using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// `salle-attente` — the appointment's place on the board (À venir → En salle d'attente → Au fauteuil → Terminé),
/// derived from the status and three presence times, and the moves between lanes. The owner's rule is « every
/// action reversible », so the headline test walks every lane pair forward and back.
/// </summary>
public class AppointmentWaitingRoomTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Slot = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 25, 9, 20, 0, DateTimeKind.Utc);

    private static Appointment Booked(DateTime? slot = null) =>
        new(Guid.NewGuid(), ClinicId, Guid.NewGuid(), Guid.NewGuid(), slot ?? Slot, TimeSpan.FromMinutes(30));

    private static readonly WaitingRoomLane[] BoardLanes =
    {
        WaitingRoomLane.Upcoming, WaitingRoomLane.Waiting, WaitingRoomLane.InChair, WaitingRoomLane.Done, WaitingRoomLane.Left,
    };

    private static Appointment At(WaitingRoomLane lane)
    {
        var appointment = Booked();
        if (lane != WaitingRoomLane.Upcoming)
        {
            appointment.MoveInWaitingRoom(lane, Now.AddMinutes(-15));
        }

        Assert.Equal(lane, appointment.WaitingRoomLane);
        return appointment;
    }

    [Fact]
    public void A_Booked_Visit_Nobody_Marked_Is_Upcoming_And_Carries_No_Times()
    {
        var appointment = Booked();

        Assert.Equal(WaitingRoomLane.Upcoming, appointment.WaitingRoomLane);
        Assert.Null(appointment.ArrivedAtUtc);
        Assert.Null(appointment.CalledInAtUtc);
        Assert.Null(appointment.LeftChairAtUtc);
        Assert.False(appointment.IsWalkIn);
    }

    [Fact]
    public void A_Slot_With_No_Patient_Is_Never_On_The_Board_And_Cannot_Be_Moved()
    {
        var blocked = new Appointment(Guid.NewGuid(), ClinicId, null, null, Slot, TimeSpan.FromMinutes(30));

        Assert.Equal(WaitingRoomLane.None, blocked.WaitingRoomLane);
        Assert.Throws<InvalidOperationException>(() => blocked.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now));
    }

    [Fact]
    public void Cancelled_From_The_Agenda_And_Absent_Visits_Are_Not_On_The_Board()
    {
        var cancelled = Booked();
        cancelled.Cancel("Le patient a appelé");
        var absent = Booked();
        absent.MarkAsNoShow();

        Assert.Equal(WaitingRoomLane.None, cancelled.WaitingRoomLane);
        Assert.Equal(WaitingRoomLane.None, absent.WaitingRoomLane);
    }

    [Fact]
    public void A_Visit_Completed_By_Its_Fiche_Shows_As_Done_With_No_Board_Times()
    {
        var appointment = Booked();
        appointment.MarkVisitCompleted();

        Assert.Equal(WaitingRoomLane.Done, appointment.WaitingRoomLane);
    }

    [Fact]
    public void Arriving_Records_The_Time_And_Undoes_The_Clocks_Guess()
    {
        var appointment = Booked();
        appointment.Start();
        appointment.RecordLateArrival(10);

        appointment.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now);

        Assert.Equal(WaitingRoomLane.Waiting, appointment.WaitingRoomLane);
        Assert.Equal(Now, appointment.ArrivedAtUtc);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Null(appointment.LateArrivalMinutes);
    }

    [Fact]
    public void Calling_In_Starts_The_Visit_And_Ending_The_Chair_Completes_It()
    {
        var appointment = At(WaitingRoomLane.Waiting);

        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Now);
        Assert.Equal(AppointmentStatus.InProgress, appointment.Status);
        Assert.Equal(Now, appointment.CalledInAtUtc);

        appointment.MoveInWaitingRoom(WaitingRoomLane.Done, Now.AddMinutes(20));
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(Now.AddMinutes(20), appointment.LeftChairAtUtc);
    }

    [Fact]
    public void Leaving_Without_Being_Seen_Cancels_With_Its_Motif_And_Keeps_The_Arrival()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        var arrived = appointment.ArrivedAtUtc;

        appointment.MoveInWaitingRoom(WaitingRoomLane.Left, Now);

        Assert.Equal(WaitingRoomLane.Left, appointment.WaitingRoomLane);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(Appointment.LeftWithoutBeingSeenReason, appointment.CancellationReason);
        Assert.Equal(arrived, appointment.ArrivedAtUtc);
    }

    [Fact]
    public void Reopening_A_Visit_Ended_By_Mistake_Keeps_The_Time_It_Was_Called()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Now);
        appointment.MoveInWaitingRoom(WaitingRoomLane.Done, Now.AddMinutes(5));

        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Now.AddMinutes(6));

        Assert.Equal(WaitingRoomLane.InChair, appointment.WaitingRoomLane);
        Assert.Equal(Now, appointment.CalledInAtUtc);
        Assert.Null(appointment.LeftChairAtUtc);
        Assert.Equal(AppointmentStatus.InProgress, appointment.Status);
    }

    [Fact]
    public void Moving_To_The_Lane_It_Is_Already_In_Changes_Nothing()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        var arrived = appointment.ArrivedAtUtc;

        Assert.False(appointment.MoveInWaitingRoom(WaitingRoomLane.Waiting, Now.AddHours(1)));
        Assert.Equal(arrived, appointment.ArrivedAtUtc);
    }

    // The owner's rule, 2026-11-25: « all actions reversible ». Every lane reaches every other one, and the way back
    // — a restore of the snapshot the move started from — returns status and times exactly.
    [Fact]
    public void Every_Board_Move_Is_Undone_Exactly_By_Restoring_Its_Snapshot()
    {
        foreach (var from in BoardLanes)
        {
            foreach (var to in BoardLanes.Where(l => l != from))
            {
                var appointment = At(from);
                var before = appointment.CaptureWaitingRoomState();
                var status = (appointment.Status, appointment.CancellationReason);

                Assert.True(appointment.MoveInWaitingRoom(to, Now), $"{from} → {to}");
                Assert.Equal(to, appointment.WaitingRoomLane);

                appointment.RestoreWaitingRoom(before, Now);

                Assert.True(before.SameAs(appointment.CaptureWaitingRoomState()), $"{from} → {to} → {from}");
                Assert.Equal(before, appointment.CaptureWaitingRoomState());
                Assert.Equal(status, (appointment.Status, appointment.CancellationReason));
            }
        }
    }

    // …and by hand, later: every lane pair is also a direct move, so a card is never stranded.
    [Fact]
    public void Every_Lane_Reaches_Every_Other_Lane_For_A_Booked_Visit()
    {
        foreach (var from in BoardLanes)
        {
            foreach (var to in BoardLanes.Where(l => l != from))
            {
                var appointment = At(from);
                appointment.MoveInWaitingRoom(to, Now);
                Assert.Equal(to, appointment.WaitingRoomLane);
            }
        }
    }

    [Fact]
    public void A_Walk_In_Is_Booked_At_His_Arrival_Already_Waiting_And_Exempt_From_The_Overlap_Constraint()
    {
        var walkIn = Appointment.WalkIn(Guid.NewGuid(), ClinicId, Guid.NewGuid(), Guid.NewGuid(), Now.AddSeconds(42), TimeSpan.FromMinutes(30));

        Assert.True(walkIn.IsWalkIn);
        Assert.Equal(WaitingRoomLane.Waiting, walkIn.WaitingRoomLane);
        Assert.Equal(Now, walkIn.AppointmentDateTime);
        Assert.True(walkIn.BookedWithOverlap);
    }

    [Fact]
    public void A_Walk_In_Has_No_Slot_To_Go_Back_To()
    {
        var walkIn = Appointment.WalkIn(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, Now, TimeSpan.FromMinutes(30));

        var refusal = Assert.Throws<InvalidOperationException>(() => walkIn.MoveInWaitingRoom(WaitingRoomLane.Upcoming, Now));
        Assert.Equal(Appointment.WalkInHasNoSlotMessage, refusal.Message);
        Assert.Throws<InvalidOperationException>(() =>
            walkIn.RestoreWaitingRoom(walkIn.CaptureWaitingRoomState() with { Lane = WaitingRoomLane.Upcoming, ArrivedAtUtc = null }, Now));
    }

    [Theory]
    [InlineData(WaitingRoomLane.Waiting, false, false, false, false)]
    [InlineData(WaitingRoomLane.InChair, true, false, false, false)]
    [InlineData(WaitingRoomLane.Upcoming, true, false, false, false)]
    [InlineData(WaitingRoomLane.Left, true, true, false, true)]
    [InlineData(WaitingRoomLane.Left, true, false, false, false)]
    [InlineData(WaitingRoomLane.Done, true, true, true, true)]
    public void An_Incoherent_Snapshot_Is_Refused(WaitingRoomLane lane, bool arrived, bool called, bool left, bool leftUnseen)
    {
        var appointment = At(WaitingRoomLane.Waiting);
        var state = appointment.CaptureWaitingRoomState() with
        {
            Lane = lane,
            ArrivedAtUtc = arrived ? Now.AddMinutes(-5) : null,
            CalledInAtUtc = called ? Now.AddMinutes(-4) : null,
            LeftChairAtUtc = left ? Now.AddMinutes(-3) : null,
            LeftUnseenAtUtc = leftUnseen ? Now.AddMinutes(-2) : null,
        };

        Assert.Throws<InvalidOperationException>(() => appointment.RestoreWaitingRoom(state, Now));
    }

    [Fact]
    public void A_Snapshot_Whose_Times_Run_Backwards_Is_Refused()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        var state = appointment.CaptureWaitingRoomState() with
        {
            Lane = WaitingRoomLane.InChair,
            ArrivedAtUtc = Now,
            CalledInAtUtc = Now.AddMinutes(-10),
        };

        Assert.Throws<InvalidOperationException>(() => appointment.RestoreWaitingRoom(state, Now.AddMinutes(1)));
    }

    [Fact]
    public void The_Fiche_And_A_Payment_End_The_Chair_Only_Of_A_Visit_In_It()
    {
        var seated = At(WaitingRoomLane.InChair);
        var waiting = At(WaitingRoomLane.Waiting);

        Assert.True(seated.StampLeftChairIfOpen(Now));
        Assert.Equal(Now, seated.LeftChairAtUtc);
        Assert.False(waiting.StampLeftChairIfOpen(Now));
        Assert.Null(waiting.LeftChairAtUtc);

        var paid = At(WaitingRoomLane.InChair);
        Assert.True(paid.EndChairIfOpen(Now));
        Assert.Equal(WaitingRoomLane.Done, paid.WaitingRoomLane);
        Assert.Equal(AppointmentStatus.Completed, paid.Status);
        Assert.False(waiting.EndChairIfOpen(Now));
        Assert.Equal(WaitingRoomLane.Waiting, waiting.WaitingRoomLane);
    }

    [Fact]
    public void A_Retired_Visit_Leaves_The_Board()
    {
        var appointment = At(WaitingRoomLane.Waiting);

        appointment.Disregard("local|x", Now);

        Assert.Equal(WaitingRoomLane.None, appointment.WaitingRoomLane);
    }

    // ---------------------------------------------------------------------------------------------- round 2

    // C8 — a move never invents a time it does not know.
    [Fact]
    public void Faire_Entrer_Straight_From_A_Venir_Records_The_Call_And_No_Arrival()
    {
        var appointment = Booked();

        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Now);

        Assert.Equal(WaitingRoomLane.InChair, appointment.WaitingRoomLane);
        Assert.Equal(Now, appointment.CalledInAtUtc);
        Assert.Null(appointment.ArrivedAtUtc);
    }

    [Theory]
    [InlineData(WaitingRoomLane.Upcoming)]
    [InlineData(WaitingRoomLane.Waiting)]
    public void A_Card_Dragged_To_Termine_From_Outside_The_Chair_Keeps_Its_Unknowns_And_Its_Booking(WaitingRoomLane from)
    {
        var appointment = At(from);
        var arrived = appointment.ArrivedAtUtc;

        appointment.MoveInWaitingRoom(WaitingRoomLane.Done, Now);

        Assert.Equal(WaitingRoomLane.Done, appointment.WaitingRoomLane);
        Assert.Equal(arrived, appointment.ArrivedAtUtc);
        Assert.Null(appointment.CalledInAtUtc);
        Assert.Null(appointment.LeftChairAtUtc);
        Assert.Equal(Slot, appointment.AppointmentDateTime);
        Assert.Null(appointment.PlannedStartUtc);
    }

    // Q1 — the RDV takes the real chair time, keeps its booking, and gives it back when the visit re-opens.
    [Fact]
    public void A_Visit_Ended_From_The_Chair_Takes_The_Real_Chair_Time_And_Keeps_Its_Booking()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Slot.AddMinutes(7).AddSeconds(40));

        appointment.MoveInWaitingRoom(WaitingRoomLane.Done, Slot.AddMinutes(21).AddSeconds(10));

        Assert.Equal(Slot.AddMinutes(7), appointment.AppointmentDateTime);
        Assert.Equal(TimeSpan.FromMinutes(15), appointment.Duration);
        Assert.Equal(Slot, appointment.PlannedStartUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), appointment.PlannedDuration);
        // A late séance overlaps the next booked RDV: the double-booking constraint must not refuse « Patient suivant ».
        Assert.True(appointment.BookedWithOverlap);
    }

    [Fact]
    public void Reopening_A_Visit_Puts_Its_Booking_Back()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Slot.AddMinutes(5));
        appointment.MoveInWaitingRoom(WaitingRoomLane.Done, Slot.AddMinutes(12));

        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Slot.AddMinutes(13));

        Assert.Equal(Slot, appointment.AppointmentDateTime);
        Assert.Equal(TimeSpan.FromMinutes(30), appointment.Duration);
        Assert.Null(appointment.PlannedStartUtc);
        Assert.Null(appointment.PlannedDuration);
    }

    [Fact]
    public void A_Fiche_Or_The_Rdv_Window_Ending_The_Chair_Also_Takes_The_Real_Time()
    {
        var byFiche = At(WaitingRoomLane.Waiting);
        byFiche.MoveInWaitingRoom(WaitingRoomLane.InChair, Slot.AddMinutes(2));
        byFiche.StampLeftChairIfOpen(Slot.AddMinutes(20));
        byFiche.MarkVisitCompleted();

        var byWindow = At(WaitingRoomLane.Waiting);
        byWindow.MoveInWaitingRoom(WaitingRoomLane.InChair, Slot.AddMinutes(2));
        byWindow.StampLeftChairIfOpen(Slot.AddMinutes(20));
        byWindow.Complete();

        foreach (var appointment in new[] { byFiche, byWindow })
        {
            Assert.Equal(Slot.AddMinutes(2), appointment.AppointmentDateTime);
            Assert.Equal(TimeSpan.FromMinutes(18), appointment.Duration);
            Assert.Equal(Slot, appointment.PlannedStartUtc);
        }
    }

    [Fact]
    public void A_Visit_Completed_By_Its_Fiche_Alone_Keeps_Its_Booked_Time()
    {
        var appointment = Booked();

        appointment.MarkVisitCompleted();

        Assert.Equal(Slot, appointment.AppointmentDateTime);
        Assert.Null(appointment.PlannedStartUtc);
    }

    [Fact]
    public void Moving_A_Real_Time_Visit_On_The_Agenda_Makes_The_New_Time_The_Booking()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Slot.AddMinutes(2));
        appointment.MoveInWaitingRoom(WaitingRoomLane.Done, Slot.AddMinutes(20));

        appointment.Reschedule(Slot.AddMinutes(30), sameClinicDay: true);

        Assert.Null(appointment.PlannedStartUtc);
        Assert.Equal(Slot.AddMinutes(30), appointment.AppointmentDateTime);
    }

    // C3 — only the board's « Parti sans être vu » (or « Absent » on a waiting patient) puts a card under « Parti ».
    [Theory]
    [InlineData(WaitingRoomLane.Waiting)]
    [InlineData(WaitingRoomLane.InChair)]
    [InlineData(WaitingRoomLane.Done)]
    public void A_Cancellation_Made_From_The_Agenda_Takes_The_Card_Off_The_Board(WaitingRoomLane from)
    {
        var appointment = At(from);

        appointment.Cancel("Erreur de saisie");

        Assert.Equal(WaitingRoomLane.None, appointment.WaitingRoomLane);
        Assert.Null(appointment.LeftUnseenAtUtc);
    }

    [Fact]
    public void Absent_For_A_Patient_Who_Was_Waiting_Reads_Parti_Sans_Etre_Vu()
    {
        var appointment = At(WaitingRoomLane.Waiting);

        appointment.MarkAsNoShow();

        Assert.Equal(WaitingRoomLane.Left, appointment.WaitingRoomLane);
        Assert.NotNull(appointment.LeftUnseenAtUtc);
    }

    [Fact]
    public void The_Boards_Parti_Sans_Etre_Vu_Records_When_He_Left()
    {
        var appointment = At(WaitingRoomLane.Waiting);

        appointment.MoveInWaitingRoom(WaitingRoomLane.Left, Now);

        Assert.Equal(Now, appointment.LeftUnseenAtUtc);
        Assert.Equal(Now, appointment.CancelledAt);
    }

    // C2 — another day is another visit, as far as the salle d'attente knows.
    [Fact]
    public void Moving_A_Waiting_Patient_To_Another_Day_Clears_His_Presence()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        appointment.SetUrgent(true, Now);

        appointment.Reschedule(Slot.AddDays(1), sameClinicDay: false);

        Assert.Equal(WaitingRoomLane.Upcoming, appointment.WaitingRoomLane);
        Assert.Null(appointment.ArrivedAtUtc);
        Assert.Null(appointment.UrgentAtUtc);
    }

    [Fact]
    public void Moving_A_Waiting_Patient_Within_The_Day_Keeps_His_Presence()
    {
        var appointment = At(WaitingRoomLane.Waiting);

        appointment.Reschedule(Slot.AddHours(1), sameClinicDay: true);

        Assert.Equal(WaitingRoomLane.Waiting, appointment.WaitingRoomLane);
    }

    // E1 — « Pas là ».
    [Fact]
    public void Pas_La_Sends_Him_Back_To_Wait_With_His_Arrival_And_A_Tag()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        var arrived = appointment.ArrivedAtUtc;
        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Now);

        appointment.SkipCall(Now.AddMinutes(1));

        Assert.Equal(WaitingRoomLane.Waiting, appointment.WaitingRoomLane);
        Assert.Equal(arrived, appointment.ArrivedAtUtc);
        Assert.Equal(Now.AddMinutes(1), appointment.CallSkippedAtUtc);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);

        appointment.MoveInWaitingRoom(WaitingRoomLane.InChair, Now.AddMinutes(5));
        Assert.Null(appointment.CallSkippedAtUtc);
    }

    [Fact]
    public void Pas_La_Is_Refused_For_A_Patient_Not_In_The_Chair()
    {
        Assert.Throws<InvalidOperationException>(() => At(WaitingRoomLane.Waiting).SkipCall(Now));
    }

    // C6 — « Le recevoir aujourd'hui ».
    [Fact]
    public void Receiving_Today_A_Rdv_From_Another_Day_Moves_It_To_Now_Arrived()
    {
        var thursday = Booked(Slot.AddDays(2));

        thursday.ReceiveToday(Now.AddSeconds(30));

        Assert.Equal(Now, thursday.AppointmentDateTime);
        Assert.Equal(WaitingRoomLane.Waiting, thursday.WaitingRoomLane);
        Assert.True(thursday.BookedWithOverlap);
        Assert.False(thursday.IsWalkIn);
    }

    // C9 — the undo compares states, to the millisecond and never on the status.
    [Fact]
    public void A_State_Read_Back_From_The_Database_Is_The_Same_State()
    {
        var appointment = At(WaitingRoomLane.Waiting);
        var state = appointment.CaptureWaitingRoomState();
        var truncated = state with
        {
            ArrivedAtUtc = state.ArrivedAtUtc!.Value.AddTicks(-(state.ArrivedAtUtc!.Value.Ticks % 10)).AddTicks(3),
        };

        Assert.True(state.SameAs(truncated));
        Assert.False(state.SameAs(state with { Lane = WaitingRoomLane.InChair }));
        Assert.False(state.SameAs(state with { UrgentAtUtc = Now }));
    }
}

/// <summary>D3 — « nobody goes before the time of his RDV, and a late patient loses his place ».</summary>
public class WaitingRoomOrderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static DateTime T(int hour, int minute) => new(2026, 11, 25, hour, minute, 0, DateTimeKind.Utc);

    private static Appointment Booked(DateTime slot, DateTime arrived)
    {
        var appointment = new Appointment(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, slot, TimeSpan.FromMinutes(30));
        appointment.MoveInWaitingRoom(WaitingRoomLane.Waiting, arrived);
        return appointment;
    }

    private static Appointment WalkIn(DateTime arrived) =>
        Appointment.WalkIn(Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, arrived, TimeSpan.FromMinutes(30));

    [Fact]
    public void An_Early_Patient_Waits_For_His_Rdv_While_A_Walk_In_Who_Came_After_Him_Goes_First()
    {
        var early = Booked(slot: T(11, 0), arrived: T(10, 30));
        var walkIn = WalkIn(T(10, 40));

        Assert.Equal(new[] { walkIn, early }, WaitingRoomOrder.Order(new[] { early, walkIn }));
    }

    [Fact]
    public void A_Late_Patient_Loses_His_Place_To_Whoever_Was_Ready_Before_He_Came()
    {
        var late = Booked(slot: T(10, 0), arrived: T(10, 40));
        var walkIn = WalkIn(T(10, 20));
        var onTime = Booked(slot: T(10, 30), arrived: T(10, 25));

        Assert.Equal(new[] { walkIn, onTime, late }, WaitingRoomOrder.Order(new[] { late, walkIn, onTime }));
    }

    [Fact]
    public void Two_Patients_Ready_At_The_Same_Minute_Go_In_Order_Of_Arrival()
    {
        var first = Booked(slot: T(10, 0), arrived: T(9, 40));
        var second = Booked(slot: T(10, 0), arrived: T(9, 50));

        Assert.Equal(new[] { first, second }, WaitingRoomOrder.Order(new[] { second, first }));
    }

    // Round 2, E2 — « Passer en premier » goes before everyone, the earliest-marked first.
    [Fact]
    public void An_Urgent_Patient_Goes_Before_Everyone_Who_Was_Ready_Earlier()
    {
        var onTime = Booked(slot: T(10, 0), arrived: T(9, 50));
        var toothache = WalkIn(T(10, 20));
        var secondUrgent = WalkIn(T(10, 25));
        toothache.SetUrgent(true, T(10, 30));
        secondUrgent.SetUrgent(true, T(10, 31));

        Assert.Equal(
            new[] { toothache, secondUrgent, onTime },
            WaitingRoomOrder.Order(new[] { onTime, secondUrgent, toothache }));

        toothache.SetUrgent(false, T(10, 32));
        Assert.Equal(new[] { secondUrgent, onTime, toothache }, WaitingRoomOrder.Order(new[] { onTime, secondUrgent, toothache }));
    }
}
