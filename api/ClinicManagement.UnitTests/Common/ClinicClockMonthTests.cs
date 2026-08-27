using ClinicManagement.Application.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <c>ClinicClock</c>'s Tunisian-month primitives (<c>vendor-whatsapp-messaging-quota</c> FR-8b, Part 0).
///
/// <para><b>Every case uses a fixed instant</b>, for <see cref="ClinicClockTests"/>' reason: a test that reads the
/// clock to build its expectation evaluates the same expression a clock bug does and agrees with it by
/// construction. Here that matters most for EC-7 — « a send at 23:59 Tunis on the 31st counts against that month »
/// is only answerable against a pinned instant.</para>
///
/// <para><b>The load-bearing case is
/// <see cref="The_Whole_Month_And_The_Month_To_Date_Differ_Mid_Month"/>.</b> The private implementation this feature
/// replaced was month-to-<i>date</i>, and its one caller feeds the vendor's own collected figure — which
/// <c>MoneyReadConsistencyTests</c> deliberately does not cover (FR-2). So collapsing the two primitives into one
/// would widen that window by the rest of the month with nothing else in the solution able to see it. This is the
/// assertion that stops it.</para>
/// </summary>
public class ClinicClockMonthTests
{
    // 22:59 UTC on 31 August 2026 = 23:59 the same day in Tunis. The last minute of the Tunisian month.
    private static readonly DateTime LastMinuteOfAugustUtc = new(2026, 8, 31, 22, 59, 0, DateTimeKind.Utc);

    // 23:01 UTC on 31 August 2026 = 00:01 on 1 September in Tunis. One minute later, and a different month.
    private static readonly DateTime FirstMinuteOfSeptemberUtc = new(2026, 8, 31, 23, 1, 0, DateTimeKind.Utc);

    // ---- EC-7: the month boundary is Tunisian, not UTC ---------------------------------------------

    [Fact]
    public void The_Month_Turns_At_Tunisian_Midnight_Not_Utc_Midnight() // [EC-7]
    {
        // Both instants are 31 August by UTC's reckoning. Only one of them is August in Tunis, and a send counted
        // against the UTC month would be booked into the forfait that had just closed.
        Assert.Equal(8, LastMinuteOfAugustUtc.Month);
        Assert.Equal(8, FirstMinuteOfSeptemberUtc.Month);

        Assert.Equal("2026-08", ClinicClock.CurrentMonthKey(LastMinuteOfAugustUtc));
        Assert.Equal("2026-09", ClinicClock.CurrentMonthKey(FirstMinuteOfSeptemberUtc));
    }

    [Fact]
    public void A_Month_Key_Is_The_Local_Days_Own_Month()
    {
        Assert.Equal("2026-08", ClinicClock.MonthKey(new DateTime(2026, 8, 1)));
        Assert.Equal("2026-08", ClinicClock.MonthKey(new DateTime(2026, 8, 31)));
        // Zero-padded, so lexicographic order is chronological order — which is what lets « effective month <= M »
        // be a plain string comparison in SQL (D-7).
        Assert.Equal("2026-01", ClinicClock.MonthKey(new DateTime(2026, 1, 9)));
        Assert.True(string.CompareOrdinal("2026-09", "2026-10") < 0);
    }

    // ---- The two range primitives, and the distinction between them --------------------------------

    [Fact]
    public void The_Whole_Month_Is_Inclusive_On_Both_Ends()
    {
        var (from, toInclusive) = ClinicClock.MonthRangeUtc("2026-08");

        // 1 August 00:00 Tunis is 31 July 23:00 UTC — the clinic's month starts an hour before the UTC one.
        Assert.Equal(new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Utc), from);
        // One tick inside the month, not the next midnight: every counting and money read here is inclusive on
        // both ends, and the exclusive bound counts a midnight row in both adjacent months (finding #20).
        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(new DateTime(2026, 8, 31)), toInclusive);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(DateTimeKind.Utc, toInclusive.Kind);

        // The instant one tick later is already September, on both bounds.
        Assert.True(toInclusive < ClinicClock.MonthRangeUtc("2026-09").From);
        Assert.Equal(toInclusive.AddTicks(1), ClinicClock.MonthRangeUtc("2026-09").From);
    }

    [Fact]
    public void The_Whole_Month_Handles_A_Leap_February()
    {
        var (_, toInclusive) = ClinicClock.MonthRangeUtc("2028-02");

        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(new DateTime(2028, 2, 29)), toInclusive);
    }

    [Fact]
    public void The_Whole_Month_And_The_Month_To_Date_Differ_Mid_Month() // [FR-8b]
    {
        var midMonth = new DateTime(2026, 8, 12);

        var whole = ClinicClock.MonthRangeUtc("2026-08");
        var toDate = ClinicClock.MonthToDateRangeUtc(midMonth);

        // They agree on where the month starts and disagree on where it ends. A single primitive would have made
        // the GetPlatformSummaryQuery move a silent widening of the vendor's collected-this-month window.
        Assert.Equal(whole.From, toDate.From);
        Assert.True(toDate.ToInclusive < whole.ToInclusive);
        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(midMonth), toDate.ToInclusive);
    }

    [Fact]
    public void The_Whole_Month_And_The_Month_To_Date_Agree_On_The_Last_Day()
    {
        var lastDay = new DateTime(2026, 8, 31);

        Assert.Equal(ClinicClock.MonthRangeUtc("2026-08"), ClinicClock.MonthToDateRangeUtc(lastDay));
    }

    /// <summary>
    /// The property that makes the <c>GetPlatformSummaryQuery</c> move safe: the deleted private method returned
    /// exactly this, so a mid-month « today » must produce the same pair it did.
    /// </summary>
    [Fact]
    public void The_Month_To_Date_Range_Reproduces_The_Private_Copy_It_Replaced()
    {
        var today = new DateTime(2026, 8, 12);

        var expected = (
            From: ClinicClock.StartOfLocalDayUtc(new DateTime(today.Year, today.Month, 1)),
            ToInclusive: ClinicClock.LastTickOfLocalDayUtc(today));

        Assert.Equal(expected, ClinicClock.MonthToDateRangeUtc(today));
    }

    // ---- Keys, labels and the renewal day ---------------------------------------------------------

    [Fact]
    public void The_French_Label_Is_Pinned_To_Fr_FR()
    {
        // « août 2026 », never « August 2026 »: this runs in a container whose ambient culture is whatever the base
        // image sets.
        Assert.Equal("août 2026", ClinicClock.MonthLabelFr(2026, 8));
        Assert.Equal("août 2026", ClinicClock.MonthLabelFr("2026-08"));
        Assert.Equal("janvier 2027", ClinicClock.MonthLabelFr("2027-01"));
    }

    [Fact]
    public void The_Next_Month_Rolls_The_Year()
    {
        Assert.Equal("2026-09", ClinicClock.NextMonthKey("2026-08"));
        Assert.Equal("2027-01", ClinicClock.NextMonthKey("2026-12"));
    }

    [Fact]
    public void The_Preceding_Months_Are_Newest_First_And_Cross_The_Year() // [AC-2.3]
    {
        Assert.Equal(
            new[] { "2026-12", "2026-11", "2026-10" },
            ClinicClock.PrecedingMonthKeys("2027-01", 3));

        // AC-2.3 asks for the twelve preceding months, so the oldest is a year and a month back.
        var twelve = ClinicClock.PrecedingMonthKeys("2026-08", 12);
        Assert.Equal(12, twelve.Count);
        Assert.Equal("2025-08", twelve[^1]);
    }

    [Fact]
    public void Asking_For_No_Preceding_Months_Yields_None()
    {
        Assert.Empty(ClinicClock.PrecedingMonthKeys("2026-08", 0));
        Assert.Empty(ClinicClock.PrecedingMonthKeys("2026-08", -3));
    }

    [Fact]
    public void The_Renewal_Day_Is_The_First_Of_The_Next_Month() // [AC-2.7]
    {
        // A bare calendar day, not an instant: AC-2.7 puts it on the screen as `2026-09-01`, and converting it
        // through toISOString would shift it into August for the first hour of every Tunisian day.
        Assert.Equal(new DateTime(2026, 9, 1), ClinicClock.FirstDayOfNextMonth(new DateTime(2026, 8, 12)));
        Assert.Equal(new DateTime(2026, 9, 1), ClinicClock.FirstDayOfNextMonth(new DateTime(2026, 8, 31)));
        Assert.Equal(new DateTime(2027, 1, 1), ClinicClock.FirstDayOfNextMonth(new DateTime(2026, 12, 4)));
    }

    // ---- Parsing a caller-supplied key ------------------------------------------------------------

    [Theory]
    [InlineData("2026-08", 2026, 8)]
    [InlineData("2026-01", 2026, 1)]
    public void A_Well_Formed_Key_Parses(string monthKey, int year, int month)
    {
        Assert.True(ClinicClock.TryParseMonthKey(monthKey, out var parsedYear, out var parsedMonth));
        Assert.Equal(year, parsedYear);
        Assert.Equal(month, parsedMonth);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-8")]
    [InlineData("2026-13")]
    [InlineData("2026-08-01")]
    [InlineData("aout 2026")]
    public void A_Malformed_Key_Is_Refused_Rather_Than_Guessed(string? monthKey)
    {
        // `--month` is caller-supplied, so this is the boundary validator rather than an internal invariant.
        Assert.False(ClinicClock.TryParseMonthKey(monthKey, out _, out _));
        Assert.Throws<ArgumentException>(() => ClinicClock.MonthRangeUtc(monthKey!));
    }
}
