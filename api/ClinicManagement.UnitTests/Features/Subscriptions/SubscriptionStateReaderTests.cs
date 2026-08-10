using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// <c>SubscriptionStateReader</c> — the single FR-1 rule six callers share (the gate, the screen, the banner, the
/// warning job, the report, the vendor verbs). If they each derived « is this cabinet expired? » themselves, a
/// cabinet could be refused a write by one and told it was fine by another.
///
/// <para>Every case uses a <b>fixed</b> clinic-local today. The reader takes it as a parameter precisely so the
/// midnight boundary — the one that matters, since an entitlement ends by the passage of time and not by a write —
/// is testable at all.</para>
/// </summary>
public class SubscriptionStateReaderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Today = new(2026, 8, 10);

    /// <summary>An entitlement ending on a given day, folded rather than assigned — there is no other way to set it.</summary>
    private static ClinicSubscription EndingOn(DateTime endsOn)
    {
        var subscription = ClinicSubscription.For(ClinicId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.Create(
                ClinicId,
                SubscriptionPeriodKind.Paid,
                recordedOnClinicDay: new DateTime(2026, 1, 1),
                recordedAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                explicitEndsOn: endsOn)
        });

        Assert.Equal(endsOn, subscription.EndsOn);
        return subscription;
    }

    private static ClinicSubscription OpenEnded()
    {
        var subscription = ClinicSubscription.For(ClinicId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.OpenEnded(
                ClinicId,
                SubscriptionPeriodKind.Grandfathered,
                new DateTime(2026, 1, 1),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        });

        Assert.Null(subscription.EndsOn);
        return subscription;
    }

    // ---- FR-1: the last working day ----------------------------------------------------------------

    // [AC-1.1][FR-1] On the end date itself the cabinet may still work, and the countdown reads 0 — NOT 1, and not
    // « expiré ». « 0 jour restant » is the honest way to say « today is your last day ».
    [Fact]
    public void On_The_End_Date_The_Cabinet_Still_Writes_And_The_Countdown_Reads_Zero()
    {
        var status = SubscriptionStateReader.Read(EndingOn(Today), Today);

        Assert.True(status.AllowsWrites);
        Assert.Equal(0, status.DaysRemaining);
        Assert.Equal(SubscriptionState.Active, status.State);
    }

    // [EC-1] Midnight passes mid-consultation: the day after the end date, writes are refused.
    [Fact]
    public void The_Day_After_The_End_Date_Writes_Are_Refused()
    {
        var status = SubscriptionStateReader.Read(EndingOn(Today.AddDays(-1)), Today);

        Assert.False(status.AllowsWrites);
        Assert.Equal(SubscriptionState.Expired, status.State);
    }

    // [FR-1] A negative countdown is never surfaced — « −12 jours restants » is not a thing to tell anybody, and a
    // client rendering it would have to special-case the sign itself.
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(400)]
    public void An_Expired_Cabinet_Surfaces_No_Countdown(int daysPast)
    {
        var status = SubscriptionStateReader.Read(EndingOn(Today.AddDays(-daysPast)), Today);

        Assert.Null(status.DaysRemaining);
        Assert.Equal(SubscriptionState.Expired, status.State);
    }

    // ---- AC-3.1: the warning window ----------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(30, false)]
    public void The_Warning_Starts_Seven_Days_Before_The_End(int daysRemaining, bool shouldWarn)
    {
        var status = SubscriptionStateReader.Read(EndingOn(Today.AddDays(daysRemaining)), Today);

        Assert.Equal(shouldWarn, status.ShouldWarn);
        Assert.Equal(daysRemaining, status.DaysRemaining);
    }

    // [AC-3.4] The four thresholds, and « largest reached » rather than « nearest » — a job that missed four days
    // must still produce the row for where the cabinet actually is, not for the threshold it slept through.
    [Theory]
    [InlineData(9, null)]
    [InlineData(8, null)]
    [InlineData(7, 7)]
    [InlineData(5, 7)]
    [InlineData(3, 3)]
    [InlineData(2, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void The_Threshold_Reached_Is_The_Largest_One_Passed(int daysRemaining, int? expected)
    {
        Assert.Equal(expected, SubscriptionStateReader.ThresholdReached(daysRemaining));
    }

    [Fact]
    public void An_Absent_Or_Negative_Countdown_Reaches_No_Threshold()
    {
        Assert.Null(SubscriptionStateReader.ThresholdReached(null));
        Assert.Null(SubscriptionStateReader.ThresholdReached(-1));
    }

    // ---- AC-2.5: no end date ----------------------------------------------------------------------

    // [AC-2.5][AC-6.3] A grandfathered cabinet is Active for ever, warned about nothing, with no countdown to
    // render — which is what lets the screen say so IN WORDS rather than printing a far-future date.
    [Fact]
    public void An_Entitlement_With_No_End_Date_Is_Active_For_Ever_And_Never_Warns()
    {
        var status = SubscriptionStateReader.Read(OpenEnded(), Today);

        Assert.Equal(SubscriptionState.Active, status.State);
        Assert.True(status.AllowsWrites);
        Assert.False(status.ShouldWarn);
        Assert.Null(status.DaysRemaining);
        Assert.Null(status.EndsOn);
    }

    // Ten years on, still Active — « no end date » must not decay into « expired » through some comparison against
    // a default.
    [Fact]
    public void An_Open_Ended_Entitlement_Is_Still_Active_Years_Later()
    {
        var status = SubscriptionStateReader.Read(OpenEnded(), Today.AddYears(10));

        Assert.Equal(SubscriptionState.Active, status.State);
        Assert.True(status.AllowsWrites);
    }

    // ---- EC-11: suspension outranks expiry --------------------------------------------------------

    // [EC-11] A suspended cabinet reads « Suspendu », NEVER « Expiré » — including when its date has also passed.
    // The two have different causes and different remedies, and telling a suspended practice its subscription
    // lapsed sends it to pay again for something a payment will not fix.
    [Fact]
    public void A_Suspended_Cabinet_Reads_Suspended_Even_When_Its_Date_Has_Also_Passed()
    {
        var subscription = Suspended(EndingOn(Today.AddDays(-30)));

        var status = SubscriptionStateReader.Read(subscription, Today);

        Assert.Equal(SubscriptionState.Suspended, status.State);
        Assert.False(status.AllowsWrites);
    }

    [Fact]
    public void A_Suspended_Cabinet_Reads_Suspended_Even_With_Time_Left()
    {
        var subscription = Suspended(EndingOn(Today.AddYears(1)));

        var status = SubscriptionStateReader.Read(subscription, Today);

        Assert.Equal(SubscriptionState.Suspended, status.State);
        Assert.False(status.AllowsWrites);
        // Warned, because a cabinet that cannot record work has to be told why.
        Assert.True(status.ShouldWarn);
    }

    // ---- AC-1.4: a trial is not a reduced product ------------------------------------------------

    // [AC-1.4] Trial changes the LABEL and nothing else: the same writes, the same warnings, the same countdown.
    // Anything gating on Trial would be making a trial a lesser product, which the spec forbids outright.
    [Fact]
    public void A_Trial_Differs_From_Active_Only_In_Its_Label()
    {
        var subscription = EndingOn(Today.AddDays(20));

        var asTrial = SubscriptionStateReader.Read(subscription, Today, isTrial: true);
        var asActive = SubscriptionStateReader.Read(subscription, Today);

        Assert.Equal(SubscriptionState.Trial, asTrial.State);
        Assert.Equal(SubscriptionState.Active, asActive.State);
        Assert.Equal(asActive with { State = SubscriptionState.Trial }, asTrial);
    }

    private static ClinicSubscription Suspended(ClinicSubscription subscription)
    {
        subscription.Suspend("Impayé depuis trois mois.", by: "job|subscription-suspend", whenUtc: NowUtc);
        return subscription;
    }

    // The mutator's own guards, since this is where suspension enters the model. The reason is mandatory because
    // EC-11 has the cabinet told « Suspendu » rather than « Expiré », so « suspended why? » must be answerable.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Suspending_Without_A_Reason_Is_Refused(string? reason)
    {
        var subscription = EndingOn(Today.AddDays(20));

        Assert.Throws<ArgumentException>(() => subscription.Suspend(reason!, by: null, whenUtc: NowUtc));
        Assert.False(subscription.IsSuspended);
    }

    // Lifting a suspension is NOT granting time: the cabinet falls back on its end date, which may still be past.
    [Fact]
    public void Unsuspending_Leaves_The_Cabinet_On_Its_Own_End_Date()
    {
        var subscription = Suspended(EndingOn(Today.AddDays(-5)));

        subscription.Unsuspend(NowUtc);

        var status = SubscriptionStateReader.Read(subscription, Today);
        Assert.Equal(SubscriptionState.Expired, status.State);
        Assert.Null(subscription.SuspensionReason);
        Assert.Null(subscription.SuspendedAtUtc);
    }

    private static readonly DateTime NowUtc = new(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);
}
