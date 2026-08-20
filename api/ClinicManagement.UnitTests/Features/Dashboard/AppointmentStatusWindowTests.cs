using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// The period arithmetic behind « Rendez-vous par statut ».
///
/// <para>These are the rules a browser could not be trusted with and that no other test covers: which day a booking
/// falls in once Tunisia's UTC+1 is applied, how wide a bucket is, and what happens at the edges of a window that
/// does not start on a Monday or the 1st.</para>
/// </summary>
public class AppointmentStatusWindowTests
{
    /// <summary>A fixed instant so nothing here depends on when the suite runs. 20 August 2026 is a Thursday.</summary>
    private static readonly DateTime NowUtc = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    // ── The fold: seven statuses, five classes ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AppointmentStatus.Completed, AppointmentStatusClass.Done)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatusClass.Upcoming)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatusClass.Upcoming)]
    [InlineData(AppointmentStatus.InProgress, AppointmentStatusClass.ToClose)]
    [InlineData(AppointmentStatus.AwaitingClosure, AppointmentStatusClass.ToClose)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatusClass.Cancelled)]
    [InlineData(AppointmentStatus.NoShow, AppointmentStatusClass.Absent)]
    public void Every_Status_Folds_Into_Its_Class(AppointmentStatus status, AppointmentStatusClass expected)
    {
        Assert.Equal(expected, AppointmentStatusClasses.Of(status));
    }

    /// <summary>
    /// The two folds join statuses answering the same question, and nothing is folded across the outcome line.
    /// A cancellation and a no-show are different facts about the practice and a clinic acts on them differently, so
    /// a future refactor that collapsed them into one « perdu » class must fail here rather than in a review.
    /// </summary>
    [Fact]
    public void Cancelled_And_Absent_Are_Never_The_Same_Class()
    {
        Assert.NotEqual(
            AppointmentStatusClasses.Of(AppointmentStatus.Cancelled),
            AppointmentStatusClasses.Of(AppointmentStatus.NoShow));
    }

    /// <summary>
    /// « Quelqu'un est au fauteuil » and « le créneau est passé » share a class here, which is safe — but
    /// <c>InProgress</c> must never share with <c>Completed</c>, because that is the pair that decides whether the
    /// work is finished.
    /// </summary>
    [Fact]
    public void InProgress_Is_Never_Classed_As_Done()
    {
        Assert.NotEqual(
            AppointmentStatusClasses.Of(AppointmentStatus.InProgress),
            AppointmentStatusClasses.Of(AppointmentStatus.Completed));
    }

    // ── Granularity ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-08-20", "2026-08-20", AppointmentBucketGranularity.Day)]   // 1 day
    [InlineData("2026-08-17", "2026-08-23", AppointmentBucketGranularity.Day)]   // 7 days — a week
    [InlineData("2026-08-01", "2026-08-31", AppointmentBucketGranularity.Day)]   // 31 days — the daily ceiling
    [InlineData("2026-08-01", "2026-09-01", AppointmentBucketGranularity.Week)]  // 32 days — one past it
    [InlineData("2026-05-01", "2026-08-28", AppointmentBucketGranularity.Week)]  // 120 days — the weekly ceiling
    [InlineData("2026-05-01", "2026-08-29", AppointmentBucketGranularity.Month)] // 121 days — one past it
    public void Granularity_Follows_The_Span(string from, string to, AppointmentBucketGranularity expected)
    {
        var result = AppointmentStatusWindow.Resolve(from, to, NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.Granularity);
    }

    [Fact]
    public void A_Single_Day_Window_Counts_As_One_Day_Not_Zero()
    {
        var result = AppointmentStatusWindow.Resolve("2026-08-20", "2026-08-20", NowUtc);

        Assert.Equal(1, result.Value!.DayCount);
        Assert.Single(result.Value.Buckets());
    }

    // ── Refusals ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Window_Over_The_Cap_Is_Refused_And_Never_Silently_Clamped()
    {
        var result = AppointmentStatusWindow.Resolve("2025-01-01", "2026-12-31", NowUtc);

        Assert.True(result.IsFailure);
        Assert.Contains("366", result.Error);
    }

    [Fact]
    public void Exactly_The_Cap_Is_Allowed()
    {
        // 2026-08-20 back 365 days inclusive = 366 days.
        var result = AppointmentStatusWindow.Resolve("2025-08-20", "2026-08-20", NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(366, result.Value!.DayCount);
    }

    [Theory]
    [InlineData("2026-08-20", null)]
    [InlineData(null, "2026-08-20")]
    [InlineData("20/08/2026", "23/08/2026")]
    [InlineData("not-a-day", "2026-08-23")]
    public void A_Half_Or_Unreadable_Range_Is_Refused(string? from, string? to)
    {
        // A lone bound is a client that lost half its state, and « 20/08/2026 » means different days in different
        // locales — the one ambiguity a bare day key exists to remove. Both are refused rather than half-answered.
        Assert.True(AppointmentStatusWindow.Resolve(from, to, NowUtc).IsFailure);
    }

    [Fact]
    public void An_Inverted_Range_Is_Refused()
    {
        Assert.True(AppointmentStatusWindow.Resolve("2026-08-23", "2026-08-17", NowUtc).IsFailure);
    }

    [Fact]
    public void No_Bounds_At_All_Is_The_Current_Clinic_Week_Monday_First()
    {
        var result = AppointmentStatusWindow.Resolve(null, null, NowUtc);

        Assert.True(result.IsSuccess);
        // 20 August 2026 is a Thursday, so the week is Mon 17 → Sun 23.
        Assert.Equal(new DateTime(2026, 8, 17), result.Value!.FromLocalDate);
        Assert.Equal(new DateTime(2026, 8, 23), result.Value.ToLocalDate);
        Assert.Equal(DayOfWeek.Monday, result.Value.FromLocalDate.DayOfWeek);
    }

    // ── Buckets ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Week_Window_Is_Seven_Daily_Buckets_Including_The_Closed_Sunday()
    {
        // The zero-filled Sunday is the point: omitting an empty bucket shortens the series and slides every later
        // column left, so the week would render six days wide.
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;

        var buckets = window.Buckets();

        Assert.Equal(7, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(b.Start, b.EndInclusive));
        Assert.Equal(new DateTime(2026, 8, 23), buckets[^1].Start);
    }

    [Fact]
    public void Weekly_Buckets_Are_Clamped_To_The_Window_At_Both_Ends()
    {
        // Thursday 2 July → Wednesday 12 August: the first bucket is 4 days and the last is 3, and each is labelled
        // as the days actually counted rather than as a full week the read does not cover.
        var window = AppointmentStatusWindow.Resolve("2026-07-02", "2026-08-12", NowUtc).Value!;
        Assert.Equal(AppointmentBucketGranularity.Week, window.Granularity);

        var buckets = window.Buckets();

        Assert.Equal(new DateTime(2026, 7, 2), buckets[0].Start);
        Assert.Equal(new DateTime(2026, 7, 5), buckets[0].EndInclusive);  // the Sunday
        Assert.Equal(new DateTime(2026, 8, 10), buckets[^1].Start);       // the Monday
        Assert.Equal(new DateTime(2026, 8, 12), buckets[^1].EndInclusive);
        // Contiguous and gapless: bucket N+1 starts the day after bucket N ends.
        for (var i = 1; i < buckets.Count; i++)
        {
            Assert.Equal(buckets[i - 1].EndInclusive.AddDays(1), buckets[i].Start);
        }
    }

    [Fact]
    public void Monthly_Buckets_Are_Clamped_To_The_Window_Too()
    {
        var window = AppointmentStatusWindow.Resolve("2026-01-15", "2026-06-10", NowUtc).Value!;
        Assert.Equal(AppointmentBucketGranularity.Month, window.Granularity);

        var buckets = window.Buckets();

        Assert.Equal(6, buckets.Count);
        Assert.Equal(new DateTime(2026, 1, 15), buckets[0].Start);
        Assert.Equal(new DateTime(2026, 1, 31), buckets[0].EndInclusive);
        Assert.Equal(new DateTime(2026, 6, 1), buckets[^1].Start);
        Assert.Equal(new DateTime(2026, 6, 10), buckets[^1].EndInclusive);
    }

    /// <summary>
    /// Every bucket's index round-trips, at every granularity. This is what the reader runs once per appointment, so
    /// an off-by-one here files a séance in the wrong column — a wrong chart that looks entirely plausible.
    /// </summary>
    [Theory]
    [InlineData("2026-08-17", "2026-08-23")] // daily
    [InlineData("2026-07-02", "2026-08-12")] // weekly, both ends clamped
    [InlineData("2026-01-15", "2026-06-10")] // monthly, both ends clamped
    public void IndexOf_Agrees_With_Buckets_For_Every_Day_Of_The_Window(string from, string to)
    {
        var window = AppointmentStatusWindow.Resolve(from, to, NowUtc).Value!;
        var buckets = window.Buckets();

        for (var day = window.FromLocalDate; day <= window.ToLocalDate; day = day.AddDays(1))
        {
            var index = window.IndexOf(day);

            Assert.InRange(index, 0, buckets.Count - 1);
            Assert.True(
                day >= buckets[index].Start && day <= buckets[index].EndInclusive,
                $"{day:yyyy-MM-dd} was filed in bucket {index} " +
                $"({buckets[index].Start:yyyy-MM-dd}..{buckets[index].EndInclusive:yyyy-MM-dd})");
        }
    }

    [Fact]
    public void A_Day_Outside_The_Window_Has_No_Bucket()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;

        Assert.Equal(-1, window.IndexOf(new DateTime(2026, 8, 16)));
        Assert.Equal(-1, window.IndexOf(new DateTime(2026, 8, 24)));
    }

    // ── The bounds handed to SQL, and the comparison window ──────────────────────────────────────────────────────

    /// <summary>
    /// The upper bound is the last <b>tick</b> of the final local day, never the next midnight. The repository read
    /// is inclusive at both ends, so an exclusive bound would count a midnight booking in two adjacent windows —
    /// the defect already recorded as finding #20 for the money reads.
    /// </summary>
    [Fact]
    public void The_Utc_Range_Ends_On_The_Last_Tick_Of_The_Final_Local_Day()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;

        var (from, toInclusive) = window.UtcRange;

        Assert.Equal(ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 8, 17)), from);
        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(new DateTime(2026, 8, 23)), toInclusive);
        // One tick short of the next local midnight, and strictly before it.
        Assert.True(toInclusive < ClinicClock.EndOfLocalDayUtc(new DateTime(2026, 8, 23)));
    }

    [Fact]
    public void The_Previous_Window_Is_The_Same_Length_And_Ends_The_Day_Before()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;

        var (previousFrom, previousToInclusive) = window.PreviousUtcRange;
        var (from, _) = window.UtcRange;

        // Ends exactly where the current window begins, with no overlap and no gap.
        Assert.Equal(from, previousToInclusive.AddTicks(1));
        Assert.Equal(ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 8, 10)), previousFrom);
    }

    [Fact]
    public void A_Calendar_Month_Window_Compares_Against_The_Same_Number_Of_Days_Not_The_Previous_Month()
    {
        // Stated because it is the surprising half of « comparé à » on this card: August has 31 days, so the
        // comparison window is the 31 days ending 31 July — which is not the month of July. The card says so in
        // words rather than implying a month-to-month comparison it is not making.
        var window = AppointmentStatusWindow.Resolve("2026-08-01", "2026-08-31", NowUtc).Value!;

        var (previousFrom, previousToInclusive) = window.PreviousUtcRange;

        Assert.Equal(ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 7, 1)), previousFrom);
        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(new DateTime(2026, 7, 31)), previousToInclusive);
    }

    /// <summary>
    /// The reason the bucketing happens in C# at all: Tunisia is UTC+1, so a booking at 00:30 local is stored at
    /// 23:30 UTC on the previous day. Bucketing the raw instant files it under yesterday — and on the 1st, under
    /// last month.
    /// </summary>
    [Fact]
    public void A_Booking_Just_After_Local_Midnight_Belongs_To_The_New_Day()
    {
        var window = AppointmentStatusWindow.Resolve("2026-08-17", "2026-08-23", NowUtc).Value!;
        var justAfterLocalMidnight = ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 8, 20)).AddMinutes(30);

        // The raw UTC instant is still 19 August; the clinic-local day is the 20th.
        Assert.Equal(19, justAfterLocalMidnight.Day);
        Assert.Equal(
            window.IndexOf(new DateTime(2026, 8, 20)),
            window.IndexOf(ClinicClock.ToClinicLocal(justAfterLocalMidnight).Date));
    }
}
