using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Dashboard;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// [AC-2] <see cref="DashboardPeriod"/> is the single authority on the dashboard's period arithmetic, and this is the
/// highest-value class in the feature: every comparable figure is measured against the previous window this type
/// derives, so a boundary bug here silently corrupts eight KPIs at once while every other test stays green.
///
/// <para>The cases below are the ones that actually break naive implementations: the end-of-month
/// <c>AddMonths</c> clamp, the Monday-based week, and the inclusive-versus-exclusive upper bound.</para>
/// </summary>
public class DashboardPeriodTests
{
    // Tunisia is UTC+1 year-round, so a clinic-local midnight is 23:00 UTC the previous day. Asserting against the
    // clock rather than hardcoding 23:00 keeps this test honest if the zone ever changes.
    private static DateTime LocalMidnightUtc(int year, int month, int day) =>
        ClinicClock.StartOfLocalDayUtc(new DateTime(year, month, day));

    private static DateTime LastTickOfLocalDayUtc(int year, int month, int day) =>
        ClinicClock.EndOfLocalDayUtc(new DateTime(year, month, day)).AddTicks(-1);

    // [AC-2] The previous month is the whole previous CALENDAR month. On the 31st a naive
    // `today.AddMonths(-1) .. today` would clamp to 28 February and compare 31 days against one day.
    [Fact]
    public void Month_On_The_31st_Compares_Against_The_Whole_Previous_Month()
    {
        // 31 March 2026, 09:00 local.
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, new DateTime(2026, 3, 31, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2026, 3, 1), period.From);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 3, 31), period.ToInclusive);
        Assert.Equal(LocalMidnightUtc(2026, 2, 1), period.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 2, 28), period.PreviousToInclusive);
    }

    // [AC-2] The mirror case: from a short month back into a long one. 1 March must look back at all 28 days of
    // February, and February's own previous month must be all 31 days of January.
    [Fact]
    public void Month_On_The_1st_Covers_The_Full_Current_And_Previous_Months()
    {
        var march = DashboardPeriod.Resolve(DashboardPeriodKey.Month, new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2026, 3, 1), march.From);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 3, 31), march.ToInclusive);
        Assert.Equal(LocalMidnightUtc(2026, 2, 1), march.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 2, 28), march.PreviousToInclusive);

        var february = DashboardPeriod.Resolve(DashboardPeriodKey.Month, new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2026, 1, 1), february.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 1, 31), february.PreviousToInclusive);
    }

    // [AC-2] A leap February is 29 days, and the previous-month derivation must not assume 28.
    [Fact]
    public void Month_Handles_A_Leap_February()
    {
        var march = DashboardPeriod.Resolve(DashboardPeriodKey.Month, new DateTime(2028, 3, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2028, 2, 1), march.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2028, 2, 29), march.PreviousToInclusive);
    }

    // [AC-2] January's previous month crosses the year boundary.
    [Fact]
    public void Month_In_January_Looks_Back_Into_The_Previous_Year()
    {
        var january = DashboardPeriod.Resolve(DashboardPeriodKey.Month, new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2026, 1, 1), january.From);
        Assert.Equal(LocalMidnightUtc(2025, 12, 1), january.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2025, 12, 31), january.PreviousToInclusive);
    }

    // [AC-2] Monday-based, matching the agenda's date-fns `weekStartsOn: 1`. A Sunday must belong to the week that
    // STARTED on the preceding Monday, which is the case a `DayOfWeek`-as-offset implementation gets wrong.
    [Theory]
    [InlineData(2026, 6, 15)] // Monday
    [InlineData(2026, 6, 18)] // Thursday
    [InlineData(2026, 6, 21)] // Sunday
    public void Week_Always_Starts_On_The_Preceding_Monday(int year, int month, int day)
    {
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Week, new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2026, 6, 15), period.From);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 6, 21), period.ToInclusive);
        Assert.Equal(LocalMidnightUtc(2026, 6, 8), period.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 6, 14), period.PreviousToInclusive);
    }

    // [AC-2] Today's previous period is yesterday — one day against one day, never a partial.
    [Fact]
    public void Today_Compares_Against_Yesterday()
    {
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Today, new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LocalMidnightUtc(2026, 6, 15), period.From);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 6, 15), period.ToInclusive);
        Assert.Equal(LocalMidnightUtc(2026, 6, 14), period.PreviousFrom);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 6, 14), period.PreviousToInclusive);
    }

    // [AC-2] The current and previous windows must never touch. If ToInclusive were the next midnight (what
    // ClinicClock.EndOfLocalDayUtc returns) a payment recorded at exactly that instant would be counted in BOTH
    // windows by the inclusive money reads — finding #20, re-armed. This is the assertion that pins the fix.
    [Theory]
    [InlineData(DashboardPeriodKey.Today)]
    [InlineData(DashboardPeriodKey.Week)]
    [InlineData(DashboardPeriodKey.Month)]
    public void Windows_Are_Adjacent_But_Never_Overlapping(DashboardPeriodKey key)
    {
        var period = DashboardPeriod.Resolve(key, new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(period.PreviousToInclusive < period.From);
        // Exactly one tick apart: adjacent with no gap, so no instant falls between the two windows either.
        Assert.Equal(period.From, period.PreviousToInclusive.AddTicks(1));
        Assert.True(period.From < period.ToInclusive);
        Assert.True(period.PreviousFrom < period.PreviousToInclusive);
    }

    // [AC-2] Every bound is an explicit UTC instant. ApplicationDbContext treats Unspecified as UTC on write, so a
    // bare local value would be silently reinterpreted and shift every boundary by the clinic's offset.
    [Theory]
    [InlineData(DashboardPeriodKey.Today)]
    [InlineData(DashboardPeriodKey.Week)]
    [InlineData(DashboardPeriodKey.Month)]
    public void Every_Bound_Is_Explicitly_Utc(DashboardPeriodKey key)
    {
        var period = DashboardPeriod.Resolve(key, new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DateTimeKind.Utc, period.From.Kind);
        Assert.Equal(DateTimeKind.Utc, period.ToInclusive.Kind);
        Assert.Equal(DateTimeKind.Utc, period.PreviousFrom.Kind);
        Assert.Equal(DateTimeKind.Utc, period.PreviousToInclusive.Kind);
    }

    // [AC-2] The bounds are clinic-local days expressed in UTC, not UTC days. On a UTC+1 clinic the month begins at
    // 23:00 on the last day of the previous month — the whole reason ClinicClock is involved at all.
    [Fact]
    public void Bounds_Are_Clinic_Local_Days_Not_Utc_Days()
    {
        var period = DashboardPeriod.Resolve(DashboardPeriodKey.Month, new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        // 1 June 00:00 clinic-local == 31 May 23:00 UTC.
        Assert.Equal(new DateTime(2026, 5, 31, 23, 0, 0, DateTimeKind.Utc), period.From);
        Assert.Equal(6, ClinicClock.ToClinicLocal(period.From).Month);
        Assert.Equal(1, ClinicClock.ToClinicLocal(period.From).Day);
    }

    // [AC-6] The trend window spans exactly TrendMonths calendar months and ends inside the current one, regardless
    // of the selected period — a six-month series cannot be derived from a one-day window.
    [Theory]
    [InlineData(DashboardPeriodKey.Today)]
    [InlineData(DashboardPeriodKey.Week)]
    [InlineData(DashboardPeriodKey.Month)]
    public void Trend_Window_Spans_Six_Months_Whatever_The_Period(DashboardPeriodKey key)
    {
        var nowUtc = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = DashboardPeriod.Resolve(key, nowUtc);

        var (from, toInclusive) = period.TrendWindow(nowUtc);

        // Six months inclusive of June => January.
        Assert.Equal(LocalMidnightUtc(2026, 1, 1), from);
        Assert.Equal(LastTickOfLocalDayUtc(2026, 6, 15), toInclusive);
    }

    // [AC-2] An out-of-range cast falls back to the month rather than throwing: this type sits on the read path of
    // the application's home screen, so a bad query string must degrade, not 500.
    [Fact]
    public void An_Unknown_Key_Falls_Back_To_The_Month()
    {
        var period = DashboardPeriod.Resolve((DashboardPeriodKey)99, new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DashboardPeriodKey.Month, period.Key);
        Assert.Equal(LocalMidnightUtc(2026, 6, 1), period.From);
    }
}
