using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Expenses;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Expenses;

/// <summary>
/// The month arithmetic behind a monthly dépense (`caisse-monthly-expenses`).
///
/// <para><b>This is the highest-value class in the feature.</b> Nobody watches the posting pass run: it fires at
/// 06:00 Tunis, and a wrong month or a wrong day produces a dépense that looks entirely plausible on every screen.
/// So the calendar rules are pinned here, where they are pure, rather than inferred from the job's behaviour.</para>
///
/// <para><b>Every month key is a fixed literal and there is no <c>DateTime.UtcNow</c> in the file.</b> The whole
/// subject is a calendar boundary, which is why <see cref="MonthlyExpenseSchedule.DueMonths"/> takes « the current
/// month » as a parameter — a fixture that reads the clock agrees with a clock-reading implementation by
/// construction, and additionally passes or fails depending on when the suite runs (<c>ClinicClockTests</c>'
/// standing lesson).</para>
/// </summary>
public class MonthlyExpenseScheduleTests
{
    // ---- DueMonths: what the pass still owes ----

    // The ordinary case, every day of every month except the turn: nothing is owed.
    [Fact]
    public void A_Series_Posted_This_Month_Owes_Nothing()
    {
        Assert.Empty(MonthlyExpenseSchedule.DueMonths("2026-09", "2026-09"));
    }

    [Fact]
    public void A_Series_Posted_Last_Month_Owes_This_One() // [AC-2]
    {
        Assert.Equal(new[] { "2026-09" }, MonthlyExpenseSchedule.DueMonths("2026-08", "2026-09"));
    }

    // [AC-2] The reason the pass returns a LIST: a clinic PC switched off for a quarter comes back owing three
    // loyers, and « post the current month » would silently swallow the other two.
    [Fact]
    public void A_Clinic_Switched_Off_For_A_Quarter_Owes_Every_Missed_Month_Oldest_First()
    {
        Assert.Equal(
            new[] { "2026-07", "2026-08", "2026-09" },
            MonthlyExpenseSchedule.DueMonths("2026-06", "2026-09"));
    }

    [Fact]
    public void A_Gap_Across_A_Year_Boundary_Is_Filled_In_Order()
    {
        Assert.Equal(
            new[] { "2025-11", "2025-12", "2026-01" },
            MonthlyExpenseSchedule.DueMonths("2025-10", "2026-01"));
    }

    // Empty, never backwards. A marker somehow ahead of today must not post a negative run of months.
    [Fact]
    public void A_Marker_Ahead_Of_Today_Owes_Nothing()
    {
        Assert.Empty(MonthlyExpenseSchedule.DueMonths("2026-12", "2026-09"));
    }

    // A corrupt marker costs a skipped series, never an endless walk — the loop's structural bound.
    [Theory]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026-13")]
    [InlineData("septembre")]
    [InlineData(null)]
    public void An_Unreadable_Month_Owes_Nothing_Rather_Than_Throwing(string? marker)
    {
        Assert.Empty(MonthlyExpenseSchedule.DueMonths(marker!, "2026-09"));
    }

    // The catch-up is bounded, so a marker from the last century cannot post ten thousand rows.
    [Fact]
    public void The_Catch_Up_Is_Bounded_Rather_Than_Unbounded()
    {
        var due = MonthlyExpenseSchedule.DueMonths("1900-01", "2026-09");

        Assert.Equal(120, due.Count);
        Assert.Equal("1900-02", due[0]);
    }

    // ---- PostingDateUtc: which day, in whose calendar ----

    // [AC-7] The clamp, on each length February and the thirty-day months take.
    [Theory]
    [InlineData("2026-01", 31, 2026, 1, 31)]
    [InlineData("2026-02", 31, 2026, 2, 28)]
    [InlineData("2028-02", 31, 2028, 2, 29)]
    [InlineData("2026-04", 31, 2026, 4, 30)]
    [InlineData("2026-06", 31, 2026, 6, 30)]
    [InlineData("2026-09", 31, 2026, 9, 30)]
    [InlineData("2026-02", 29, 2026, 2, 28)]
    [InlineData("2026-09", 5, 2026, 9, 5)]
    public void A_Day_Past_The_Month_Falls_On_Its_Last_Day(
        string monthKey, int dayOfMonth, int year, int month, int expectedDay)
    {
        var posted = MonthlyExpenseSchedule.PostingDateUtc(monthKey, dayOfMonth);

        Assert.Equal(new DateTime(year, month, expectedDay), ClinicClock.ToClinicLocal(posted).Date);
    }

    // [AC-7] The offset's DIRECTION, pinned with a literal. The clamp cases above read the instant back through
    // the same clock that wrote it, so they would still agree if the zone handling inverted; this one would not.
    // Tunisia is UTC+1, so the 5th of September begins at 23:00 on the 4th, UTC.
    [Fact]
    public void A_Posting_Date_Is_The_Start_Of_The_Cabinets_Day_Not_Of_UTCs()
    {
        Assert.Equal(
            new DateTime(2026, 9, 4, 23, 0, 0, DateTimeKind.Utc),
            MonthlyExpenseSchedule.PostingDateUtc("2026-09", 5));
    }

    [Fact]
    public void An_Unreadable_Month_Cannot_Be_Posted()
    {
        Assert.Throws<ArgumentException>(() => MonthlyExpenseSchedule.PostingDateUtc("2026-13", 5));
    }

    // ---- MonthOf / DayOfMonthOf: reading a stored dépense back ----

    // ⚠️ The defect this pair exists to prevent, and it bit twice in this feature (once on the frontend as
    // `.slice(0, 10)`): a dépense on the Tunisian 1st is STORED as 23:00 on the last day of the previous month,
    // so `.Date` on the instant answers with the wrong month AND the wrong day.
    [Fact]
    public void A_Depense_On_The_First_Belongs_To_Its_Own_Month_Not_To_UTCs()
    {
        var storedForSeptemberFirst = new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc);

        Assert.Equal("2026-09", MonthlyExpenseSchedule.MonthOf(storedForSeptemberFirst));
        Assert.Equal(1, MonthlyExpenseSchedule.DayOfMonthOf(storedForSeptemberFirst));
        Assert.Equal(8, storedForSeptemberFirst.Date.Month); // what the naive reading would have said
    }

    [Fact]
    public void A_Stored_Day_Round_Trips_Through_The_Cabinets_Calendar()
    {
        var posted = MonthlyExpenseSchedule.PostingDateUtc("2026-09", 5);

        Assert.Equal("2026-09", MonthlyExpenseSchedule.MonthOf(posted));
        Assert.Equal(5, MonthlyExpenseSchedule.DayOfMonthOf(posted));
    }

    // ---- The day-of-month refusal ----

    [Theory]
    [InlineData(1, true)]
    [InlineData(28, true)]
    [InlineData(31, true)]
    [InlineData(0, false)]
    [InlineData(32, false)]
    [InlineData(-1, false)]
    public void A_Day_Outside_One_To_Thirty_One_Is_Refused_In_French(int dayOfMonth, bool accepted)
    {
        var refusal = MonthlyExpenseSchedule.RefuseDayOfMonth(dayOfMonth);

        if (accepted)
        {
            Assert.Null(refusal);
        }
        else
        {
            Assert.Equal(MonthlyExpenseSchedule.DayOutOfRange, refusal);
        }
    }
}
