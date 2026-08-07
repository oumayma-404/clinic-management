using ClinicManagement.Application.Features.Dashboard;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// [AC-3][AC-4] <see cref="PeriodComparison"/> is the one place the dashboard's delta arithmetic and its
/// "no comparison available" representation live. The cases that matter are the degenerate ones: a zero baseline, an
/// undefined rate, and a negative baseline.
/// </summary>
public class PeriodComparisonTests
{
    // [AC-3] The ordinary case.
    [Fact]
    public void Of_Computes_A_Signed_Percentage_Rounded_To_One_Decimal()
    {
        var rise = PeriodComparison.Of(84m, 71m);
        Assert.Equal(84m, rise.Current);
        Assert.Equal(71m, rise.Previous);
        Assert.Equal(18.3m, rise.DeltaPercent);

        var fall = PeriodComparison.Of(12m, 15m);
        Assert.Equal(-20.0m, fall.DeltaPercent);
    }

    // [AC-3] A zero baseline yields no percentage. "From nothing to something" is not +100 % — it is a change with no
    // meaningful ratio, and the UI has a defined rendering for that (« — »). Returning 100 or infinity would both be
    // inventing a number.
    [Fact]
    public void Of_Yields_No_Delta_When_The_Previous_Period_Was_Zero()
    {
        var comparison = PeriodComparison.Of(500m, 0m);

        Assert.Equal(500m, comparison.Current);
        Assert.Equal(0m, comparison.Previous);
        Assert.Null(comparison.DeltaPercent);
    }

    // [AC-3] Asymmetric on purpose: falling TO zero from a real figure is expressible, and is exactly the kind of
    // collapse a clinic owner needs to see.
    [Fact]
    public void Of_Reports_Minus_One_Hundred_When_A_Real_Figure_Falls_To_Zero()
    {
        Assert.Equal(-100.0m, PeriodComparison.Of(0m, 900m).DeltaPercent);
    }

    // [AC-3] Both zero is a real, complete answer — two empty periods, no change — not a missing comparison, but the
    // ratio is still undefined, so the delta is null rather than 0.
    [Fact]
    public void Of_With_Both_Sides_Zero_Reports_Values_But_No_Delta()
    {
        var comparison = PeriodComparison.Of(0m, 0m);

        Assert.Equal(0m, comparison.Current);
        Assert.Equal(0m, comparison.Previous);
        Assert.Null(comparison.DeltaPercent);
    }

    // [AC-3] Net can legitimately be negative. A rise from −100 to −50 is an IMPROVEMENT and must read as positive;
    // dividing by a signed baseline instead of its magnitude would invert the sign and tell the user their loss grew.
    [Fact]
    public void Of_Keeps_The_Sign_Meaningful_Across_A_Negative_Baseline()
    {
        Assert.Equal(50.0m, PeriodComparison.Of(-50m, -100m).DeltaPercent);
        Assert.Equal(-50.0m, PeriodComparison.Of(-150m, -100m).DeltaPercent);
    }

    // [AC-4] A rate whose denominator was zero is UNDEFINED, not zero. For the taux d'absence, reporting 0 % would
    // assert perfect attendance — a far stronger claim than "nothing was booked", and one a closed practice would
    // broadcast every August.
    [Fact]
    public void Rate_With_No_Current_Value_Reports_Null_Not_Zero()
    {
        var comparison = PeriodComparison.Rate(null, 11.9m);

        Assert.Null(comparison.Current);
        Assert.Equal(11.9m, comparison.Previous);
        Assert.Null(comparison.DeltaPercent);
    }

    // [AC-4] An undefined previous rate suppresses the delta but must not hide the current one — a clinic in its
    // first month still deserves to see its own figure.
    [Fact]
    public void Rate_With_No_Previous_Value_Keeps_The_Current_One()
    {
        var comparison = PeriodComparison.Rate(8.3m, null);

        Assert.Equal(8.3m, comparison.Current);
        Assert.Null(comparison.Previous);
        Assert.Null(comparison.DeltaPercent);
    }

    // [AC-4] Both defined: a normal rate comparison, rounded through the shared money authority.
    [Fact]
    public void Rate_With_Both_Sides_Compares_Them()
    {
        var comparison = PeriodComparison.Rate(8.3m, 11.9m);

        Assert.Equal(8.3m, comparison.Current);
        Assert.Equal(11.9m, comparison.Previous);
        Assert.Equal(-30.3m, comparison.DeltaPercent);
    }

    // [AC-4] A rate of exactly zero is a REAL rate (everyone showed up) and must not be conflated with an undefined
    // one. This is the distinction the nullable Current exists to preserve.
    [Fact]
    public void Rate_Distinguishes_A_Real_Zero_From_An_Undefined_One()
    {
        var perfectAttendance = PeriodComparison.Rate(0m, 10m);
        var nothingBooked = PeriodComparison.Rate(null, 10m);

        Assert.Equal(0m, perfectAttendance.Current);
        Assert.Null(nothingBooked.Current);
        Assert.Equal(-100.0m, perfectAttendance.DeltaPercent);
        Assert.Null(nothingBooked.DeltaPercent);
    }
}
