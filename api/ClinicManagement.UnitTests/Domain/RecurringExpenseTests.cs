using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The monthly-dépense template's own rules (`caisse-monthly-expenses`).
///
/// <para>Two of these carry the feature's whole promise about the past. <see cref="RecurringExpense.Update"/>
/// must leave <c>LastPostedMonth</c> alone, or « le loyer est passé à 850 » silently re-posts a month the
/// practice has already read and reconciled; and <see cref="RecurringExpense.MarkPosted"/> must only ever move
/// forward, or a re-run rewinds the marker and posts a month twice.</para>
/// </summary>
public class RecurringExpenseTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Cancelled = new(2026, 9, 2, 10, 30, 0, DateTimeKind.Utc);

    private static RecurringExpense Loyer(int dayOfMonth = 5, string lastPosted = "2026-09") =>
        new(Guid.NewGuid(), ClinicA, "Loyer", 800m, PaymentMethod.Cash, dayOfMonth, lastPosted, "Local");

    // ---- Construction ----

    [Fact]
    public void A_New_Series_Is_Active_And_Starts_From_The_Month_It_Was_Created_In()
    {
        var series = Loyer(lastPosted: "2026-09");

        Assert.True(series.IsActive);
        Assert.Null(series.CancelledAt);
        Assert.Equal("2026-09", series.LastPostedMonth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_Series_Refuses_An_Amount_That_Is_Not_Money(decimal amount)
    {
        Assert.Throws<ArgumentException>(() =>
            new RecurringExpense(Guid.NewGuid(), ClinicA, "Loyer", amount, PaymentMethod.Cash, 5, "2026-09"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void A_Series_Refuses_A_Day_Outside_The_Month(int dayOfMonth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecurringExpense(Guid.NewGuid(), ClinicA, "Loyer", 800m, PaymentMethod.Cash, dayOfMonth, "2026-09"));
    }

    // ---- Update: future months only ----

    [Fact]
    public void Modifying_A_Series_Leaves_The_Months_Already_Posted_Alone() // [AC-5]
    {
        var series = Loyer(dayOfMonth: 2, lastPosted: "2026-09");

        series.Update("Loyer", 850m, PaymentMethod.Transfer, 5, "Local, avenue Habib Bourguiba");

        Assert.Equal("2026-09", series.LastPostedMonth);
        Assert.Equal(850m, series.Amount);
        Assert.Equal(5, series.DayOfMonth);
        Assert.Equal(PaymentMethod.Transfer, series.Method);
        Assert.NotNull(series.UpdatedAt);
    }

    [Fact]
    public void Modifying_A_Series_Refuses_An_Amount_That_Is_Not_Money()
    {
        var series = Loyer();

        Assert.Throws<ArgumentException>(() => series.Update("Loyer", 0m, PaymentMethod.Cash, 5, null));
        Assert.Equal(800m, series.Amount);
    }

    // ---- MarkPosted: forwards only ----

    [Fact]
    public void Recording_A_Month_Advances_The_Marker()
    {
        var series = Loyer(lastPosted: "2026-08");

        series.MarkPosted("2026-09");

        Assert.Equal("2026-09", series.LastPostedMonth);
    }

    // [AC-2] A re-run must not rewind the marker, or the month it names is posted a second time.
    [Theory]
    [InlineData("2026-08")]
    [InlineData("2026-01")]
    [InlineData("2025-12")]
    public void Recording_An_Earlier_Month_Does_Not_Rewind_The_Marker(string earlier)
    {
        var series = Loyer(lastPosted: "2026-09");

        series.MarkPosted(earlier);

        Assert.Equal("2026-09", series.LastPostedMonth);
    }

    // ---- Stop ----

    [Fact]
    public void Stopping_A_Series_Ends_It_Without_Deleting_It() // [AC-6]
    {
        var series = Loyer();

        series.Stop(Cancelled);

        Assert.False(series.IsActive);
        Assert.Equal(Cancelled, series.CancelledAt);
    }

    // A double tap, or a second tab, must not move the instant the practice actually ended the commitment.
    [Fact]
    public void Stopping_A_Stopped_Series_Keeps_The_First_Instant() // [AC-6]
    {
        var series = Loyer();
        series.Stop(Cancelled);

        series.Stop(Cancelled.AddDays(30));

        Assert.Equal(Cancelled, series.CancelledAt);
    }

    // « Arrêter » is not « settle up »: the marker stays where it was, so a month left unposted stays unposted.
    [Fact]
    public void Stopping_A_Series_Does_Not_Settle_The_Month_It_Still_Owed() // [AC-6]
    {
        var series = Loyer(lastPosted: "2026-07");

        series.Stop(Cancelled);

        Assert.Equal("2026-07", series.LastPostedMonth);
    }
}
