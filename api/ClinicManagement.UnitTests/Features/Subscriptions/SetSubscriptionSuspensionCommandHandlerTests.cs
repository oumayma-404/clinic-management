using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// Suspending and unsuspending a cabinet (<c>clinic-subscription</c> Part F — FR-7, EC-11).
///
/// <para><b>The load-bearing case is <see cref="Suspending_Does_Not_Touch_The_Ledger_And_Lifting_It_Grants_No_Time"/></b>,
/// because it pins the distinction the whole feature depends on: <b>non-payment is not suspension</b>. Non-payment is
/// the absence of a grant and expresses itself as expiry; suspension is for abuse or fraud. Conflate them — by having
/// a suspension consume ledger time, or an unsuspension restore it — and paying would appear to lift a suspension
/// while lifting one would appear to hand out days nobody paid for.</para>
///
/// <para>Its sibling is <see cref="A_Suspended_Cabinet_Reads_Suspendu_Even_With_A_Future_End_Date"/> (EC-11): the
/// state a suspended cabinet is shown must be « Suspendu », never « Expiré », because the two have different
/// remedies and telling a suspended practice its subscription lapsed sends it to pay for nothing.</para>
/// </summary>
public class SetSubscriptionSuspensionCommandHandlerTests
{
    private static readonly DateTime Today = ClinicClock.ClinicToday();

    private static SetSubscriptionSuspensionCommandHandler Handler(SubscriptionVendorHarness harness) =>
        new(harness.Subscriptions, harness.Clinics.Object, harness.Users.Object, harness.UnitOfWork.Object,
            NullLogger<SetSubscriptionSuspensionCommandHandler>.Instance);

    private static SetSubscriptionSuspensionCommand Suspend(string? reason = "Usage frauduleux signalé") =>
        new()
        {
            ClinicId = SubscriptionVendorHarness.ClinicId,
            Suspend = true,
            Reason = reason,
            ActedBy = "job|subscription-suspend",
        };

    private static SetSubscriptionSuspensionCommand Lift() =>
        new()
        {
            ClinicId = SubscriptionVendorHarness.ClinicId,
            Suspend = false,
            ActedBy = "job|subscription-unsuspend",
        };

    // [FR-7] A suspension records its motif and its actor, and stops the cabinet writing whatever its date says.
    [Fact]
    public async Task Suspending_Records_The_Motif_And_The_Actor()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = harness.GivenEntitlement(Today, durationMonths: 12);

        var result = await Handler(harness).Handle(Suspend(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsSuspended);
        Assert.True(subscription.IsSuspended);
        Assert.Equal("Usage frauduleux signalé", subscription.SuspensionReason);
        Assert.Equal("job|subscription-suspend", subscription.SuspendedBy);
        Assert.NotNull(subscription.SuspendedAtUtc);
        Assert.False(SubscriptionStateReader.Read(subscription, Today).AllowsWrites);
    }

    // [FR-7] The one that keeps « non-payment is not suspension » true: the ledger is untouched in both directions,
    // and the end date is exactly what it was. So paying does not lift a suspension, and lifting one grants no time.
    [Fact]
    public async Task Suspending_Does_Not_Touch_The_Ledger_And_Lifting_It_Grants_No_Time()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = harness.GivenEntitlement(Today, durationMonths: 12);
        var endsOn = subscription.EndsOn;
        var entriesBefore = harness.Subscriptions.Entries.Count;

        Assert.True((await Handler(harness).Handle(Suspend(), CancellationToken.None)).IsSuccess);
        Assert.Equal(endsOn, subscription.EndsOn);
        Assert.Equal(entriesBefore, harness.Subscriptions.Entries.Count);

        var lifted = await Handler(harness).Handle(Lift(), CancellationToken.None);

        Assert.True(lifted.IsSuccess);
        Assert.Equal(endsOn, lifted.Value!.EndsOn);
        Assert.Equal(entriesBefore, harness.Subscriptions.Entries.Count);
    }

    // [FR-7] Lifting clears the whole trail rather than leaving a stale motif behind — a cabinet that reads
    // « Actif » while still carrying « Usage frauduleux signalé » would be a contradiction on its own screen.
    [Fact]
    public async Task Lifting_A_Suspension_Clears_Its_Whole_Trail()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = harness.GivenEntitlement(Today, durationMonths: 12);
        Assert.True((await Handler(harness).Handle(Suspend(), CancellationToken.None)).IsSuccess);

        var result = await Handler(harness).Handle(Lift(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(subscription.IsSuspended);
        Assert.Null(subscription.SuspensionReason);
        Assert.Null(subscription.SuspendedBy);
        Assert.Null(subscription.SuspendedAtUtc);
    }

    // [EC-11] Suspension outranks a live end date. Reading « Expiré » here would send the practice to pay for
    // something a payment cannot unblock; reading « Actif » would hide that it has been stopped.
    [Fact]
    public async Task A_Suspended_Cabinet_Reads_Suspendu_Even_With_A_Future_End_Date()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = harness.GivenEntitlement(Today, durationMonths: 12);
        Assert.True(subscription.EndsOn > Today);

        Assert.True((await Handler(harness).Handle(Suspend(), CancellationToken.None)).IsSuccess);

        var status = SubscriptionStateReader.Read(subscription, Today);
        Assert.Equal(SubscriptionState.Suspended, status.State);
        Assert.Null(status.DaysRemaining);
    }

    // [FR-7] Lifting a suspension on a LAPSED cabinet leaves it read-only. Restoring writes here would be the same
    // conflation the ledger case above rules out, arriving from the other side.
    [Fact]
    public async Task Lifting_A_Suspension_On_A_Lapsed_Cabinet_Leaves_It_Read_Only()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = harness.GivenEntitlement(Today.AddDays(-200), durationMonths: 1);
        Assert.True((await Handler(harness).Handle(Suspend(), CancellationToken.None)).IsSuccess);

        var result = await Handler(harness).Handle(Lift(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var status = SubscriptionStateReader.Read(subscription, Today);
        Assert.Equal(SubscriptionState.Expired, status.State);
        Assert.False(status.AllowsWrites);
    }

    // [FR-7] The motif is mandatory when suspending, because the cabinet is told it is suspended rather than lapsed
    // and « why » must be answerable. Nothing is written on the refusal.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Suspending_Without_A_Motif_Is_Refused_And_Nothing_Is_Written(string? reason)
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = harness.GivenEntitlement(Today, durationMonths: 12);

        var result = await Handler(harness).Handle(Suspend(reason), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SetSubscriptionSuspensionCommandHandler.ReasonRequiredError, result.Error);
        Assert.False(subscription.IsSuspended);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Lifting needs no motif: an unsuspension has nothing to explain to the practice, which simply stops being told
    // it is suspended. Asserted so the symmetry is a decision rather than an oversight.
    [Fact]
    public async Task Lifting_A_Suspension_Needs_No_Motif()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 12);
        Assert.True((await Handler(harness).Handle(Suspend(), CancellationToken.None)).IsSuccess);

        Assert.True((await Handler(harness).Handle(Lift(), CancellationToken.None)).IsSuccess);
    }

    // [EC-6] Same distinct code as its two siblings.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Is_Refused_Under_The_Missing_Code()
    {
        var harness = new SubscriptionVendorHarness();

        var result = await Handler(harness).Handle(Suspend(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionRefusals.MissingCode, result.Code);
    }
}
