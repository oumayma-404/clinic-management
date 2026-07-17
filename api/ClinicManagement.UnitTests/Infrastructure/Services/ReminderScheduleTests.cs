using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Tiered reminder send-time computation (spec "Reminder scheduling" rule, AC-1). Pure function, no I/O.
/// </summary>
public class ReminderScheduleTests
{
    private static readonly int[] Tiers = { 24, 6 };
    private const int MinLead = 1;
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Picks_The_Largest_Future_Tier_24h()
    {
        var appt = Now.AddHours(30); // appt-24h is 6h from now (future)

        var send = ReminderSchedule.ComputeSendTimeUtc(appt, Now, Tiers, MinLead);

        Assert.Equal(appt.AddHours(-24), send);
    }

    [Fact]
    public void Falls_Back_To_6h_Tier_When_24h_Tier_Already_Past()
    {
        var appt = Now.AddHours(10); // appt-24h is in the past; appt-6h is 4h from now

        var send = ReminderSchedule.ComputeSendTimeUtc(appt, Now, Tiers, MinLead);

        Assert.Equal(appt.AddHours(-6), send);
    }

    [Fact]
    public void Sends_Promptly_When_Inside_Smallest_Tier_But_Outside_Min_Lead()
    {
        var appt = Now.AddHours(3); // closer than 6h, still more than 1h out

        var send = ReminderSchedule.ComputeSendTimeUtc(appt, Now, Tiers, MinLead);

        Assert.Equal(Now, send); // prompt → due on the next tick
    }

    [Fact]
    public void No_Reminder_When_Within_The_Min_Lead_Window()
    {
        var appt = Now.AddMinutes(45); // <= now + 1h

        Assert.Null(ReminderSchedule.ComputeSendTimeUtc(appt, Now, Tiers, MinLead));
    }

    [Fact]
    public void No_Reminder_When_Appointment_Is_In_The_Past()
    {
        var appt = Now.AddHours(-2);

        Assert.Null(ReminderSchedule.ComputeSendTimeUtc(appt, Now, Tiers, MinLead));
    }

    [Fact]
    public void Prefers_The_Largest_Tier_For_A_Far_Future_Appointment()
    {
        var appt = Now.AddHours(100);

        var send = ReminderSchedule.ComputeSendTimeUtc(appt, Now, Tiers, MinLead);

        Assert.Equal(appt.AddHours(-24), send); // 24h preferred over 6h
    }
}
