using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// [AC-6] The « Tendance » series.
///
/// <para>⚠️ These tests mock <see cref="IInvoiceRepository"/>, so they prove the reader's <b>bucketing</b> and nothing
/// about SQL translation. That distinction is not academic here: the first implementation grouped by the clinic-local
/// month inside the query, passed this entire class, and then failed on the first real request with
/// <c>42883: function pg_catalog.timezone(unknown, interval) does not exist</c>. The reader now asks for a plain
/// per-month <c>SUM</c> and does the month maths in C#, which is what the bounds assertions below pin.</para>
/// </summary>
public class DashboardTrendReaderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime FixedNow = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();

    private static readonly DashboardPeriod Period =
        DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);

    private DashboardTrendReader Reader() => new(_invoices.Object);

    /// <summary>Every window returns 0 unless a test says otherwise.</summary>
    private void WireEmpty()
    {
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
    }

    /// <summary>Stubs one clinic-local calendar month's window with a figure.</summary>
    private void WireMonth(int year, int month, decimal collected)
    {
        var start = ClinicClock.StartOfLocalDayUtc(new DateTime(year, month, 1));
        var end = ClinicClock.EndOfLocalDayUtc(new DateTime(year, month, 1).AddMonths(1).AddDays(-1)).AddTicks(-1);

        _invoices.Setup(r => r.GetCollectedBetweenAsync(ClinicId, start, end, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collected);
    }

    // [AC-6] Six points, oldest first, ending in the current month.
    [Fact]
    public async Task Returns_Six_Consecutive_Months_Oldest_First()
    {
        WireEmpty();

        var points = await Reader().ReadAsync(ClinicId, Period, FixedNow, CancellationToken.None);

        Assert.Equal(DashboardPeriod.TrendMonths, points.Count);
        Assert.Equal(new[] { "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06" },
            points.Select(p => p.Month).ToArray());
    }

    // [AC-6] The load-bearing case: a month with no collections becomes an explicit zero, not a missing point.
    [Fact]
    public async Task Fills_Months_With_No_Collections_As_Zero()
    {
        WireEmpty();
        WireMonth(2026, 1, 9800m);
        WireMonth(2026, 6, 12400m);

        var points = await Reader().ReadAsync(ClinicId, Period, FixedNow, CancellationToken.None);

        Assert.Equal(6, points.Count);
        Assert.Equal(9800m, points[0].Collected);
        Assert.Equal(0m, points[1].Collected);
        Assert.Equal(0m, points[2].Collected);
        Assert.Equal(0m, points[3].Collected);
        Assert.Equal(0m, points[4].Collected);
        Assert.Equal(12400m, points[5].Collected);
    }

    // [AC-6] Each month is read over ITS OWN clinic-local window — the bug this class failed to catch was in exactly
    // this arithmetic, so it is asserted directly rather than inferred from the returned figures.
    [Fact]
    public async Task Reads_Each_Month_Over_Its_Own_Clinic_Local_Window()
    {
        WireEmpty();

        await Reader().ReadAsync(ClinicId, Period, FixedNow, CancellationToken.None);

        // February 2026: 1 Feb 00:00 local .. last tick of 28 Feb local.
        var febStart = ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 2, 1));
        var febEnd = ClinicClock.EndOfLocalDayUtc(new DateTime(2026, 2, 28)).AddTicks(-1);

        _invoices.Verify(r => r.GetCollectedBetweenAsync(ClinicId, febStart, febEnd, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _invoices.Verify(r => r.GetCollectedBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(DashboardPeriod.TrendMonths));
    }

    // [AC-6] Consecutive months must not overlap: each window ends exactly one tick before the next begins, or a
    // payment made at a month boundary would be counted in two points.
    [Fact]
    public async Task Consecutive_Month_Windows_Are_Adjacent_But_Never_Overlapping()
    {
        WireEmpty();
        var windows = new List<(DateTime From, DateTime To)>();
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime from, DateTime to, Guid? _, CancellationToken _) => windows.Add((from, to)))
            .ReturnsAsync(0m);

        await Reader().ReadAsync(ClinicId, Period, FixedNow, CancellationToken.None);

        Assert.Equal(DashboardPeriod.TrendMonths, windows.Count);
        for (var i = 1; i < windows.Count; i++)
        {
            Assert.Equal(windows[i].From, windows[i - 1].To.AddTicks(1));
        }
    }

    // [AC-6] A window crossing a year boundary must label and order the months correctly.
    [Fact]
    public async Task Crosses_The_Year_Boundary_Correctly()
    {
        var february = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc);
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, february);
        WireEmpty();
        WireMonth(2025, 9, 500m);
        WireMonth(2026, 2, 700m);

        var points = await Reader().ReadAsync(ClinicId, period, february, CancellationToken.None);

        Assert.Equal(new[] { "2025-09", "2025-10", "2025-11", "2025-12", "2026-01", "2026-02" },
            points.Select(p => p.Month).ToArray());
        Assert.Equal(500m, points[0].Collected);
        Assert.Equal(700m, points[5].Collected);
    }

    // [AC-6] A leap February's window covers 29 days.
    [Fact]
    public async Task Covers_A_Leap_February_In_Full()
    {
        var march = new DateTime(2028, 3, 10, 10, 0, 0, DateTimeKind.Utc);
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, march);
        WireEmpty();

        await Reader().ReadAsync(ClinicId, period, march, CancellationToken.None);

        var febEnd = ClinicClock.EndOfLocalDayUtc(new DateTime(2028, 2, 29)).AddTicks(-1);
        _invoices.Verify(r => r.GetCollectedBetweenAsync(
                ClinicId, ClinicClock.StartOfLocalDayUtc(new DateTime(2028, 2, 1)), febEnd, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-6] The series is independent of the selected period — a one-day window still yields six months, or the
    // sparkline would collapse whenever the user picked « Aujourd'hui ».
    [Fact]
    public async Task Is_Independent_Of_The_Selected_Period()
    {
        WireEmpty();
        var today = DashboardPeriod.Resolve(DashboardPeriodKey.Today, FixedNow);

        var points = await Reader().ReadAsync(ClinicId, today, FixedNow, CancellationToken.None);

        Assert.Equal(DashboardPeriod.TrendMonths, points.Count);
        Assert.Equal("2026-01", points[0].Month);
    }

    // [AC-1] Nothing is ever read for another clinic.
    [Fact]
    public async Task Every_Read_Is_Scoped_To_The_Callers_Clinic()
    {
        WireEmpty();

        await Reader().ReadAsync(ClinicId, Period, FixedNow, CancellationToken.None);

        _invoices.Verify(r => r.GetCollectedBetweenAsync(
            OtherClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
