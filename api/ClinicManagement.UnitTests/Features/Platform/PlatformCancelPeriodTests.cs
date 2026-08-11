using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console corrects a mistake (<c>platform-console</c> US-5): one ledger entry is cancelled with a written
/// reason, and the cabinet's end date recomputes — possibly into the past.
///
/// <para><b>It runs the real command over the companion's own in-memory ledger</b>
/// (<c>SubscriptionVendorHarness</c>), like <c>PlatformRecordPaymentTests</c> beside it, because every AC here is
/// about what the ledger ends up holding: a row <i>kept</i> and struck through (AC-5.2), a date that moved
/// (AC-5.3). A mocked repository would prove a method was called and nothing about either.</para>
///
/// <para><b>The load-bearing case is
/// <see cref="The_Preview_On_The_Fiche_Is_Exactly_What_Cancelling_Then_Does"/>.</b> AC-5.3 requires the confirmation
/// to state the resulting date <i>before</i> the vendor commits, and the failure mode of a preview is silent: any
/// plausible-looking date passes review, the vendor confirms it, and the write produces a different one — so the
/// practice is told one thing and given another. It runs the real detail query and the real command over <b>one</b>
/// ledger and compares their two answers with each other, never with a retyped literal, which is what makes the two
/// independent paths hold each other rather than both agreeing with a mistake.</para>
///
/// <para>⚠️ Fixtures anchor on <c>ClinicClock.ClinicToday()</c>, as the vendor command tests and
/// <c>PlatformRecordPaymentTests</c> do and the opposite of <c>SubscriptionGateMiddlewareTests</c>: the property
/// under test is « where does this cabinet stand <i>today</i> », so a fixture decades away has no covering entry at
/// all and EC-7 ceases to exist.</para>
/// </summary>
public class PlatformCancelPeriodTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";
    private const string ClinicName = "Cabinet Ben Ali";

    private readonly SubscriptionVendorHarness _harness = new();
    private readonly FakeAccessLedger _ledger = new();
    private readonly Mock<IClinicActivityRepository> _activity = new();

    public PlatformCancelPeriodTests()
    {
        _harness.Clinics
            .Setup(c => c.GetByIdAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(SubscriptionVendorHarness.ClinicId, ClinicName, city: "Tunis"));
    }

    // ------------------------------------------------------------------ harness

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private CancelSubscriptionPeriodFromConsoleCommandHandler Handler(
        IPlatformSessionContext? session = null, ITenantScope? scope = null) =>
        new(_harness.Clinics.Object, _harness.Users.Object, _harness.Subscriptions, _ledger,
            session ?? new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<CancelSubscriptionPeriodFromConsoleCommandHandler>.Instance);

    private static CancelSubscriptionPeriodFromConsoleCommand Cancel(
        Guid entryId, string reason = "Paiement enregistré sur le mauvais cabinet") =>
        new()
        {
            ClinicId = SubscriptionVendorHarness.ClinicId,
            EntryId = entryId,
            Reason = reason,
        };

    /// <summary>
    /// EC-7's own fixture: a cabinet whose free days ran out a month ago and which has been working since on a grant
    /// recorded three weeks ago. Cancelling that grant is the case the AC is written about.
    ///
    /// <para>⚠️ <b>The trial entry is not decoration.</b> Every cabinet is provisioned with an opening entry (FR-13),
    /// and it is what the fold falls back to — a ledger holding <i>only</i> the cancelled grant folds to no date at
    /// all, i.e. « sans échéance », which is a different and much rarer state.</para>
    /// </summary>
    private (ClinicSubscription Subscription, SubscriptionPeriod Trial, SubscriptionPeriod Grant) GivenTrialThenGrant()
    {
        var clinicId = SubscriptionVendorHarness.ClinicId;
        var today = ClinicClock.ClinicToday();
        var now = DateTime.UtcNow;

        var subscription = ClinicSubscription.For(clinicId, now);

        var trial = _harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Trial, today.AddDays(-60), now.AddDays(-60), durationDays: 30));

        var grant = _harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Paid, today.AddDays(-21), now.AddDays(-21),
            durationMonths: 12, amount: 1_200.000m, method: SubscriptionPaymentMethod.Transfer));

        subscription.RecomputeFrom(new[] { trial, grant });
        _harness.Subscriptions.Subscription = subscription;

        return (subscription, trial, grant);
    }

    // ------------------------------------------------------------------ AC-5.1 the motif

    // [AC-5.1] The motif is mandatory, refused in French, and nothing at all happens: the entry is untouched and no
    // access row is written. A cancellation that half-applied would be worse than one refused.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Blank_Motif_Is_Refused_And_Changes_Nothing(string reason)
    {
        var (_, _, grant) = GivenTrialThenGrant();

        var result = await Handler().Handle(Cancel(grant.Id, reason), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelSubscriptionPeriodFromConsoleCommandHandler.ReasonRequiredError, result.Error);
        Assert.False(grant.IsCancelled);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ AC-5.2 the entry is kept

    // [AC-5.2] Never edited and never deleted: the row stays in the ledger, struck through, carrying its motif, its
    // canceller and the moment. This is the assertion a « delete the row » implementation fails.
    [Fact]
    public async Task The_Entry_Is_Kept_And_Carries_Its_Motif_Its_Canceller_And_Its_Moment()
    {
        var (_, _, grant) = GivenTrialThenGrant();
        var entriesBefore = _harness.Subscriptions.Entries.Count;

        var result = await Handler().Handle(
            Cancel(grant.Id, "Encaissé pour le cabinet voisin"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(entriesBefore, _harness.Subscriptions.Entries.Count);

        var kept = _harness.Subscriptions.Entries.Single(e => e.Id == grant.Id);
        Assert.True(kept.IsCancelled);
        Assert.Equal("Encaissé pour le cabinet voisin", kept.CancelReason);
        Assert.NotNull(kept.CancelledAtUtc);
        // The console account, through AuditActor's own constant — never a retyped literal, and never a clinic user
        // id, which the counter pass's AC-2.2 exclusion would then fail to recognise.
        Assert.Equal(AuditActor.Console(AccountId).UserId, kept.CancelledBy);
        Assert.StartsWith(AuditActor.ConsolePrefix, kept.CancelledBy!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ AC-5.3 / EC-7 the date

    // [AC-5.3] The console computes NO date. What it reports must be exactly what re-folding the remaining entries
    // yields — asserted against the real fold rather than a retyped literal, which would be a second copy of the
    // arithmetic and could agree with a mistake.
    [Fact]
    public async Task The_New_End_Date_Is_The_Ledgers_Own_Fold()
    {
        var (_, _, grant) = GivenTrialThenGrant();

        var result = await Handler().Handle(Cancel(grant.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            SubscriptionLedger.Fold(_harness.Subscriptions.Entries.Select(e => e.ToLedgerEntry())),
            result.Value!.EndsOn);
    }

    // [EC-7] Cancelling a grant the cabinet has been working on puts it back into read-only, and the answer says so
    // — the date moves INTO THE PAST and `MakesReadOnly` is true. `PreviousEndsOn` is what makes that legible.
    [Fact]
    public async Task Cancelling_A_Three_Week_Old_Grant_Puts_The_Cabinet_Back_Into_Read_Only()
    {
        var (subscription, _, grant) = GivenTrialThenGrant();
        var endBefore = subscription.EndsOn!.Value;
        var today = ClinicClock.ClinicToday();

        Assert.True(endBefore > today, "the fixture must start with a cabinet that may work");

        var result = await Handler().Handle(Cancel(grant.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(endBefore, result.Value!.PreviousEndsOn);
        Assert.True(result.Value.EndsOn < today, "the trial ran out before today, so the date moves into the past");
        Assert.True(result.Value.MakesReadOnly);
        Assert.Equal(nameof(SubscriptionState.Expired), result.Value.State);
    }

    // ------------------------------------------------------------------ the preview (AC-5.3, before committing)

    // [AC-5.3][EC-7] THE load-bearing case. The fiche's own preview and the write's own answer are produced by two
    // independent paths over ONE ledger, and they must agree — because a preview's failure mode is silent: any
    // plausible date passes review, the vendor confirms it, and the practice is then given a different one.
    [Fact]
    public async Task The_Preview_On_The_Fiche_Is_Exactly_What_Cancelling_Then_Does()
    {
        var (_, _, grant) = GivenTrialThenGrant();
        WireCabinet();

        var detail = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        Assert.True(detail.IsSuccess);
        var preview = detail.Value!.Payments.Single(p => p.EntryId == grant.Id).IfCancelled;
        Assert.NotNull(preview);

        var cancelled = await Handler().Handle(Cancel(grant.Id), CancellationToken.None);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(preview!.EndsOn, cancelled.Value!.EndsOn);
        Assert.Equal(preview.MakesReadOnly, cancelled.Value.MakesReadOnly);
        Assert.Equal(preview.State, cancelled.Value.State);
        Assert.Equal(preview.StateLabel, cancelled.Value.StateLabel);
    }

    // The preview is offered on live entries only. A cancelled row has nothing left to cancel, and a « what would
    // happen » figure beside it would invite a control that can only be refused.
    [Fact]
    public async Task An_Already_Cancelled_Entry_Carries_No_Preview()
    {
        var (subscription, _, grant) = GivenTrialThenGrant();
        grant.Cancel("Doublon", "console|op", DateTime.UtcNow);
        subscription.RecomputeFrom(_harness.Subscriptions.Entries);
        WireCabinet();

        var detail = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        var entry = detail.Value!.Payments.Single(p => p.EntryId == grant.Id);
        Assert.True(entry.IsCancelled);
        Assert.Null(entry.IfCancelled);
    }

    // [EC-11] A suspended cabinet is already read-only, so the preview must not let the screen credit the
    // cancellation with a consequence it did not cause — `State` stays `Suspended`, which is what the dialog reads to
    // say « cette annulation n'y change rien ». Suspension outranks expiry throughout the product.
    [Fact]
    public async Task A_Suspended_Cabinets_Preview_Stays_Suspended()
    {
        var (subscription, _, grant) = GivenTrialThenGrant();
        subscription.Suspend("Usage abusif", "console|op", DateTime.UtcNow);
        WireCabinet(isSuspended: true);

        var detail = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        var preview = detail.Value!.Payments.Single(p => p.EntryId == grant.Id).IfCancelled;
        Assert.NotNull(preview);
        Assert.Equal(nameof(SubscriptionState.Suspended), preview!.State);
        Assert.True(preview.MakesReadOnly);
    }

    // ------------------------------------------------------------------ AC-7.3 the access ledger

    // [AC-7.3] The correction is recorded in the console's own ledger, naming who, which cabinet, which entry and
    // when — and it is staged in the SAME save as the cancellation, so a correction with no ledger row behind it is
    // not a state this command can produce.
    [Fact]
    public async Task The_Cancellation_Is_Recorded_In_The_Access_Ledger()
    {
        var (_, _, grant) = GivenTrialThenGrant();

        var result = await Handler().Handle(Cancel(grant.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(_ledger.Rows);
        Assert.Equal(PlatformAccessAction.CancelledPeriod, row.Action);
        Assert.Equal(AccountId, row.PlatformAccountId);
        Assert.Equal(AccountEmail, row.AccountEmail);
        Assert.Equal(ClinicName, row.ClinicName);
        Assert.Equal(grant.Id, row.SubscriptionPeriodId);
    }

    // [DEV-8] The new action has French wording. `PlatformAccessLabels` falls through to the CLR name for an unmapped
    // member, so a member added without its label degrades silently into « CancelledPeriod » on a French screen —
    // which is exactly what this asserts is not the case.
    [Fact]
    public void The_New_Action_Has_A_French_Label()
    {
        var label = PlatformAccessLabels.Action(PlatformAccessAction.CancelledPeriod);

        Assert.NotEqual(nameof(PlatformAccessAction.CancelledPeriod), label);
        Assert.Equal("Période annulée", label);
    }

    // [AC-7.3] An unattributable correction must not aboutir — the read path's rule applied to a write, and checked
    // BEFORE the entry is touched, because `CancelledBy` lands on a row nobody can edit afterwards.
    [Fact]
    public async Task An_Unattributable_Cancellation_Does_Not_Aboutir()
    {
        var (_, _, grant) = GivenTrialThenGrant();

        var handler = Handler(session: new FakePlatformSession { AccountId = null, Email = null });

        var result = await handler.Handle(Cancel(grant.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(grant.IsCancelled);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ refusals

    // An entry already struck through is a state of the world, not a rejected request — somebody else cancelled it,
    // and its motif and author are on the fiche. It carries a CODE so the dialog can say so and re-read, rather than
    // recovering the outcome by matching the French sentence.
    [Fact]
    public async Task An_Already_Cancelled_Entry_Is_Refused_With_Its_Own_Code()
    {
        var (_, _, grant) = GivenTrialThenGrant();
        grant.Cancel("Première annulation", "console|op", DateTime.UtcNow);

        var result = await Handler().Handle(Cancel(grant.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelSubscriptionPeriodFromConsoleCommandHandler.AlreadyCancelledCode, result.Code);
        Assert.Equal("Première annulation", grant.CancelReason);
        Assert.Empty(_ledger.Rows);
    }

    // An unknown entry is refused under a code, so a stale fiche whose entry has since gone renders a French state
    // rather than a generic error.
    [Fact]
    public async Task An_Unknown_Entry_Is_Refused_With_A_Code()
    {
        GivenTrialThenGrant();

        var result = await Handler().Handle(
            Cancel(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")), CancellationToken.None);

        Assert.Equal(CancelSubscriptionPeriodFromConsoleCommandHandler.UnknownEntryCode, result.Code);
        Assert.Empty(_ledger.Rows);
    }

    // The entry is located within the CABINET'S OWN ledger, so another practice's entry is structurally unreachable
    // rather than checked for — the companion's own rule, and the reason a mistyped cabinet id cannot shorten the
    // wrong practice's cover.
    [Fact]
    public async Task Another_Cabinets_Entry_Cannot_Be_Cancelled_Through_This_Cabinet()
    {
        GivenTrialThenGrant();

        var foreign = _harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            SubscriptionVendorHarness.OtherClinicId, SubscriptionPeriodKind.Paid,
            ClinicClock.ClinicToday(), DateTime.UtcNow, durationMonths: 12));

        var result = await Handler().Handle(Cancel(foreign.Id), CancellationToken.None);

        Assert.Equal(CancelSubscriptionPeriodFromConsoleCommandHandler.UnknownEntryCode, result.Code);
        Assert.False(foreign.IsCancelled);
    }

    // [EC-12] A write reached with no cross-cabinet scope declared THROWS rather than reading zero rows and
    // reporting « cette période ne figure pas dans le journal » — the guard every console path carries.
    [Fact]
    public async Task A_Cancellation_Without_A_Declared_Scope_Refuses_Instead_Of_Reading_Nothing()
    {
        var (_, _, grant) = GivenTrialThenGrant();

        var handler = Handler(scope: new TenantScope(NullLogger<TenantScope>.Instance));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(Cancel(grant.Id), CancellationToken.None));
    }

    // ------------------------------------------------------------------ the detail read, for the preview cases

    private GetPlatformClinicDetailQueryHandler DetailHandler() =>
        new(_activity.Object, _harness.Users.Object, _harness.Subscriptions, _ledger,
            new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _harness.UnitOfWork.Object, SystemWideScope(),
            NullLogger<GetPlatformClinicDetailQueryHandler>.Instance);

    private void WireCabinet(bool isSuspended = false)
    {
        var clinicId = SubscriptionVendorHarness.ClinicId;

        _activity.Setup(r => r.GetClinicRowAsync(clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformClinicRow(
                clinicId, ClinicName, "Tunis", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                HasEntitlement: true,
                Plan: SubscriptionPlan.Cabinet,
                SubscriptionEndsOn: _harness.Subscriptions.Subscription!.EndsOn,
                SubscriptionIsSuspended: isSuspended,
                LatestCoverKind: SubscriptionPeriodKind.Paid,
                Users: 3, Patients: 412, Appointments30d: 96, Writes7d: 4, Writes30d: 12, ActiveDays30d: 9,
                LastWriteAt: null, LastLoginAt: null,
                CollectedThisMonth: 0m,
                CountersComputedAt: new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)));

        _activity.Setup(r => r.GetDaysAsync(
                clinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ClinicActivityDay>());

        _harness.Users.Setup(r => r.GetPrimaryAdminContactAsync(clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicAdminContact("Salma Ben Ali", "salma@cabinet.tn", IsActive: true));
    }
}
