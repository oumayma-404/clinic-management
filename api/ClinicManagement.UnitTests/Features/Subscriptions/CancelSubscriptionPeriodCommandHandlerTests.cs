using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// Correcting a mis-keyed grant (<c>clinic-subscription</c> Part F — AC-5.4, AC-5.5, EC-4).
///
/// <para><b>The load-bearing case is <see cref="Cancelling_A_Middle_Entry_Moves_The_End_Date"/></b>, and it is the
/// one that justifies the ledger storing <i>durations</i> rather than windows. With absolute stored windows, voiding
/// anything but the latest entry changes no date at all — a cabinet with a mistaken 12-month grant followed by a
/// correct one would keep all 24 months, because the later window's end is still the maximum. AC-5.4 would then be
/// true only sometimes, and « sometimes » is invisible: the wrong cases look exactly like the right ones.</para>
///
/// <para>The sibling is <see cref="A_Cancellation_Can_Push_The_Date_Into_The_Past"/> (EC-4). The point of the whole
/// mechanism is that a grant recorded against the wrong practice can be taken back — including the ability to work,
/// which the cabinet then loses again. A correction that could only ever be neutral would not be one.</para>
/// </summary>
public class CancelSubscriptionPeriodCommandHandlerTests
{
    private static readonly DateTime Today = ClinicClock.ClinicToday();
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static CancelSubscriptionPeriodCommandHandler Handler(SubscriptionVendorHarness harness) =>
        new(harness.Subscriptions, harness.Clinics.Object, harness.Users.Object, harness.UnitOfWork.Object,
            NullLogger<CancelSubscriptionPeriodCommandHandler>.Instance);

    private static CancelSubscriptionPeriodCommand Cancel(Guid entryId, string? reason = "Mauvais cabinet") =>
        new()
        {
            ClinicId = SubscriptionVendorHarness.ClinicId,
            EntryId = entryId,
            Reason = reason ?? string.Empty,
        };

    /// <summary>
    /// Three consecutive grants on one cabinet, folded — so the middle one is genuinely in the middle rather than
    /// merely recorded second.
    /// </summary>
    private static (SubscriptionVendorHarness Harness, SubscriptionPeriod First, SubscriptionPeriod Middle,
        SubscriptionPeriod Last) GivenThreeGrants()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = ClinicSubscription.For(SubscriptionVendorHarness.ClinicId, BaseUtc);

        var first = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.ClinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc, durationMonths: 1));
        var middle = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.ClinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc.AddMinutes(1),
            durationMonths: 12));
        var last = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.ClinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc.AddMinutes(2),
            durationMonths: 1));

        subscription.RecomputeFrom(new[] { first, middle, last });
        harness.Subscriptions.Subscription = subscription;
        return (harness, first, middle, last);
    }

    // ---- AC-5.4: the fold, not the maximum -------------------------------------------------------

    // [AC-5.4] Voiding the MIDDLE entry removes exactly its months. An implementation that stored absolute windows,
    // or that kept the maximum end date, would leave this date untouched and pass every other test in this class.
    [Fact]
    public async Task Cancelling_A_Middle_Entry_Moves_The_End_Date()
    {
        var (harness, _, middle, _) = GivenThreeGrants();
        var endBefore = harness.Subscriptions.Subscription!.EndsOn;

        var result = await Handler(harness).Handle(Cancel(middle.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(endBefore, result.Value!.PreviousEndsOn);
        Assert.True(result.Value.EndsOn < endBefore, "Voiding a middle entry must shorten the cover.");

        // Roughly the twelve months that entry contributed, stated as a range rather than an exact date: AddMonths
        // clamps to the end of a shorter month, so an exact arithmetic expectation would be a second, subtly
        // different fold. The precise value is pinned against the real fold by the next case.
        var removed = (endBefore!.Value - result.Value.EndsOn!.Value).TotalDays;
        Assert.InRange(removed, 330, 400);
    }

    // [AC-5.4] And the resulting date is the fold over what remains — asserted against an independent fold, so the
    // handler cannot be subtracting a duration of its own accord.
    [Fact]
    public async Task The_Corrected_Date_Is_The_Fold_Over_The_Entries_That_Remain()
    {
        var (harness, _, middle, _) = GivenThreeGrants();

        var result = await Handler(harness).Handle(Cancel(middle.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            SubscriptionLedger.Fold(harness.Subscriptions.Entries.Select(e => e.ToLedgerEntry())),
            result.Value!.EndsOn);
    }

    // ---- AC-5.5: kept, never deleted, always with a reason ---------------------------------------

    // [AC-5.5] The row stays. Deleting it would make « what were we paid, and for what » unaskable rather than
    // merely unanswered, and the ledger is the only record of a payment the vendor received.
    [Fact]
    public async Task The_Cancelled_Entry_Is_Kept_And_Carries_Its_Reason_And_Actor()
    {
        var (harness, _, middle, _) = GivenThreeGrants();

        var result = await Handler(harness).Handle(
            new CancelSubscriptionPeriodCommand
            {
                ClinicId = SubscriptionVendorHarness.ClinicId,
                EntryId = middle.Id,
                Reason = "Paiement enregistré sur le mauvais cabinet",
                CancelledBy = "job|subscription-cancel",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, harness.Subscriptions.Entries.Count);
        Assert.True(middle.IsCancelled);
        Assert.Equal("Paiement enregistré sur le mauvais cabinet", middle.CancelReason);
        Assert.Equal("job|subscription-cancel", middle.CancelledBy);
        Assert.NotNull(middle.CancelledAtUtc);
    }

    // [AC-5.5] The motif is mandatory. The date can move into the past as a result, so « why is this cabinet
    // suddenly read-only » has to be answerable from the row itself.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Cancellation_Without_A_Reason_Is_Refused_And_Nothing_Is_Written(string? reason)
    {
        var (harness, _, middle, _) = GivenThreeGrants();

        var result = await Handler(harness).Handle(Cancel(middle.Id, reason), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelSubscriptionPeriodCommandHandler.ReasonRequiredError, result.Error);
        Assert.False(middle.IsCancelled);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- EC-4: the correction can cost the cabinet its ability to work ---------------------------

    // [EC-4] A cabinet whose only cover was the mistaken grant goes back to being read-only, with a date in the
    // past. That is the correct outcome, not an edge case to be softened.
    [Fact]
    public async Task A_Cancellation_Can_Push_The_Date_Into_The_Past()
    {
        var harness = new SubscriptionVendorHarness();
        var subscription = ClinicSubscription.For(SubscriptionVendorHarness.ClinicId, BaseUtc);

        var lapsed = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.ClinicId, SubscriptionPeriodKind.Trial, Today.AddDays(-200), BaseUtc,
            durationDays: 30));
        var mistake = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.ClinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc.AddMinutes(1),
            durationMonths: 12));

        subscription.RecomputeFrom(new[] { lapsed, mistake });
        harness.Subscriptions.Subscription = subscription;
        Assert.True(subscription.EndsOn > Today);

        var result = await Handler(harness).Handle(Cancel(mistake.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EndsOn < Today, "Cancelling the only live grant must hand the date back.");
        Assert.Equal(SubscriptionState.Expired, SubscriptionStateReader.Read(subscription, Today).State);
    }

    // ---- Refusals -------------------------------------------------------------------------------

    // An entry of ANOTHER cabinet is structurally unreachable: the handler looks inside this cabinet's own ledger
    // rather than fetching by id, so there is no cross-tenant read to get wrong.
    [Fact]
    public async Task An_Entry_Of_Another_Cabinet_Is_Not_Found_Rather_Than_Cancelled()
    {
        var (harness, _, _, _) = GivenThreeGrants();
        var foreign = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.OtherClinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc,
            durationMonths: 12));

        var result = await Handler(harness).Handle(Cancel(foreign.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(foreign.Id.ToString(), result.Error);
        Assert.False(foreign.IsCancelled);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Cancelling twice is refused rather than throwing out of the aggregate — the entity's own guard would surface
    // as a generic French error, and « déjà annulée » is a statement the operator can act on.
    [Fact]
    public async Task An_Already_Cancelled_Entry_Is_Refused()
    {
        var (harness, _, middle, _) = GivenThreeGrants();
        Assert.True((await Handler(harness).Handle(Cancel(middle.Id), CancellationToken.None)).IsSuccess);

        var again = await Handler(harness).Handle(Cancel(middle.Id), CancellationToken.None);

        Assert.True(again.IsFailure);
        Assert.Contains("déjà annulée", again.Error);
    }

    // No entry id at all is its own refusal, for the same reason as the grant's missing-cabinet case.
    [Fact]
    public async Task Naming_No_Entry_Is_Refused_With_The_Usage_Sentence()
    {
        var (harness, _, _, _) = GivenThreeGrants();

        var result = await Handler(harness).Handle(Cancel(Guid.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelSubscriptionPeriodCommandHandler.EntryRequiredError, result.Error);
    }

    // [EC-6] Same distinct code as the grant's: no entitlement row is a fault, not a lapse.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Is_Refused_Under_The_Missing_Code()
    {
        var harness = new SubscriptionVendorHarness();

        var result = await Handler(harness).Handle(Cancel(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionRefusals.MissingCode, result.Code);
    }
}
