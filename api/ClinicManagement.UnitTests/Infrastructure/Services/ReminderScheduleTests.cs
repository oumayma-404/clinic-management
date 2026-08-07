using ClinicManagement.Application.Common;
using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Tiered reminder send-time computation (spec "Reminder scheduling" rule, AC-1) — and since L3c the fact that
/// it returns <b>every</b> future tier rather than only the largest, with a clinic-local quiet-hours floor.
/// Pure function, no I/O.
///
/// <para>The old contract (<c>ComputeSendTimeUtc</c> → one nullable instant) is what this replaces: the biggest
/// future tier won and the rest were discarded, while the settings screen invited « Ex. 24, 6 ». Every assertion
/// about "the largest tier" below is now an assertion that the largest tier is <b>among</b> the results, which is
/// the difference the feature is about.</para>
/// </summary>
public class ReminderScheduleTests
{
    private static readonly int[] Tiers = { 24, 6 };
    private const int MinLead = 1;
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Equal bounds = the floor is off. Used by every tier test so the two concerns stay separable.</summary>
    private static readonly (int StartHour, int EndHour) NoQuietHours = (0, 0);

    /// <summary>The shipped default: nothing sends between 21:00 and 08:00 clinic-local.</summary>
    private static readonly (int StartHour, int EndHour) DefaultQuietHours = (21, 8);

    private static IReadOnlyList<ReminderSendTime> Compute(
        DateTime appt, (int StartHour, int EndHour)? quiet = null) =>
        ReminderSchedule.ComputeSendTimesUtc(appt, Now, Tiers, MinLead, quiet ?? NoQuietHours);

    [Fact]
    public void Returns_Every_Future_Tier_Not_Just_The_Largest()
    {
        var appt = Now.AddHours(30); // both appt-24h (in 6h) and appt-6h (in 24h) are future

        var sends = Compute(appt);

        // The whole point of L3c: two tiers configured, two messages queued.
        Assert.Equal(2, sends.Count);
        Assert.Contains(sends, s => s.LeadHours == 24 && s.SendAtUtc == appt.AddHours(-24));
        Assert.Contains(sends, s => s.LeadHours == 6 && s.SendAtUtc == appt.AddHours(-6));
    }

    [Fact]
    public void Orders_Earliest_First()
    {
        var sends = Compute(Now.AddHours(30));

        Assert.Equal(sends.OrderBy(s => s.SendAtUtc).Select(s => s.SendAtUtc), sends.Select(s => s.SendAtUtc));
    }

    [Fact]
    public void Drops_A_Tier_Already_In_The_Past_And_Keeps_The_Rest()
    {
        var appt = Now.AddHours(10); // appt-24h is in the past; appt-6h is 4h from now

        var sends = Compute(appt);

        var single = Assert.Single(sends);
        Assert.Equal(6, single.LeadHours);
        Assert.Equal(appt.AddHours(-6), single.SendAtUtc);
    }

    [Fact]
    public void Sends_Promptly_When_Inside_Smallest_Tier_But_Outside_Min_Lead()
    {
        var appt = Now.AddHours(3); // closer than 6h, still more than 1h out

        var single = Assert.Single(Compute(appt));

        Assert.Equal(ReminderSendTime.PromptLeadHours, single.LeadHours);
        Assert.Equal(Now, single.SendAtUtc); // prompt → due on the next tick
    }

    [Fact]
    public void No_Reminder_When_Within_The_Min_Lead_Window()
    {
        Assert.Empty(Compute(Now.AddMinutes(45))); // <= now + 1h
    }

    [Fact]
    public void No_Reminder_When_Appointment_Is_In_The_Past()
    {
        Assert.Empty(Compute(Now.AddHours(-2)));
    }

    [Fact]
    public void Ignores_A_Duplicated_Or_Non_Positive_Tier()
    {
        // A comma-separated settings field is hand-typed: « 24, 24, 0, -6 » must not queue four messages.
        var appt = Now.AddHours(48);

        var sends = ReminderSchedule.ComputeSendTimesUtc(
            appt, Now, new[] { 24, 24, 0, -6 }, MinLead, NoQuietHours);

        Assert.Equal(24, Assert.Single(sends).LeadHours);
    }

    /*
     * Quiet hours. The motivating case is stated in the spec: an 08:00 appointment booked ~22 h ahead resolves
     * its 24 h tier to 02:00, and a reminder that wakes the patient is worse than none.
     */

    [Fact]
    public void Pulls_A_Send_That_Lands_In_Quiet_Hours_Back_To_The_Evening_Before()
    {
        // 2026-01-05 07:00 UTC == 08:00 clinic-local. 24h earlier is 07:00 local on the 4th (fine); 6h earlier
        // is 02:00 local on the 5th — inside 21:00→08:00.
        var appt = new DateTime(2026, 1, 5, 7, 0, 0, DateTimeKind.Utc);

        var sends = ReminderSchedule.ComputeSendTimesUtc(appt, Now, Tiers, MinLead, DefaultQuietHours);

        var moved = Assert.Single(sends, s => s.LeadHours == 6);
        var local = ClinicClock.ToClinicLocal(moved.SendAtUtc);
        // Earlier, not later: the evening before reaches the patient; 08:00 IS the appointment.
        //
        // 20:59 and not 21:00, deliberately: the window is [21:00, 08:00), so its own start hour is itself quiet,
        // and returning it would move the send from one quiet instant to another. The full-day sweep below is
        // what caught that.
        Assert.Equal(20, local.Hour);
        Assert.Equal(59, local.Minute);
        Assert.Equal(4, local.Day);
    }

    [Fact]
    public void Never_Places_A_Send_Inside_Quiet_Hours()
    {
        // Sweep a full day of appointment times: whatever the tiers resolve to, nothing may land in the window.
        for (var hour = 0; hour < 24; hour++)
        {
            var appt = new DateTime(2026, 1, 6, hour, 0, 0, DateTimeKind.Utc);
            foreach (var send in ReminderSchedule.ComputeSendTimesUtc(appt, Now, Tiers, MinLead, DefaultQuietHours))
            {
                var local = ClinicClock.ToClinicLocal(send.SendAtUtc);
                Assert.False(
                    local.Hour >= 21 || local.Hour < 8,
                    $"appointment at {hour}:00Z produced a send at {local:yyyy-MM-dd HH:mm} local");
            }
        }
    }

    [Fact]
    public void A_Send_Is_Never_Moved_Into_The_Past()
    {
        // `Now` is 13:00 local, outside the window, so the prompt tier is unaffected; the assertion that matters
        // is the general one — no tier may resolve before now, or the dispatcher would fire it immediately with
        // whatever wording it had.
        for (var hours = 2; hours <= 72; hours++)
        {
            foreach (var send in ReminderSchedule.ComputeSendTimesUtc(
                         Now.AddHours(hours), Now, Tiers, MinLead, DefaultQuietHours))
            {
                Assert.True(send.SendAtUtc >= Now, $"tier for +{hours}h resolved to {send.SendAtUtc:o}");
            }
        }
    }

    [Fact]
    public void Equal_Quiet_Bounds_Disable_The_Floor_Rather_Than_Blocking_Everything()
    {
        var appt = new DateTime(2026, 1, 5, 7, 0, 0, DateTimeKind.Utc);

        var sends = ReminderSchedule.ComputeSendTimesUtc(appt, Now, Tiers, MinLead, (0, 0));

        // With the floor off, the 6h tier keeps its raw 02:00-local slot instead of being moved.
        var raw = Assert.Single(sends, s => s.LeadHours == 6);
        Assert.Equal(appt.AddHours(-6), raw.SendAtUtc);
    }
}
