using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// <c>SubscriptionLedger</c> — the fold that decides when a cabinet stops being able to record work
/// (<c>clinic-subscription</c> Part A). The highest-value class in the feature: every screen, the gate, the warning
/// job and every vendor verb read the date this produces, so an off-by-one here is wrong everywhere at once and
/// visible nowhere in particular.
///
/// <para><b>Two properties carry most of the risk, and both are asserted directly.</b> The fold takes <b>no
/// clock</b> — <see cref="The_Same_Entries_Fold_To_The_Same_Date_Whatever_Today_Is"/> — and it folds on an
/// <b>exclusive</b> cursor, which is what makes one formula correct for an entry's recorded day (an inclusive
/// <i>start</i>) and for a running end date (an inclusive <i>end</i>). Getting the second wrong yields a 31-day
/// trial or a one-day grant on a lapsed cabinet, and both compile.</para>
///
/// <para>Every date here is a fixed literal. There is no <c>DateTime.UtcNow</c> anywhere in the file, deliberately:
/// a test that reads the clock agrees with a clock-dependent fold by construction.</para>
/// </summary>
public class SubscriptionLedgerTests
{
    private static readonly DateTime CreationDay = new(2026, 8, 10);

    private static SubscriptionLedgerEntry Entry(
        DateTime recordedOnClinicDay,
        int? months = null,
        int? days = null,
        DateTime? explicitEndsOn = null,
        bool cancelled = false,
        int sequence = 0) =>
        new(
            Guid.Parse($"11111111-1111-1111-1111-{sequence:D12}"),
            recordedOnClinicDay,
            // Ordering is by RecordedAtUtc then Id; derived from the recorded day plus the sequence so entries
            // recorded on the same day still have a deterministic order.
            recordedOnClinicDay.AddHours(sequence),
            months,
            days,
            explicitEndsOn,
            cancelled);

    // ---- the trial, which is the arithmetic the exclusive cursor exists for -------------------------

    // [AC-1.1] 30 days counting the creation day as DAY 1: created 10 Aug → the cabinet may work all of 8 Sep.
    // A single `anchor + duration` over the recorded day gives 9 Sep — a 31-day trial — which is exactly the
    // off-by-one that looks right and ships.
    [Fact]
    public void A_Trial_Only_Ledger_Ends_On_The_Thirtieth_Day_Counting_The_Creation_Day()
    {
        var endsOn = SubscriptionLedger.Fold(new[] { Entry(CreationDay, days: 30) });

        Assert.Equal(new DateTime(2026, 9, 8), endsOn);
        Assert.Equal(29, (endsOn!.Value - CreationDay).Days);
    }

    // The general form of the same rule, so the 30 above is not the only width that is right.
    [Theory]
    [InlineData(1, "2026-08-10")]
    [InlineData(2, "2026-08-11")]
    [InlineData(30, "2026-09-08")]
    [InlineData(45, "2026-09-23")]
    public void A_Days_Duration_Counts_The_Recorded_Day_As_Day_One(int days, string expected)
    {
        Assert.Equal(
            DateTime.Parse(expected),
            SubscriptionLedger.Fold(new[] { Entry(CreationDay, days: days) }));
    }

    // ---- AC-5.2 / EC-3: paying early never costs days ----------------------------------------------

    // [AC-5.2][EC-3] A cabinet covered to 20 Sep that pays 12 months lands on 20 Sep 2027 — the old end plus twelve
    // months, with NO −1. The grant is recorded 10 days BEFORE the end and must not restart from that day, which is
    // what « the later of the current end or today » means when read through each entry's own anchor.
    [Fact]
    public void Paying_Early_Extends_From_The_Existing_End_Date_Not_From_Today()
    {
        var endsOn = SubscriptionLedger.Fold(new[]
        {
            Entry(new DateTime(2026, 8, 22), explicitEndsOn: new DateTime(2026, 9, 20), sequence: 1),
            Entry(new DateTime(2026, 9, 10), months: 12, sequence: 2)
        });

        Assert.Equal(new DateTime(2027, 9, 20), endsOn);
    }

    // The mirror case, and the one the exclusive cursor stops being wrong in the other direction: a cabinet that
    // LAPSED restarts from the day the grant was recorded, so 12 months from 1 Oct runs to 30 Sep the next year —
    // not to 30 Sep + 1, and not from the stale end date.
    [Fact]
    public void A_Lapsed_Cabinet_Restarts_Its_Count_On_The_Day_The_Grant_Was_Recorded()
    {
        var endsOn = SubscriptionLedger.Fold(new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),                      // ends 8 Sep
            Entry(new DateTime(2026, 10, 1), months: 12, sequence: 2)        // recorded well after the lapse
        });

        Assert.Equal(new DateTime(2027, 9, 30), endsOn);
    }

    // ---- clock-freedom, the trap decision 3 spends a page on ---------------------------------------

    // [R-6] The fold is a pure function of the entries. If it took « today », a lapsed entry would restart from
    // today on every recomputation — so cancelling one entry would move unrelated dates, and `verify-schema`'s
    // « stored == fold » check would flap daily. There is no way to express that as a parameter here, which is the
    // assertion: the method has no clock to pass, and folding twice yields the same answer.
    [Fact]
    public void The_Same_Entries_Fold_To_The_Same_Date_Whatever_Today_Is()
    {
        var entries = new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),
            Entry(new DateTime(2026, 10, 1), months: 12, sequence: 2)
        };

        // Two "different days" are indistinguishable to a fold that reads no clock — which is the point.
        Assert.Equal(SubscriptionLedger.Fold(entries), SubscriptionLedger.Fold(entries));
        Assert.Equal(new DateTime(2027, 9, 30), SubscriptionLedger.Fold(entries));
    }

    [Fact]
    public void The_Fold_Does_Not_Depend_On_The_Order_The_Entries_Arrive_In()
    {
        var entries = new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),
            Entry(new DateTime(2026, 10, 1), months: 12, sequence: 2)
        };

        Assert.Equal(
            SubscriptionLedger.Fold(entries),
            SubscriptionLedger.Fold(entries.Reverse().ToList()));
    }

    // ---- AC-5.4 / EC-4: cancellation ---------------------------------------------------------------

    // [AC-5.4] Cancelling a MIDDLE entry moves the end date. This is the assertion an incremental
    // `EndsOn += duration` cannot satisfy: with that shape, cancelling anything but the latest entry changes
    // nothing at all, and the cabinet keeps cover it was never paid for.
    [Fact]
    public void Cancelling_A_Middle_Entry_Moves_The_End_Date()
    {
        var first = Entry(CreationDay, days: 30, sequence: 1);
        var middle = Entry(new DateTime(2026, 8, 20), months: 6, sequence: 2);
        var last = Entry(new DateTime(2026, 8, 25), months: 1, sequence: 3);

        var withMiddle = SubscriptionLedger.Fold(new[] { first, middle, last });
        var withoutMiddle = SubscriptionLedger.Fold(new[]
        {
            first,
            middle with { IsCancelled = true },
            last
        });

        Assert.NotEqual(withMiddle, withoutMiddle);
        Assert.True(withoutMiddle < withMiddle);
    }

    // [EC-4] Cancelling the only paid entry may push the date INTO THE PAST — the cabinet becomes read-only, which
    // is the correct outcome of « that payment never happened » and must not be clamped to today.
    [Fact]
    public void Cancelling_Can_Push_The_End_Date_Into_The_Past()
    {
        var trial = Entry(CreationDay, days: 30, sequence: 1);
        var paid = Entry(new DateTime(2026, 9, 1), months: 12, sequence: 2);

        var endsOn = SubscriptionLedger.Fold(new[] { trial, paid with { IsCancelled = true } });

        Assert.Equal(new DateTime(2026, 9, 8), endsOn);
    }

    [Fact]
    public void A_Ledger_Whose_Every_Entry_Is_Cancelled_Has_No_End_Date_At_All()
    {
        var endsOn = SubscriptionLedger.Fold(new[]
        {
            Entry(CreationDay, days: 30, cancelled: true, sequence: 1)
        });

        // Null, i.e. « nothing has ever entitled this cabinet » — which the state reader turns into Active-for-ever
        // rather than Expired. Worth knowing: a cabinet is made read-only by a date in the past, never by an
        // empty ledger, so a full cancellation is not how the vendor cuts somebody off (that is suspension).
        Assert.Null(endsOn);
    }

    // ---- month arithmetic and open-endedness -------------------------------------------------------

    // [FR-2][EC-3] AddMonths clamps: 31 Jan + 1 month is 28 Feb (29 in a leap year), never 3 March.
    [Theory]
    [InlineData("2026-01-31", 1, "2026-02-27")]
    [InlineData("2028-01-31", 1, "2028-02-28")]
    [InlineData("2026-08-31", 6, "2027-02-27")]
    public void Month_Durations_Clamp_To_The_End_Of_A_Shorter_Month(string recorded, int months, string expected)
    {
        Assert.Equal(
            DateTime.Parse(expected),
            SubscriptionLedger.Fold(new[] { Entry(DateTime.Parse(recorded), months: months) }));
    }

    [Fact]
    public void An_Open_Ended_Entry_Collapses_The_End_Date_To_Nothing()
    {
        var endsOn = SubscriptionLedger.Fold(new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),
            Entry(new DateTime(2026, 8, 15), sequence: 2)  // no duration of any kind
        });

        Assert.Null(endsOn);
    }

    // A cancelled open-ended entry must NOT collapse anything — it contributes nothing, like every other cancelled
    // row. Worth its own case because « is open-ended » and « is cancelled » are tested in that order in the fold.
    [Fact]
    public void A_Cancelled_Open_Ended_Entry_Does_Not_Collapse_The_End_Date()
    {
        var endsOn = SubscriptionLedger.Fold(new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),
            Entry(new DateTime(2026, 8, 15), cancelled: true, sequence: 2)
        });

        Assert.Equal(new DateTime(2026, 9, 8), endsOn);
    }

    [Fact]
    public void An_Empty_Ledger_Folds_To_No_End_Date()
    {
        Assert.Null(SubscriptionLedger.Fold(Array.Empty<SubscriptionLedgerEntry>()));
    }

    // ---- the spans, FR-2's derived « période couverte » --------------------------------------------

    [Fact]
    public void Each_Entry_Gets_The_Stretch_It_Covers()
    {
        var (endsOn, spans) = SubscriptionLedger.FoldWithSpans(new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),
            Entry(new DateTime(2026, 9, 1), months: 1, sequence: 2)
        });

        Assert.Equal(2, spans.Count);
        Assert.Equal(CreationDay, spans[0].FromDay);
        Assert.Equal(new DateTime(2026, 9, 8), spans[0].ThroughDay);
        // The second resumes where the first's cover ran out — 9 Sep, not the day it was recorded.
        Assert.Equal(new DateTime(2026, 9, 9), spans[1].FromDay);
        Assert.Equal(new DateTime(2026, 10, 8), spans[1].ThroughDay);
        Assert.Equal(new DateTime(2026, 10, 8), endsOn);
    }

    // A cancelled entry is DISPLAYED (« Annulé », struck through) while contributing nothing, which is why it needs
    // a span of its own that is empty rather than being dropped from the list — the history screen pages over these.
    [Fact]
    public void A_Cancelled_Entry_Keeps_A_Row_With_No_Period()
    {
        var (_, spans) = SubscriptionLedger.FoldWithSpans(new[]
        {
            Entry(CreationDay, days: 30, cancelled: true)
        });

        var span = Assert.Single(spans);
        Assert.Null(span.FromDay);
        Assert.Null(span.ThroughDay);
    }

    [Fact]
    public void An_Open_Ended_Entry_Gets_A_Start_And_No_End()
    {
        var (_, spans) = SubscriptionLedger.FoldWithSpans(new[] { Entry(CreationDay) });

        var span = Assert.Single(spans);
        Assert.Equal(CreationDay, span.FromDay);
        Assert.Null(span.ThroughDay);
    }

    [Fact]
    public void Fold_And_FoldWithSpans_Cannot_Disagree()
    {
        var entries = new[]
        {
            Entry(CreationDay, days: 30, sequence: 1),
            Entry(new DateTime(2026, 9, 1), months: 3, sequence: 2)
        };

        Assert.Equal(SubscriptionLedger.Fold(entries), SubscriptionLedger.FoldWithSpans(entries).EndsOn);
    }
}
