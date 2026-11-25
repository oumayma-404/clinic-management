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
                var before = (appointment.WaitingRoomLane, appointment.Status, appointment.ArrivedAtUtc,
                    appointment.CalledInAtUtc, appointment.LeftChairAtUtc, appointment.CancellationReason);

                Assert.True(appointment.MoveInWaitingRoom(to, Now), $"{from} → {to}");
                Assert.Equal(to, appointment.WaitingRoomLane);

                appointment.RestoreWaitingRoom(before.Item1, before.Item3, before.Item4, before.Item5, Now);

                Assert.Equal(before,
                    (appointment.WaitingRoomLane, appointment.Status, appointment.ArrivedAtUtc,
                        appointment.CalledInAtUtc, appointment.LeftChairAtUtc, appointment.CancellationReason));
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
        Assert.Throws<InvalidOperationException>(() => walkIn.RestoreWaitingRoom(WaitingRoomLane.Upcoming, null, null, null, Now));
    }

    [Theory]
    [InlineData(WaitingRoomLane.Waiting, false, false, false)]
    [InlineData(WaitingRoomLane.InChair, true, false, false)]
    [InlineData(WaitingRoomLane.Upcoming, true, false, false)]
    [InlineData(WaitingRoomLane.Left, true, true, false)]
    public void An_Incoherent_Snapshot_Is_Refused(WaitingRoomLane lane, bool arrived, bool called, bool left)
    {
        var appointment = At(WaitingRoomLane.Waiting);

        Assert.Throws<InvalidOperationException>(() => appointment.RestoreWaitingRoom(
            lane,
            arrived ? Now.AddMinutes(-5) : null,
            called ? Now.AddMinutes(-4) : null,
            left ? Now.AddMinutes(-3) : null,
            Now));
    }

    [Fact]
    public void A_Snapshot_Whose_Times_Run_Backwards_Is_Refused()
    {
        var appointment = At(WaitingRoomLane.Waiting);

        Assert.Throws<InvalidOperationException>(() =>
            appointment.RestoreWaitingRoom(WaitingRoomLane.InChair, Now, Now.AddMinutes(-10), null, Now.AddMinutes(1)));
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
}
