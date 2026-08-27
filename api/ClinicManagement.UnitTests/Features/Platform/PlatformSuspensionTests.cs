using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console suspends a cabinet for abuse, and lifts that suspension (<c>platform-console</c> US-6).
///
/// <para><b>It runs the real command over the companion's own in-memory entitlement</b>
/// (<c>SubscriptionVendorHarness</c>), like <c>PlatformCancelPeriodTests</c> beside it, because the ACs here are about
/// what the entitlement ends up holding — a motif, an author, a moment (AC-6.1) — and about a date that must
/// <i>not</i> have moved (AC-6.4). A mocked repository would prove a method was called and nothing about either.</para>
///
/// <para><b>The load-bearing case is
/// <see cref="Lifting_A_Suspension_Off_A_Lapsed_Cabinet_Leaves_It_Read_Only_For_Expiry"/>.</b> It is the one that
/// fails if the handler reports the outcome it intended instead of reading the state rule: every other assertion
/// still passes while the console tells the vendor a practice can work again when it cannot. The mirror,
/// <see cref="Suspension_Consumes_No_Paid_Day_And_Lifting_Restores_The_Same_Date"/>, is AC-6.4 itself — and it is the
/// assertion an implementation that « restored » the entitlement by re-granting time would fail.</para>
///
/// <para>⚠️ Fixtures anchor on <c>ClinicClock.ClinicToday()</c>, as the vendor command tests and
/// <c>PlatformCancelPeriodTests</c> do and the opposite of <c>SubscriptionGateMiddlewareTests</c>: the property under
/// test is « may this cabinet work <i>today</i> », so a fixture decades away collapses every case into « expiré ».</para>
/// </summary>
public class PlatformSuspensionTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";
    private const string ClinicName = "Cabinet Ben Ali";
    private const string Motif = "Facturation frauduleuse signalée par un patient";

    private readonly SubscriptionVendorHarness _harness = new();
    private readonly FakeAccessLedger _ledger = new();
    private readonly Mock<IClinicActivityRepository> _activity = new();

    public PlatformSuspensionTests()
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

    private SetClinicSuspensionFromConsoleCommandHandler Handler(
        IPlatformSessionContext? session = null, ITenantScope? scope = null) =>
        new(_harness.Clinics.Object, _harness.Users.Object, _harness.Subscriptions, _ledger,
            session ?? new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<SetClinicSuspensionFromConsoleCommandHandler>.Instance);

    private static SetClinicSuspensionFromConsoleCommand Suspend(string reason = Motif) =>
        new() { ClinicId = SubscriptionVendorHarness.ClinicId, Suspend = true, Reason = reason };

    private static SetClinicSuspensionFromConsoleCommand Lift() =>
        new() { ClinicId = SubscriptionVendorHarness.ClinicId, Suspend = false };

    /// <summary>A cabinet working on a grant that runs for another eleven months — the ordinary target of a
    /// suspension, and the fixture AC-6.4 is measured against.</summary>
    private ClinicSubscription GivenACabinetThatMayWork()
    {
        var clinicId = SubscriptionVendorHarness.ClinicId;
        var today = ClinicClock.ClinicToday();
        var now = DateTime.UtcNow;

        var subscription = ClinicSubscription.For(clinicId, now);
        var grant = _harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Paid, today.AddDays(-21), now.AddDays(-21),
            durationMonths: 12, amount: 1_200.000m, method: SubscriptionPaymentMethod.Transfer));

        subscription.RecomputeFrom(new[] { grant }, now);
        _harness.Subscriptions.Subscription = subscription;
        return subscription;
    }

    /// <summary>
    /// A cabinet whose free days ran out a month ago and which has paid nothing since — suspended anyway, because
    /// suspension is about conduct and not about payment (AC-6.3).
    /// </summary>
    private ClinicSubscription GivenALapsedCabinet()
    {
        var clinicId = SubscriptionVendorHarness.ClinicId;
        var today = ClinicClock.ClinicToday();
        var now = DateTime.UtcNow;

        var subscription = ClinicSubscription.For(clinicId, now);
        var trial = _harness.Subscriptions.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Trial, today.AddDays(-60), now.AddDays(-60), durationDays: 30));

        subscription.RecomputeFrom(new[] { trial }, now);
        _harness.Subscriptions.Subscription = subscription;
        return subscription;
    }

    // ------------------------------------------------------------------ AC-6.1 the motif

    // [AC-6.1] The motif is mandatory, refused in French, and nothing at all happens — the cabinet is not suspended
    // and no access row is written. « Suspendu » with no answer to « pourquoi ? » is the state this AC exists to
    // prevent, so a suspension that half-applied would be worse than one refused.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Blank_Motif_Is_Refused_And_Changes_Nothing(string reason)
    {
        var subscription = GivenACabinetThatMayWork();

        var result = await Handler().Handle(Suspend(reason), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SetClinicSuspensionFromConsoleCommandHandler.ReasonRequiredError, result.Error);
        Assert.False(subscription.IsSuspended);
        Assert.Empty(_ledger.Rows);
    }

    // [AC-6.1] The motif, its author and its moment land on the entitlement — and the author is the CONSOLE account
    // through AuditActor's own constant, never a retyped literal and never a clinic user id, which the counter pass's
    // AC-2.2 exclusion would then fail to recognise (suspending a dormant cabinet would make it read as active).
    [Fact]
    public async Task Suspending_Records_The_Motif_Its_Console_Author_And_Its_Moment()
    {
        var subscription = GivenACabinetThatMayWork();

        var result = await Handler().Handle(Suspend(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(subscription.IsSuspended);
        Assert.Equal(Motif, subscription.SuspensionReason);
        Assert.NotNull(subscription.SuspendedAtUtc);
        Assert.Equal(AuditActor.Console(AccountId).UserId, subscription.SuspendedBy);
        Assert.StartsWith(AuditActor.ConsolePrefix, subscription.SuspendedBy!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ AC-6.2 / AC-6.3 suspended ≠ expired

    // [AC-6.2][EC-11] A suspended cabinet is read-only and reads « Suspendu » — even while its cover is perfectly
    // valid, which is the case a naive « expired means read-only » implementation gets wrong in the other direction.
    [Fact]
    public async Task A_Suspended_Cabinet_Is_Read_Only_And_Reads_Suspended_Not_Expired()
    {
        GivenACabinetThatMayWork();

        var result = await Handler().Handle(Suspend(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsSuspended);
        Assert.True(result.Value.MakesReadOnly);
        Assert.Equal(nameof(SubscriptionState.Suspended), result.Value.State);
        Assert.NotEqual(nameof(SubscriptionState.Expired), result.Value.State);
        // EC-11: no countdown for a suspended cabinet — « se termine dans 11 mois » would invite a payment that
        // unblocks nothing.
        Assert.Null(result.Value.DaysRemaining);
    }

    // [AC-6.2][AC-6.3] And a cabinet that is BOTH suspended and lapsed still reads « Suspendu ». Suspension outranks
    // expiry, because a payment would not lift it and telling the practice otherwise sends it to pay for nothing.
    [Fact]
    public async Task A_Cabinet_That_Is_Both_Suspended_And_Lapsed_Still_Reads_Suspended()
    {
        GivenALapsedCabinet();

        var result = await Handler().Handle(Suspend(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(SubscriptionState.Suspended), result.Value!.State);
        Assert.True(result.Value.EndsOn < ClinicClock.ClinicToday(), "the fixture's cover ran out a month ago");
    }

    // [AC-6.3] The two new journal actions have French wording. `PlatformAccessLabels` falls through to the CLR name
    // for an unmapped member, so a member added without its label degrades silently into « Suspended » on a French
    // screen — and « Cabinet suspendu » rather than « Abonnement suspendu », since this is not a payment state.
    [Fact]
    public void The_Two_New_Actions_Have_French_Labels_That_Do_Not_Mention_Payment()
    {
        var suspended = PlatformAccessLabels.Action(PlatformAccessAction.Suspended);
        var lifted = PlatformAccessLabels.Action(PlatformAccessAction.Unsuspended);

        Assert.NotEqual(nameof(PlatformAccessAction.Suspended), suspended);
        Assert.NotEqual(nameof(PlatformAccessAction.Unsuspended), lifted);
        Assert.Equal("Cabinet suspendu", suspended);
        Assert.Equal("Suspension levée", lifted);
        Assert.DoesNotContain("bonnement", suspended, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aiement", suspended, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ AC-6.4 no paid day is lost

    // [AC-6.4] THE AC. Suspension consumes no paid day and lifting restores exactly the entitlement the cabinet had —
    // asserted as the date being the SAME OBJECT VALUE across all three moments, never as « a date in the future ».
    // An implementation that « restored » cover by granting time back would pass a looser assertion and would be
    // quietly giving away months.
    [Fact]
    public async Task Suspension_Consumes_No_Paid_Day_And_Lifting_Restores_The_Same_Date()
    {
        var subscription = GivenACabinetThatMayWork();
        var endBefore = subscription.EndsOn;
        var entriesBefore = _harness.Subscriptions.Entries.Count;

        var suspended = await Handler().Handle(Suspend(), CancellationToken.None);
        Assert.True(suspended.IsSuccess);
        Assert.Equal(endBefore, subscription.EndsOn);

        var lifted = await Handler().Handle(Lift(), CancellationToken.None);

        Assert.True(lifted.IsSuccess);
        Assert.False(lifted.Value!.IsSuspended);
        Assert.Equal(endBefore, lifted.Value.EndsOn);
        Assert.Equal(endBefore, subscription.EndsOn);
        Assert.False(lifted.Value.MakesReadOnly);
        // The ledger is untouched in both directions: no entry added, none cancelled. That is what makes AC-6.4 a
        // property of the design rather than of a restore step somebody has to remember to write.
        Assert.Equal(entriesBefore, _harness.Subscriptions.Entries.Count);
        Assert.DoesNotContain(_harness.Subscriptions.Entries, e => e.IsCancelled);
    }

    // [AC-6.4] The load-bearing case. Lifting a suspension off a cabinet whose cover lapsed while it was stopped
    // leaves it read-only — for EXPIRY, which the answer names. A handler that reported the outcome it intended
    // (« la suspension est levée, donc le cabinet travaille ») would pass every other test in this file and tell the
    // vendor a practice can work when the next save it attempts will be refused.
    [Fact]
    public async Task Lifting_A_Suspension_Off_A_Lapsed_Cabinet_Leaves_It_Read_Only_For_Expiry()
    {
        GivenALapsedCabinet();
        Assert.True((await Handler().Handle(Suspend(), CancellationToken.None)).IsSuccess);

        var lifted = await Handler().Handle(Lift(), CancellationToken.None);

        Assert.True(lifted.IsSuccess);
        Assert.False(lifted.Value!.IsSuspended);
        Assert.True(lifted.Value.MakesReadOnly);
        Assert.Equal(nameof(SubscriptionState.Expired), lifted.Value.State);
    }

    // ------------------------------------------------------------------ AC-7.3 the access ledger

    // [AC-7.3] Both directions are recorded in the console's own ledger, naming who, which cabinet and when — staged
    // in the SAME save as the write, so a suspension with no ledger row behind it is not a state this command can
    // produce. ⚠️ Neither row names a `SubscriptionPeriodId`: suspension touches no entry, and pointing at one would
    // be a claim that a payment was involved (AC-6.3).
    [Fact]
    public async Task Both_Directions_Are_Recorded_In_The_Access_Ledger_And_Name_No_Period()
    {
        GivenACabinetThatMayWork();

        Assert.True((await Handler().Handle(Suspend(), CancellationToken.None)).IsSuccess);
        Assert.True((await Handler().Handle(Lift(), CancellationToken.None)).IsSuccess);

        Assert.Collection(
            _ledger.Rows,
            row =>
            {
                Assert.Equal(PlatformAccessAction.Suspended, row.Action);
                Assert.Equal(AccountId, row.PlatformAccountId);
                Assert.Equal(AccountEmail, row.AccountEmail);
                Assert.Equal(ClinicName, row.ClinicName);
                Assert.Null(row.SubscriptionPeriodId);
            },
            row =>
            {
                Assert.Equal(PlatformAccessAction.Unsuspended, row.Action);
                Assert.Null(row.SubscriptionPeriodId);
            });
    }

    // [AC-7.3] An unattributable suspension must not aboutir — the read path's rule applied to a write, checked
    // BEFORE the entitlement is touched, because `SuspendedBy` is what the cabinet is judged by afterwards.
    [Fact]
    public async Task An_Unattributable_Suspension_Does_Not_Aboutir()
    {
        var subscription = GivenACabinetThatMayWork();

        var handler = Handler(session: new FakePlatformSession { AccountId = null, Email = null });

        var result = await handler.Handle(Suspend(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(subscription.IsSuspended);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ refusals

    // Re-suspending is a refusal, not a re-statement: the entitlement holds exactly one motif, one author and one
    // moment, so a second `Suspend` would overwrite a colleague's reasoning with no trace of it anywhere. The first
    // motif and its author survive, which is the assertion an « idempotent » implementation fails.
    [Fact]
    public async Task Re_Suspending_Is_Refused_And_Keeps_The_First_Motif_And_Author()
    {
        var subscription = GivenACabinetThatMayWork();
        Assert.True((await Handler().Handle(Suspend("Premier motif"), CancellationToken.None)).IsSuccess);
        var suspendedAt = subscription.SuspendedAtUtc;

        var result = await Handler().Handle(Suspend("Second motif"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SetClinicSuspensionFromConsoleCommandHandler.AlreadySuspendedCode, result.Code);
        Assert.Equal("Premier motif", subscription.SuspensionReason);
        Assert.Equal(suspendedAt, subscription.SuspendedAtUtc);
        Assert.Single(_ledger.Rows);
    }

    // Lifting a cabinet that is not suspended is refused rather than answered « c'est fait » — `Unsuspend` would clear
    // nothing, so a silent success would write an `Unsuspended` journal row for an action that never happened, and on
    // the fiche it would read as having released a read-only cabinet whose real problem is its end date. The refusal
    // says exactly that.
    [Fact]
    public async Task Lifting_A_Cabinet_That_Is_Not_Suspended_Is_Refused_With_Its_Own_Code()
    {
        GivenALapsedCabinet();

        var result = await Handler().Handle(Lift(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SetClinicSuspensionFromConsoleCommandHandler.NotSuspendedCode, result.Code);
        Assert.Empty(_ledger.Rows);
    }

    // An unknown cabinet is refused under a CODE the console branches on, so a stale portfolio whose cabinet has
    // since gone renders EC-13's own French state rather than a generic error.
    [Fact]
    public async Task An_Unknown_Cabinet_Is_Refused_With_A_Code()
    {
        var result = await Handler().Handle(
            new SetClinicSuspensionFromConsoleCommand
            {
                ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Suspend = true,
                Reason = Motif,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SetClinicSuspensionFromConsoleCommandHandler.UnknownClinicCode, result.Code);
        Assert.Empty(_ledger.Rows);
    }

    // [EC-12] A write reached with no cross-cabinet scope declared THROWS rather than reading zero rows and reporting
    // « cabinet introuvable » — the guard every console path carries, and the one whose absence is invisible because
    // the broken version *succeeds*.
    [Fact]
    public async Task A_Suspension_Without_A_Declared_Scope_Refuses_Instead_Of_Reading_Nothing()
    {
        GivenACabinetThatMayWork();

        var handler = Handler(scope: new TenantScope(NullLogger<TenantScope>.Instance));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(Suspend(), CancellationToken.None));
    }

    // ------------------------------------------------------------------ the fiche reads it back (AC-6.1, AC-6.3)

    // [AC-6.1] The write and the read held against each other over ONE entitlement, `PlatformCancelPeriodTests`'
    // technique: the motif the command wrote is the motif the fiche serves. A trail written and never read back — or
    // read back from somewhere else — looks identical from either side alone.
    [Fact]
    public async Task The_Fiche_Reads_Back_The_Motif_The_Command_Wrote()
    {
        GivenACabinetThatMayWork();
        Assert.True((await Handler().Handle(Suspend(), CancellationToken.None)).IsSuccess);
        WireCabinet(isSuspended: true);

        var detail = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        Assert.True(detail.IsSuccess);
        var trail = detail.Value!.Suspension;
        Assert.NotNull(trail);
        Assert.Equal(Motif, trail!.SuspensionReason);
        Assert.Equal(_harness.Subscriptions.Subscription!.SuspendedAtUtc, trail.SuspendedAt);
        Assert.Equal(AuditActor.Console(AccountId).UserId, trail.SuspendedBy);
    }

    // A cabinet that is not suspended carries NO trail — null, never an object with blank fields, so the fiche cannot
    // render an empty « Motif » that would read as « suspendu, sans raison ».
    [Fact]
    public async Task An_Unsuspended_Cabinet_Carries_No_Trail()
    {
        GivenACabinetThatMayWork();
        WireCabinet();

        var detail = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Null(detail.Value!.Suspension);
    }

    // And lifting withdraws it, which is why the journal rows above are the durable record: `Unsuspend` clears the
    // motif on purpose, so a released cabinet stops reading as suspended anywhere.
    [Fact]
    public async Task Lifting_Withdraws_The_Trail_From_The_Fiche()
    {
        GivenACabinetThatMayWork();
        Assert.True((await Handler().Handle(Suspend(), CancellationToken.None)).IsSuccess);
        Assert.True((await Handler().Handle(Lift(), CancellationToken.None)).IsSuccess);
        WireCabinet();

        var detail = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        Assert.Null(detail.Value!.Suspension);
        // The two journal rows are what remains, and they are the only answer to « qui a suspendu ce cabinet ? »
        // once the trail is gone.
        Assert.Equal(2, _ledger.Rows.Count(r =>
            r.Action is PlatformAccessAction.Suspended or PlatformAccessAction.Unsuspended));
    }

    // ------------------------------------------------------------------ the detail read, for the cases above

    private GetPlatformClinicDetailQueryHandler DetailHandler() =>
        new(_activity.Object, _harness.Users.Object, _harness.Subscriptions, _ledger,
            new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _harness.UnitOfWork.Object, SystemWideScope(),
            PlatformMessagingReadStubs.NoAllowances(),
            PlatformMessagingReadStubs.NoReminderSettings(),
            PlatformMessagingReadStubs.NotSold(),
            NullLogger<GetPlatformClinicDetailQueryHandler>.Instance);

    private void WireCabinet(bool isSuspended = false)
    {
        var clinicId = SubscriptionVendorHarness.ClinicId;

        _activity.Setup(r => r.GetClinicRowAsync(clinicId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
