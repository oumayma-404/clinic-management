using ClinicManagement.Application.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The clinic's wall clock (audit §§ 4.1 and 4.2, ACs P6.1–6.9). Tunisia is <b>UTC+1 all year</b>.
///
/// <para><b>Why every case here uses a fixed instant.</b> These are the tests that can actually fail on the two
/// findings. § 4.2 is « the invoice number takes its year from <c>UtcNow.Year</c> », and a test that asserts
/// against a freshly-read <c>DateTime.UtcNow.Year</c> agrees with that defect by construction — it evaluates the
/// same expression the bug does, so it passes either way, and additionally flakes for one hour every New Year.
/// § 1 flagged exactly that in <c>IssueInvoiceCommandHandlerTests</c> (AC-P6.9). A fixed instant makes the
/// question answerable: at 23:30 UTC on 31 December the clinic is already in the next year, and nothing about
/// when the suite runs changes that.</para>
/// </summary>
public class ClinicClockTests
{
    // 23:30 UTC on 31 December 2025 = 00:30 on 1 January 2026 in Tunis. The one instant that separates a
    // clinic-local year from a UTC one.
    private static readonly DateTime NewYearEveLateUtc = new(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc);

    // 22:30 UTC the same evening = 23:30 Tunis — still 31 December on both clocks, the control case.
    private static readonly DateTime NewYearEveEarlyUtc = new(2025, 12, 31, 22, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void ClinicYear_Is_The_Next_Year_In_The_Last_Hour_Of_A_Utc_December() // [AC-P6.7][AC-P6.8][AC-P6.9]
    {
        // The whole of § 4.2 in one assertion: the two answers differ, and the clinic's is the correct one.
        Assert.Equal(2025, NewYearEveLateUtc.Year);
        Assert.Equal(2026, ClinicClock.ClinicYear(NewYearEveLateUtc));
    }

    [Fact]
    public void ClinicYear_Matches_Utc_When_The_Two_Days_Agree()
    {
        Assert.Equal(2025, ClinicClock.ClinicYear(NewYearEveEarlyUtc));
    }

    [Fact]
    public void ClinicToday_Is_The_Local_Calendar_Day() // [AC-P6.4][AC-P6.6]
    {
        Assert.Equal(new DateTime(2026, 1, 1), ClinicClock.ClinicToday(NewYearEveLateUtc));
        Assert.Equal(new DateTime(2025, 12, 31), ClinicClock.ClinicToday(NewYearEveEarlyUtc));
    }

    [Fact]
    public void A_Local_Day_Starts_An_Hour_Before_Utc_Midnight() // [AC-P6.2]
    {
        var day = new DateTime(2026, 7, 15);

        // 15 July 00:00 Tunis is 14 July 23:00 UTC. This is why a payment taken at 00:30 Tunis fell into the
        // previous UTC day: the clinic's day starts an hour before the UTC one.
        Assert.Equal(new DateTime(2026, 7, 14, 23, 0, 0, DateTimeKind.Utc), ClinicClock.StartOfLocalDayUtc(day));
        Assert.Equal(new DateTime(2026, 7, 15, 23, 0, 0, DateTimeKind.Utc), ClinicClock.EndOfLocalDayUtc(day));
    }

    [Fact]
    public void LastTickOfLocalDay_Is_Inside_The_Day_Not_The_Next_Midnight() // [AC-P6.2] finding #20
    {
        var day = new DateTime(2026, 7, 15);
        var exclusiveEnd = ClinicClock.EndOfLocalDayUtc(day);
        var inclusiveEnd = ClinicClock.LastTickOfLocalDayUtc(day);

        // One tick, and the whole of finding #20: the money reads are inclusive on both ends, so handing them
        // the exclusive bound counts a payment recorded at exactly midnight in BOTH adjacent periods.
        Assert.Equal(exclusiveEnd.AddTicks(-1), inclusiveEnd);
        Assert.True(inclusiveEnd < exclusiveEnd);
    }

    [Fact]
    public void TodayRangeUtc_Covers_Exactly_One_Local_Day() // [AC-P6.2][AC-P6.3]
    {
        var (from, toInclusive) = ClinicClock.TodayRangeUtc(NewYearEveLateUtc);

        // The clinic is in 1 January 2026, so the range is that day — not 31 December, which is what
        // `DateTime.UtcNow.Date` returned and what made « aujourd'hui » run 01:00 to 01:00 (§ 4.1).
        Assert.Equal(ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 1, 1)), from);
        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(new DateTime(2026, 1, 1)), toInclusive);

        // The instant we are actually at must fall inside the window it is used to describe.
        Assert.InRange(NewYearEveLateUtc, from, toInclusive);
    }

    [Fact]
    public void ToUtc_And_ToClinicLocal_Round_Trip()
    {
        var wallClock = new DateTime(2026, 7, 15, 9, 30, 0);

        var asUtc = ClinicClock.ToUtc(wallClock);

        Assert.Equal(DateTimeKind.Utc, asUtc.Kind);
        // A bare local DateTime handed to a query would be reinterpreted as UTC by the DbContext's converter
        // and shift every boundary by an hour, which is why the helpers return an explicit UTC instant.
        Assert.Equal(new DateTime(2026, 7, 15, 8, 30, 0, DateTimeKind.Utc), asUtc);
        Assert.Equal(wallClock, ClinicClock.ToClinicLocal(asUtc));
    }
}
