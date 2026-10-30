using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « Reporter » lists free starts (<c>features/post-visit-late-and-postpone/blueprint-free-slots.md</c>). The rules are
/// the booking guard's, reused — so the test that matters most is the agreement one: listed ⇔ accepted by the save.
/// </summary>
public class FreeSlotFinderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Salma = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Nadia = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Tuesday = new(2026, 11, 3);
    private static readonly DateOnly Sunday = new(2026, 11, 1);
    private static readonly DateTime LongAgo = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Mon–Sat 09:00–17:00 with a 12:30–13:30 break; Sunday closed.</summary>
    private const string ClinicHours = """
        [{"Day":"Monday","Enabled":true,"From":"09:00","To":"17:00","BreakFrom":"12:30","BreakTo":"13:30"},
         {"Day":"Tuesday","Enabled":true,"From":"09:00","To":"17:00","BreakFrom":"12:30","BreakTo":"13:30"},
         {"Day":"Wednesday","Enabled":true,"From":"09:00","To":"17:00","BreakFrom":"12:30","BreakTo":"13:30"},
         {"Day":"Thursday","Enabled":true,"From":"09:00","To":"17:00","BreakFrom":"12:30","BreakTo":"13:30"},
         {"Day":"Friday","Enabled":true,"From":"09:00","To":"17:00","BreakFrom":"12:30","BreakTo":"13:30"},
         {"Day":"Saturday","Enabled":true,"From":"09:00","To":"17:00","BreakFrom":"12:30","BreakTo":"13:30"},
         {"Day":"Sunday","Enabled":false,"From":"09:00","To":"17:00"}]
        """;

    /// <summary>Salma's own hours: Monday 10:00–12:00 only.</summary>
    private const string SalmaHours = """
        [{"Day":"Monday","Enabled":true,"From":"10:00","To":"12:00"}]
        """;

    /// <summary>Tunis is UTC+1 all year.</summary>
    private static DateTime At(DateOnly day, string hhmm) =>
        day.ToDateTime(TimeOnly.Parse(hhmm), DateTimeKind.Utc).AddHours(-1);

    private static Appointment Visit(Guid? doctorId, DateOnly day, string hhmm, int minutes) =>
        new(Guid.NewGuid(), ClinicId, doctorId is null ? null : Guid.NewGuid(), doctorId, At(day, hhmm), TimeSpan.FromMinutes(minutes));

    private static FreeDay One(
        IReadOnlyCollection<Appointment>? visits = null,
        Guid? doctorId = null,
        int minutes = 30,
        string? doctorHours = null,
        string? clinicHours = ClinicHours,
        IReadOnlyCollection<DoctorLeave>? leaves = null,
        Guid? exclude = null,
        DateTime? now = null,
        DateOnly? day = null) =>
        FreeSlotFinder.Compute(
            WorkingHoursResolver.Resolve(doctorHours, clinicHours),
            leaves ?? Array.Empty<DoctorLeave>(),
            visits ?? Array.Empty<Appointment>(),
            doctorId ?? Salma,
            TimeSpan.FromMinutes(minutes),
            day ?? Monday,
            1,
            exclude,
            now ?? LongAgo)[0];

    private static List<string> Starts(FreeDay day) => day.Starts.Select(s => s.ToString("HH:mm")).ToList();

    [Fact]
    public void An_Empty_Day_Lists_Every_Quarter_Hour_Inside_The_Hours_And_Outside_The_Break()
    {
        var day = One();

        Assert.Equal(FreeDayState.Open, day.State);
        var starts = Starts(day);
        Assert.Equal("09:00", starts[0]);
        Assert.Equal("16:30", starts[^1]);
        Assert.Contains("12:00", starts);   // 12:00–12:30 ends as the break begins
        Assert.Contains("13:30", starts);
        Assert.DoesNotContain("12:15", starts);
        Assert.DoesNotContain("13:00", starts);
        Assert.DoesNotContain("16:45", starts); // would end after 17:00
        Assert.Equal(26, starts.Count);
    }

    [Fact]
    public void The_Practitioners_Own_Hours_Replace_The_Clinics()
    {
        var starts = Starts(One(doctorHours: SalmaHours));

        Assert.Equal(new[] { "10:00", "10:15", "10:30", "10:45", "11:00", "11:15", "11:30" }, starts);
    }

    [Fact]
    public void Back_To_Back_Is_Free_And_Any_Overlap_Is_Not()
    {
        var starts = Starts(One(new[] { Visit(Salma, Monday, "10:00", 30) }));

        Assert.Contains("09:30", starts);
        Assert.Contains("10:30", starts);
        Assert.DoesNotContain("09:45", starts);
        Assert.DoesNotContain("10:00", starts);
        Assert.DoesNotContain("10:15", starts);
    }

    [Fact]
    public void An_Unassigned_Block_Takes_Every_Practitioner_And_A_Colleagues_Visit_Takes_None()
    {
        var starts = Starts(One(new[] { Visit(null, Monday, "11:00", 60), Visit(Nadia, Monday, "14:00", 30) }));

        Assert.DoesNotContain("11:00", starts);
        Assert.DoesNotContain("11:30", starts);
        Assert.Contains("14:00", starts);
    }

    [Fact]
    public void Cancelled_Absent_And_Retired_Visits_Free_Their_Slot()
    {
        var cancelled = Visit(Salma, Monday, "10:00", 30);
        cancelled.Cancel("motif");
        var absent = Visit(Salma, Monday, "11:00", 30);
        absent.MarkAsNoShow();
        var retired = Visit(Salma, Monday, "14:00", 30);
        retired.Disregard("local|someone", LongAgo);

        var starts = Starts(One(new[] { cancelled, absent, retired }));

        Assert.Contains("10:00", starts);
        Assert.Contains("11:00", starts);
        Assert.Contains("14:00", starts);
    }

    [Fact]
    public void The_Visit_Being_Moved_Does_Not_Block_Itself()
    {
        var moving = Visit(Salma, Monday, "10:00", 30);

        Assert.Contains("10:00", Starts(One(new[] { moving }, exclude: moving.Id)));
        Assert.DoesNotContain("10:00", Starts(One(new[] { moving })));
    }

    [Fact]
    public void A_Conge_Takes_The_Practitioners_Day_And_Says_So()
    {
        var leave = new[] { new DoctorLeave(Guid.NewGuid(), ClinicId, Salma, Monday, Monday, "Formation") };

        var salma = One(leaves: leave);
        Assert.Equal(FreeDayState.Leave, salma.State);
        Assert.Empty(salma.Starts);
        Assert.Equal(FreeDayState.Open, One(leaves: leave, day: Tuesday).State);
    }

    [Fact]
    public void A_Window_Is_Placed_On_Its_Tunis_Day()
    {
        // 23:30 UTC on Monday is already Tuesday in Tunis — the day a congé is matched on.
        var (first, last) = AppointmentScheduling.LocalDaysOf(new DateTime(2026, 11, 2, 23, 30, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

        Assert.Equal(Tuesday, first);
        Assert.Equal(Tuesday, last);
    }

    [Fact]
    public void A_Closed_Day_Is_Closed_And_A_Taken_Day_Is_Full()
    {
        Assert.Equal(FreeDayState.Closed, One(day: Sunday).State);

        var full = One(new[] { Visit(Salma, Monday, "09:00", 480) });
        Assert.Equal(FreeDayState.Full, full.State);
        Assert.Empty(full.Starts);
    }

    [Fact]
    public void Today_Offers_Only_Starts_Still_Ahead_And_Nothing_After_Closing()
    {
        var afternoon = One(now: At(Monday, "14:05"));
        Assert.Equal("14:15", Starts(afternoon)[0]);

        Assert.Equal(FreeDayState.Closed, One(now: At(Monday, "18:00")).State);
    }

    [Fact]
    public void No_Hours_Anywhere_Is_Unrestricted_With_No_List()
    {
        var day = One(clinicHours: null);

        Assert.Equal(FreeDayState.Unrestricted, day.State);
        Assert.Empty(day.Starts);
    }

    [Fact]
    public void A_Booking_Longer_Than_Each_Opening_Finds_No_Start()
    {
        Assert.Equal(FreeDayState.Closed, One(minutes: 240).State);
    }

    /// <summary>
    /// The guarantee « Reporter » rests on: a start the list offers is one the save accepts without an override, and a
    /// start it leaves out is one the save refuses. Every quarter hour of two days, against the guard's real helpers.
    /// </summary>
    [Fact]
    public async Task Every_Listed_Start_Is_Accepted_By_The_Booking_Guard_And_Every_Other_Is_Refused()
    {
        var cancelled = Visit(Salma, Monday, "16:00", 30);
        cancelled.Cancel("motif");
        var visits = new List<Appointment>
        {
            Visit(Salma, Monday, "10:00", 45),
            Visit(null, Monday, "15:00", 30),
            Visit(Nadia, Monday, "11:00", 30),
            cancelled,
        };
        var leave = new DoctorLeave(Guid.NewGuid(), ClinicId, Salma, Tuesday, Tuesday, null);
        var clinic = new Clinic(ClinicId, "Cabinet Test", code: "CODE01");
        clinic.SetWorkingHours(ClinicHours);
        var doctor = new Doctor(Salma, ClinicId, "Salma", "Ben Youssef", "General");

        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(visits);
        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByIdAsync(Salma, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
        var clinics = new Mock<IClinicRepository>();
        clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        var leaves = new Mock<IDoctorLeaveRepository>();
        leaves.Setup(r => r.GetOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { leave });
        leaves.Setup(r => r.FindOverlappingAsync(ClinicId, Salma, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, DateOnly from, DateOnly to, Guid? _, CancellationToken _) => leave.Covers(from, to) ? leave : null);

        var duration = TimeSpan.FromMinutes(30);
        var days = await FreeSlotFinder.FindAsync(
            appointments.Object, doctors.Object, clinics.Object, leaves.Object,
            ClinicId, Salma, duration, Monday, 2, null, LongAgo, default);

        var disagreements = new List<string>();
        foreach (var day in days)
        {
            for (var time = TimeSpan.Zero; time < TimeSpan.FromDays(1); time += FreeSlotFinder.Step)
            {
                var startUtc = day.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-1) + time;
                var listed = day.Starts.Contains(TimeOnly.FromTimeSpan(time));
                var collision = await AppointmentScheduling.FindCollisionAsync(
                    appointments.Object, ClinicId, Salma, startUtc, duration, null, default);
                var hours = await AppointmentScheduling.CheckWorkingHoursAsync(
                    doctors.Object, clinics.Object, leaves.Object, ClinicId, Salma, startUtc, duration, default);
                var accepted = collision is null && hours.IsSuccess;
                if (listed != accepted)
                {
                    disagreements.Add($"{day.Date:dd/MM} {TimeOnly.FromTimeSpan(time):HH:mm}: listed={listed}, accepted={accepted}");
                }
            }
        }

        Assert.True(disagreements.Count == 0, string.Join(Environment.NewLine, disagreements));
        Assert.True(days[0].Starts.Count > 0, "the fixture must leave Monday with free starts, or the test proves nothing");
        Assert.Equal(FreeDayState.Leave, days[1].State);
    }

    // ── the query ─────────────────────────────────────────────────────────────────────────────────────────────

    private static (GetFreeSlotsQueryHandler Handler, Appointment Visit) Query(Guid visitClinic)
    {
        var visit = new Appointment(Guid.NewGuid(), visitClinic, Guid.NewGuid(), Salma, At(Monday, "10:00"), TimeSpan.FromMinutes(60));
        var clinic = new Clinic(ClinicId, "Cabinet Test", code: "CODE01");
        clinic.SetWorkingHours(ClinicHours);

        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByIdAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(visit);
        appointments.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });
        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByIdAsync(Salma, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Doctor(Salma, ClinicId, "Salma", "Ben Youssef", "General"));
        var clinics = new Mock<IClinicRepository>();
        clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        var leaves = new Mock<IDoctorLeaveRepository>();
        leaves.Setup(r => r.GetOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DoctorLeave>());
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));

        return (new GetFreeSlotsQueryHandler(appointments.Object, doctors.Object, clinics.Object, leaves.Object, resolver.Object), visit);
    }

    [Fact]
    public async Task Another_Clinics_Visit_Reads_As_Not_Found()
    {
        var (handler, visit) = Query(OtherClinicId);

        var result = await handler.Handle(new GetFreeSlotsQuery { AppointmentId = visit.Id, From = Monday, Days = 1 }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Rendez-vous introuvable.", result.Error);
    }

    [Fact]
    public async Task The_Visits_Own_Practitioner_Length_And_Slot_Are_Used()
    {
        var (handler, visit) = Query(ClinicId);

        var result = await handler.Handle(new GetFreeSlotsQuery { AppointmentId = visit.Id, From = Monday, Days = 1 }, default);

        Assert.True(result.IsSuccess, result.Error);
        var day = Assert.Single(result.Value!.Days);
        Assert.Equal("2026-11-02", day.Date);
        Assert.Equal("Open", day.State);
        Assert.Contains("10:00", day.Starts);      // its own slot does not block it
        Assert.Contains("16:00", day.Starts);      // 60 min: 16:00–17:00 fits
        Assert.DoesNotContain("16:15", day.Starts);
        Assert.DoesNotContain("12:00", day.Starts); // 60 min would run into the break
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100, FreeSlotFinder.MaxDays)]
    public async Task The_Window_Is_Clamped(int asked, int served)
    {
        var (handler, visit) = Query(ClinicId);

        var result = await handler.Handle(new GetFreeSlotsQuery { AppointmentId = visit.Id, From = Monday, Days = asked }, default);

        Assert.Equal(served, result.Value!.Days.Count);
    }
}
