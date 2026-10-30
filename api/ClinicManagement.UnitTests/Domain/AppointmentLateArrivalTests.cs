using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// « Retard » (`features/post-visit-late-and-postpone`): a fact recorded on a visit the patient came to. It never
/// moves the booked slot, and only pushes the moment the séance is considered over.
/// </summary>
public class AppointmentLateArrivalTests
{
    private static readonly DateTime At = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private static Appointment In(AppointmentStatus status)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, At, TimeSpan.FromMinutes(30));
        switch (status)
        {
            case AppointmentStatus.Scheduled:
                break;
            case AppointmentStatus.InProgress:
                appointment.Start();
                break;
            case AppointmentStatus.AwaitingClosure:
                appointment.MarkAwaitingClosure();
                break;
            case AppointmentStatus.Completed:
                appointment.Complete();
                break;
            case AppointmentStatus.Cancelled:
                appointment.Cancel("motif");
                break;
            case AppointmentStatus.NoShow:
                appointment.MarkAsNoShow();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Not arrangeable by a legal move.");
        }

        return appointment;
    }

    public static TheoryData<AppointmentStatus> Admitting => new()
    {
        AppointmentStatus.InProgress, AppointmentStatus.AwaitingClosure, AppointmentStatus.Completed,
    };

    public static TheoryData<AppointmentStatus> Refusing => new()
    {
        AppointmentStatus.Scheduled, AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
    };

    [Theory]
    [MemberData(nameof(Admitting))]
    public void A_Visit_The_Patient_Came_To_Records_A_Retard_And_Keeps_Its_Slot(AppointmentStatus status)
    {
        var appointment = In(status);

        appointment.RecordLateArrival(15);

        Assert.Equal(15, appointment.LateArrivalMinutes);
        Assert.Equal(status, appointment.Status);
        Assert.Equal(At, appointment.AppointmentDateTime);
        Assert.Equal(TimeSpan.FromMinutes(30), appointment.Duration);
    }

    [Theory]
    [MemberData(nameof(Refusing))]
    public void A_Visit_Nobody_Came_To_Refuses_A_Retard_In_French(AppointmentStatus status)
    {
        var appointment = In(status);

        var refusal = Assert.Throws<InvalidOperationException>(() => appointment.RecordLateArrival(15));

        Assert.Contains("retard", refusal.Message);
        Assert.Null(appointment.LateArrivalMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(Appointment.MaxLateArrivalMinutes + 1)]
    public void A_Retard_Outside_The_Range_Is_Refused(int minutes)
    {
        var appointment = In(AppointmentStatus.InProgress);

        Assert.Throws<InvalidOperationException>(() => appointment.RecordLateArrival(minutes));
        Assert.Null(appointment.LateArrivalMinutes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Appointment.MaxLateArrivalMinutes)]
    public void The_Bounds_Themselves_Are_Accepted(int minutes)
    {
        var appointment = In(AppointmentStatus.Completed);

        appointment.RecordLateArrival(minutes);

        Assert.Equal(minutes, appointment.LateArrivalMinutes);
    }

    [Fact]
    public void Null_Clears_It_And_Is_Harmless_On_Any_Status()
    {
        var appointment = In(AppointmentStatus.InProgress);
        appointment.RecordLateArrival(20);

        appointment.RecordLateArrival(null);

        Assert.Null(appointment.LateArrivalMinutes);
        In(AppointmentStatus.Scheduled).RecordLateArrival(null);
    }

    [Fact]
    public void The_Seance_Ends_Later_While_The_Booked_Slot_Stays_As_It_Was()
    {
        var appointment = In(AppointmentStatus.InProgress);

        appointment.RecordLateArrival(15);

        Assert.Equal(At.AddMinutes(45), appointment.ExpectedEndUtc);
        Assert.False(appointment.HasEndedBy(At.AddMinutes(30)));
        Assert.True(appointment.HasEndedBy(At.AddMinutes(45)));
        Assert.Equal(At.AddMinutes(30), At + appointment.Duration);
    }

    [Fact]
    public void With_No_Retard_The_Seance_Ends_At_The_Booked_End()
    {
        var appointment = In(AppointmentStatus.InProgress);

        Assert.Equal(At.AddMinutes(30), appointment.ExpectedEndUtc);
        Assert.True(appointment.HasEndedBy(At.AddMinutes(30)));
    }

    [Fact]
    public void Absent_Erases_The_Retard()
    {
        var appointment = In(AppointmentStatus.AwaitingClosure);
        appointment.RecordLateArrival(10);

        appointment.MarkAsNoShow();

        Assert.Null(appointment.LateArrivalMinutes);
    }

    [Fact]
    public void Cancelling_Erases_The_Retard()
    {
        var appointment = In(AppointmentStatus.Completed);
        appointment.RecordLateArrival(10);

        appointment.Cancel("motif");

        Assert.Null(appointment.LateArrivalMinutes);
    }

    [Fact]
    public void Moving_The_Visit_Erases_The_Retard()
    {
        var appointment = In(AppointmentStatus.InProgress);
        appointment.RecordLateArrival(10);

        appointment.Reschedule(At.AddDays(2), sameClinicDay: false);

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Null(appointment.LateArrivalMinutes);
    }

    [Fact]
    public void A_Move_Within_The_Same_Minute_Is_Not_A_Move()
    {
        var appointment = In(AppointmentStatus.Completed);
        appointment.RecordLateArrival(10);

        appointment.Reschedule(At.AddSeconds(30), sameClinicDay: true);

        Assert.Equal(10, appointment.LateArrivalMinutes);
    }

    [Fact]
    public void Closing_The_Visit_Keeps_The_Retard()
    {
        var appointment = In(AppointmentStatus.InProgress);
        appointment.RecordLateArrival(10);

        appointment.MarkAwaitingClosure();
        appointment.Complete();

        Assert.Equal(10, appointment.LateArrivalMinutes);
    }

    [Fact]
    public void An_Absent_Patient_Who_Turns_Up_Can_Become_Termine()
    {
        Assert.True(Appointment.CanTransition(AppointmentStatus.NoShow, AppointmentStatus.Completed));
        Assert.Contains(AppointmentStatus.Completed, Appointment.ManualNextStatusesFrom(AppointmentStatus.NoShow));

        var appointment = In(AppointmentStatus.NoShow);
        appointment.Complete();
        appointment.RecordLateArrival(40);

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(40, appointment.LateArrivalMinutes);
    }

    [Fact]
    public void A_Fiche_Alone_Still_Does_Not_Overturn_Absent()
    {
        var appointment = In(AppointmentStatus.NoShow);

        Assert.Equal(VisitCompletionOutcome.Contradicted, appointment.MarkVisitCompleted());
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
    }

    /// <summary>The invariant, over every status a retard can be held in × every move the domain offers.</summary>
    [Fact]
    public void No_Move_Leaves_A_Retard_On_A_Status_That_Cannot_Carry_One()
    {
        var moves = new (string Name, Action<Appointment> Apply)[]
        {
            ("Start", a => a.Start()),
            ("MarkAwaitingClosure", a => a.MarkAwaitingClosure()),
            ("Complete", a => a.Complete()),
            ("MarkVisitCompleted", a => a.MarkVisitCompleted()),
            ("Cancel", a => a.Cancel("motif")),
            ("MarkAsNoShow", a => a.MarkAsNoShow()),
            ("Reschedule the same day", a => a.Reschedule(a.AppointmentDateTime.AddHours(2), sameClinicDay: true)),
            ("Reschedule another day", a => a.Reschedule(a.AppointmentDateTime.AddDays(2), sameClinicDay: false)),
        };

        var broken = new List<string>();
        foreach (var from in Appointment.LateArrivalStatuses)
        {
            foreach (var (name, apply) in moves)
            {
                var appointment = In(from);
                appointment.RecordLateArrival(10);
                try
                {
                    apply(appointment);
                }
                catch (InvalidOperationException)
                {
                    continue; // An illegal move changes nothing.
                }

                if (appointment.LateArrivalMinutes is not null
                    && !Appointment.LateArrivalStatuses.Contains(appointment.Status))
                {
                    broken.Add($"{from} → {name} left {appointment.LateArrivalMinutes} min on {appointment.Status}");
                }
            }
        }

        Assert.Empty(broken);
    }

    /// <summary>The browser offers the retard on exactly the statuses the domain accepts it on.</summary>
    [Fact]
    public void The_Browser_Mirror_Holds_The_Same_Statuses_Both_Ways()
    {
        var match = Regex.Match(
            LabelsFile(), @"export const LATE_ARRIVAL_STATUSES = \[(?<list>[^\]]*)\] as const");
        Assert.True(match.Success, "LATE_ARRIVAL_STATUSES not found in web/components/appointment-labels.ts");

        var browser = Regex.Matches(match.Groups["list"].Value, "\"(\\w+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        var server = Appointment.LateArrivalStatuses.Select(s => s.ToString()).ToHashSet();

        Assert.True(
            browser.SetEquals(server),
            $"browser [{string.Join(", ", browser)}] ≠ server [{string.Join(", ", server)}]");
    }

    private static string LabelsFile([CallerFilePath] string thisFile = "")
    {
        var native = "web/components/appointment-labels.ts".Replace('/', Path.DirectorySeparatorChar);
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        // Thrown, never skipped: a mirror test that cannot find one side would report green over nothing.
        throw new FileNotFoundException($"web/components/appointment-labels.ts not found walking up from {thisFile}.");
    }
}
