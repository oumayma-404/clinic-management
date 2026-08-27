using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// The two readers behind the appointment charts: that the fold lands in the right column, that empty buckets are
/// emitted, and that the trend's last point knows it is incomplete.
/// </summary>
public class DashboardAppointmentReadersTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Thursday 20 August 2026, 09:00 UTC.</summary>
    private static readonly DateTime NowUtc = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();

    /// <summary>A booking at 10:00 clinic-local on the given local day.</summary>
    private static AppointmentStatusSlot At(int year, int month, int day, AppointmentStatus status) =>
        new(ClinicClock.ToUtc(new DateTime(year, month, day, 10, 0, 0)), status);

    private void WireTimeline(params AppointmentStatusSlot[] slots)
    {
        _appointments.Setup(r => r.GetStatusTimelineAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(slots);
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>());
    }

    // ── The status mix ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_Class_Lands_In_Its_Own_Column_And_The_Totals_Add_Up()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        WireTimeline(
            At(2026, 8, 17, AppointmentStatus.Completed),
            At(2026, 8, 17, AppointmentStatus.Completed),
            At(2026, 8, 17, AppointmentStatus.NoShow),
            At(2026, 8, 19, AppointmentStatus.Cancelled),
            At(2026, 8, 19, AppointmentStatus.AwaitingClosure),
            At(2026, 8, 21, AppointmentStatus.Scheduled),
            At(2026, 8, 21, AppointmentStatus.Confirmed));

        var dto = await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, null, CancellationToken.None);

        Assert.Equal(7, dto.Buckets.Count);

        var monday = dto.Buckets[0];
        Assert.Equal("2026-08-17", monday.Start);
        Assert.Equal(2, monday.Done);
        Assert.Equal(1, monday.Absent);
        Assert.Equal(3, monday.Total);

        var wednesday = dto.Buckets[2];
        Assert.Equal(1, wednesday.Cancelled);
        Assert.Equal(1, wednesday.ToClose);

        var friday = dto.Buckets[4];
        Assert.Equal(2, friday.Upcoming);

        Assert.Equal(7, dto.Total);
        Assert.Equal(dto.Buckets.Sum(b => b.Total), dto.Total);
        // The five classes summed always equal the bucket's own total — the table view shows both, so a drift here
        // would be visible to a user as a row that does not add up.
        Assert.All(dto.Buckets, b =>
            Assert.Equal(b.Total, b.Done + b.Upcoming + b.ToClose + b.Cancelled + b.Absent));
    }

    [Fact]
    public async Task A_Day_With_Nothing_Is_Still_A_Column()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        WireTimeline(At(2026, 8, 17, AppointmentStatus.Completed));

        var dto = await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, null, CancellationToken.None);

        Assert.Equal(7, dto.Buckets.Count);
        Assert.Equal(0, dto.Buckets[^1].Total);
        Assert.Equal("2026-08-23", dto.Buckets[^1].Start);
    }

    [Fact]
    public async Task An_Empty_Window_Reports_Zero_Rather_Than_No_Buckets()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        WireTimeline();

        var dto = await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, null, CancellationToken.None);

        Assert.Equal(0, dto.Total);
        Assert.Equal(7, dto.Buckets.Count);
    }

    /// <summary>
    /// The footnote the five-class fold gives back: how many of « À venir » the patient has actually confirmed.
    /// Counted from the raw status, so it is a real reading rather than a share of the class.
    /// </summary>
    [Fact]
    public async Task Confirmed_Upcoming_Counts_Only_Confirmed_Not_The_Whole_Class()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        WireTimeline(
            At(2026, 8, 21, AppointmentStatus.Confirmed),
            At(2026, 8, 21, AppointmentStatus.Confirmed),
            At(2026, 8, 21, AppointmentStatus.Scheduled),
            At(2026, 8, 17, AppointmentStatus.Completed));

        var dto = await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, null, CancellationToken.None);

        Assert.Equal(3, dto.Buckets[4].Upcoming);
        Assert.Equal(2, dto.ConfirmedUpcoming);
    }

    /// <summary>
    /// A booking 30 minutes after clinic-local midnight is stored on the previous UTC day. It must be counted in the
    /// new day's column — the whole reason the bucketing is done in C# rather than in SQL.
    /// </summary>
    [Fact]
    public async Task A_Booking_Just_After_Local_Midnight_Is_Counted_In_The_New_Day()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        var justAfterMidnight = ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 8, 20)).AddMinutes(30);
        WireTimeline(new AppointmentStatusSlot(justAfterMidnight, AppointmentStatus.Completed));

        var dto = await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, null, CancellationToken.None);

        Assert.Equal(1, dto.Buckets[3].Done);      // Thursday 20th
        Assert.Equal(0, dto.Buckets[2].Total);     // and NOT Wednesday 19th
    }

    [Fact]
    public async Task The_Previous_Total_Comes_From_The_Preceding_Window_Of_The_Same_Length()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        var (previousFrom, previousToInclusive) = window.PreviousUtcRange;
        WireTimeline(At(2026, 8, 17, AppointmentStatus.Completed));
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                ClinicId, previousFrom, previousToInclusive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>
            {
                [AppointmentStatus.Completed] = 40,
                [AppointmentStatus.NoShow] = 2
            });

        var dto = await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, null, CancellationToken.None);

        // Every class counts toward the comparison, not just the honoured ones — it is a volume comparison.
        Assert.Equal(42, dto.PreviousTotal);
    }

    [Fact]
    public async Task The_Practitioner_Filter_Reaches_The_Repository()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        var doctorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        WireTimeline();

        await new DashboardAppointmentStatusReader(_appointments.Object)
            .ReadAsync(ClinicId, window, doctorId, CancellationToken.None);

        _appointments.Verify(r => r.GetStatusTimelineAsync(
            ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), doctorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The six-month trend ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Trend_Is_Six_Months_Oldest_First_With_Gaps_Filled()
    {
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>());
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, NowUtc);

        var points = await new DashboardAppointmentTrendReader(_appointments.Object)
            .ReadAsync(ClinicId, period, NowUtc, CancellationToken.None);

        Assert.Equal(DashboardPeriod.TrendMonths, points.Count);
        Assert.Equal("2026-03", points[0].Month);
        Assert.Equal("2026-08", points[^1].Month);
        Assert.All(points, p => Assert.Equal(0, p.Total));
    }

    [Fact]
    public async Task The_Trend_Carries_Both_Measures_From_One_Read()
    {
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>
            {
                [AppointmentStatus.Completed] = 180,
                [AppointmentStatus.Cancelled] = 12,
                [AppointmentStatus.NoShow] = 8
            });
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, NowUtc);

        var points = await new DashboardAppointmentTrendReader(_appointments.Object)
            .ReadAsync(ClinicId, period, NowUtc, CancellationToken.None);

        Assert.All(points, p =>
        {
            Assert.Equal(200, p.Total);
            Assert.Equal(180, p.Completed);
        });
    }

    /// <summary>
    /// Only the month the clinic is currently in is partial. Without the flag the last point holds a fraction of a
    /// month beside five whole ones and reads as a collapse in bookings.
    /// </summary>
    [Fact]
    public async Task Only_The_Current_Month_Is_Marked_Partial()
    {
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>());
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, NowUtc);

        var points = await new DashboardAppointmentTrendReader(_appointments.Object)
            .ReadAsync(ClinicId, period, NowUtc, CancellationToken.None);

        Assert.True(points[^1].IsPartial);
        Assert.All(points.Take(DashboardPeriod.TrendMonths - 1), p => Assert.False(p.IsPartial));
    }

    /// <summary>
    /// On the last day of a month nothing is partial — the month is complete. Computed per month rather than
    /// assumed of the last point, so this is a property rather than a coincidence.
    /// </summary>
    [Fact]
    public async Task On_The_Last_Day_Of_The_Month_No_Point_Is_Partial()
    {
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>());
        var lastDayOfAugust = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, lastDayOfAugust);

        var points = await new DashboardAppointmentTrendReader(_appointments.Object)
            .ReadAsync(ClinicId, period, lastDayOfAugust, CancellationToken.None);

        Assert.All(points, p => Assert.False(p.IsPartial));
    }

    /// <summary>
    /// Each month's upper bound is the last tick of its final local day, never the next midnight —
    /// <c>CountByStatusBetweenAsync</c> is inclusive at both ends, so an exclusive bound counts a midnight booking
    /// in two adjacent months (finding #20).
    /// </summary>
    [Fact]
    public async Task Month_Bounds_Never_Overlap()
    {
        var windows = new List<(DateTime From, DateTime To)>();
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime f, DateTime t, CancellationToken _) => windows.Add((f, t)))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>());
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, NowUtc);

        await new DashboardAppointmentTrendReader(_appointments.Object)
            .ReadAsync(ClinicId, period, NowUtc, CancellationToken.None);

        Assert.Equal(DashboardPeriod.TrendMonths, windows.Count);
        for (var i = 1; i < windows.Count; i++)
        {
            // Contiguous to the tick: no gap a booking could fall into, no overlap it could be counted twice in.
            Assert.Equal(windows[i].From, windows[i - 1].To.AddTicks(1));
        }
    }
}
