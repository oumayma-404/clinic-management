using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console's two WhatsApp-forfait writes and the read that feeds their confirmations
/// (<c>vendor-whatsapp-messaging-quota</c> US-6, US-7, US-8).
///
/// <para>The domain rules — standing-vs-top-up, the past-month refusal, what a cancellation reaches — belong to
/// <c>MessagingVendorCommandTests</c>. What these add is what only <i>composition</i> can be wrong about: the access
/// ledger riding the same save (AC-6.8), a double-click producing one entry (AC-6.7), and — the load-bearing one —
/// <see cref="The_Preview_On_The_Fiche_Equals_What_Cancelling_Actually_Does"/>, which runs the real read and the real
/// write over one ledger so AC-7.3's « computed server-side » cannot drift into a second arithmetic that agrees only on
/// the cases somebody happened to try.</para>
/// </summary>
public class PlatformMessagingWriteTests
{
    private static readonly Guid AccountId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly MessagingVendorHarness _harness = new();
    private readonly FakePlatformAccessLedger _ledger = new();

    private static string ThisMonth => ClinicClock.CurrentMonthKey();

    private RecordMessagingAllowanceFromConsoleCommandHandler Record(ITenantScope? scope = null) =>
        new(_harness.Allowances, _harness.Clinics.Object, _harness.Users.Object, _ledger, SignedIn(),
            _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<RecordMessagingAllowanceFromConsoleCommandHandler>.Instance);

    private CancelMessagingAllowanceFromConsoleCommandHandler CancelFromConsole(ITenantScope? scope = null) =>
        new(_harness.Allowances, _harness.Clinics.Object, _harness.Users.Object, _ledger, SignedIn(),
            _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<CancelMessagingAllowanceFromConsoleCommandHandler>.Instance);

    private static IPlatformSessionContext SignedIn()
    {
        var session = new Mock<IPlatformSessionContext>();
        session.Setup(s => s.GetAccountId()).Returns(AccountId);
        session.Setup(s => s.GetEmail()).Returns("vendor@editeur.tn");
        return session.Object;
    }

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    // [AC-6.8] The journal row and the allocation it records land in ONE save. An action that cannot be attributed does
    // not succeed — so the ledger row is staged before the refold's single commit, never written afterwards where a
    // failure would leave an allocation nobody can account for.
    [Fact]
    public async Task Recording_A_Forfait_Journals_It_In_The_Same_Save()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Record().Handle(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500, AmountDt = 45.000m,
                Method = "Transfer", Reference = "VIR-2026-0413"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(_ledger.Entries);
        Assert.Equal(PlatformAccessAction.GrantedMessagingAllowance, row.Action);
        Assert.Equal(AccountId, row.PlatformAccountId);
        Assert.Equal(MessagingVendorHarness.ClinicName, row.ClinicName);

        // ⚠️ It names the MESSAGING entry, not the SubscriptionPeriodId beside it. Sharing that column would have been
        // one line and would make the journal assert that a forfait de rappels extended the cabinet's right to record
        // work — and it would hand a replay the wrong kind of id.
        Assert.Equal(result.Value!.EntryId, row.MessagingAllowanceEntryId);
        Assert.Null(row.SubscriptionPeriodId);

        // Staged, then one commit: the ledger row was present before the save that persisted the allocation.
        Assert.Equal(1, _harness.Saves);
        Assert.Equal(1, _ledger.StagedBeforeSave);
    }

    // [AC-6.7] The vendor's own double-click: the same key twice produces ONE entry and replays the first outcome as a
    // SUCCESS, flagged, rather than claiming to have taken the money twice or refusing it.
    [Fact]
    public async Task A_Repeated_Submission_Produces_One_Entry_And_Replays_The_First_Outcome()
    {
        _harness.GivenStanding(200, ThisMonth);
        var handler = Record();

        var first = await handler.Handle(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500, IdempotencyKey = "sheet-1"
            },
            CancellationToken.None);

        var second = await handler.Handle(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500, IdempotencyKey = "sheet-1"
            },
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(first.Value!.AlreadyRecorded);
        Assert.True(second.Value!.AlreadyRecorded);
        Assert.Equal(first.Value.EntryId, second.Value.EntryId);

        // One standing entry beyond the provisioned one, one journal row, one save.
        Assert.Equal(2, _harness.Allowances.Entries.Count);
        Assert.Single(_ledger.Entries);
        Assert.Equal(1, _harness.Saves);

        // ⚠️ The replay states no « avant » figure rather than guessing one: what the forfait was before the first
        // submission is not recoverable afterwards, and inventing it would show a change that did not happen.
        Assert.Null(second.Value.PreviousAllowanceThisMonth);
        Assert.Equal(500, second.Value.AllowanceThisMonth);
    }

    // [AC-6.7][EC-5] The other half, and the one a read-first check cannot cover: two simultaneous submissions both read
    // « rien encore enregistré » and both insert, so the unique index refuses the second. That is not an error to show —
    // it is the first submission's answer.
    [Fact]
    public async Task A_Submission_That_Loses_The_Unique_Index_Race_Replays_Rather_Than_Failing()
    {
        _harness.GivenStanding(200, ThisMonth);

        // The winner's row, already committed by the racing request.
        var winnerEntry = _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.ClinicId, MessagingAllowanceKind.Standing, 500, ThisMonth, DateTime.UtcNow));

        _ledger.Hidden.Add(new PlatformAccessEntry(
            AccountId, "vendor@editeur.tn", MessagingVendorHarness.ClinicId, MessagingVendorHarness.ClinicName,
            PlatformAccessAction.GrantedMessagingAllowance, DateTime.UtcNow,
            idempotencyKey: "sheet-2", messagingAllowanceEntryId: winnerEntry.Id));

        _harness.UnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate key value violates unique constraint"));

        var result = await Record().Handle(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500, IdempotencyKey = "sheet-2"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyRecorded);
        Assert.Equal(winnerEntry.Id, result.Value.EntryId);
    }

    // [AC-7.5] « Déjà annulée » is a 409-carrying refusal, not a silent replay: that allocation was struck through by
    // SOMEBODY, and the fiche the refusal sends the reader back to is where the motif and the author are.
    [Fact]
    public async Task Cancelling_An_Already_Cancelled_Allocation_Is_Refused_And_Journals_Nothing()
    {
        var (entry, _) = _harness.GivenStanding(200, ThisMonth);
        entry.Cancel("Première annulation", "console|abc", DateTime.UtcNow);

        var result = await CancelFromConsole().Handle(
            new CancelMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = entry.Id, Reason = "Deuxième tentative"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            CancelMessagingAllowanceFromConsoleCommandHandler.AlreadyCancelledCode, result.Code);

        // Nothing happened, so nothing is journalled — a refused action must not read as an action taken.
        Assert.Empty(_ledger.Entries);
        Assert.Equal(0, _harness.Saves);
    }

    // [AC-6.8] A cancellation is journalled in the same save too, under its own action.
    [Fact]
    public async Task Cancelling_A_Forfait_Journals_It_Under_Its_Own_Action()
    {
        var (entry, _) = _harness.GivenStanding(200, ThisMonth);

        var result = await CancelFromConsole().Handle(
            new CancelMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = entry.Id, Reason = "Erreur de cabinet"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(_ledger.Entries);
        Assert.Equal(PlatformAccessAction.CancelledMessagingAllowance, row.Action);
        Assert.Equal(entry.Id, row.MessagingAllowanceEntryId);
        Assert.Equal(1, _harness.Saves);

        // The canceller is `console|{accountId}` through AuditActor's own constant — which is also the prefix the
        // counter pass excludes, so a vendor correction does not make a dormant cabinet read as busy tomorrow.
        Assert.Equal(AuditActor.Console(AccountId).UserId, entry.CancelledBy);
    }

    // [EC-12] An undeclared cross-clinic scope reads zero rows with no error, which on this surface would report every
    // cabinet as unknown. It throws instead, on every console path.
    [Fact]
    public async Task A_Write_With_No_Declared_Scope_Throws_Rather_Than_Reading_Nothing()
    {
        _harness.GivenStanding(200, ThisMonth);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Record(new TenantScope(NullLogger<TenantScope>.Instance)).Handle(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500
            },
            CancellationToken.None));
    }

    // An unknown payment method is REFUSED rather than ignored. Unlike a filter, where a stale value should narrow
    // nothing, this is a fact being written into a ledger nobody can edit afterwards.
    [Fact]
    public async Task An_Unknown_Payment_Method_Is_Refused()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Record().Handle(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500, Method = "Bitcoin"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(RecordMessagingAllowanceFromConsoleCommandHandler.UnknownMethodError, result.Error);
        Assert.Empty(_ledger.Entries);
    }

    // ── AC-7.3, and the highest-value case in the file ────────────────────────────────────────────────────────────
    //
    // The consequence the confirmation shows must be what cancelling actually does. Both halves run over ONE ledger, so
    // this fails on any second arithmetic — including the plausible « the current forfait minus this entry's messages »,
    // which is wrong for a STANDING entry because such an entry replaces rather than adds: cancelling the 900 hands the
    // month back to the 200 that was in force before it, not to 900 − 900 = 0.
    [Fact]
    public async Task The_Preview_On_The_Fiche_Equals_What_Cancelling_Actually_Does()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth, consumed: 150);

        var raised = _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.ClinicId, MessagingAllowanceKind.Standing, 900, ThisMonth,
            DateTime.UtcNow.AddMinutes(1)));

        month.SetAllowance(900, DateTime.UtcNow);

        // What the fiche promises.
        var detail = await Detail().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = MessagingVendorHarness.ClinicId }, CancellationToken.None);

        Assert.True(detail.IsSuccess);
        var previewed = detail.Value!.Messaging!.Entries.Single(e => e.EntryId == raised.Id).IfCancelled;
        Assert.NotNull(previewed);

        // What cancelling does.
        var done = await CancelFromConsole().Handle(
            new CancelMessagingAllowanceFromConsoleCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = raised.Id, Reason = "Erreur de saisie"
            },
            CancellationToken.None);

        Assert.True(done.IsSuccess);
        Assert.Equal(previewed!.Allowance, done.Value!.AllowanceThisMonth);
        Assert.Equal(previewed.Consumed, done.Value.ConsumedThisMonth);
        Assert.Equal(previewed.Exhausted, done.Value.ExhaustedThisMonth);

        // And it is the earlier standing figure, not zero — the assertion the naive subtraction fails.
        Assert.Equal(200, done.Value.AllowanceThisMonth);
        Assert.Equal(150, done.Value.ConsumedThisMonth);
        Assert.False(done.Value.ExhaustedThisMonth);
    }

    // [AC-7.4] The preview says « épuisé » in advance when the reduced forfait would fall below what is already spent —
    // which is the sentence the confirmation has to carry, since the vendor is about to hold a practice's reminders.
    [Fact]
    public async Task The_Preview_Says_Epuise_When_The_Cancellation_Would_Put_The_Month_Over()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth, consumed: 260);

        var topUp = _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.ClinicId, MessagingAllowanceKind.TopUp, 300, ThisMonth,
            DateTime.UtcNow.AddMinutes(1)));

        month.SetAllowance(500, DateTime.UtcNow);

        var detail = await Detail().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = MessagingVendorHarness.ClinicId }, CancellationToken.None);

        var previewed = detail.Value!.Messaging!.Entries.Single(e => e.EntryId == topUp.Id).IfCancelled!;

        Assert.Equal(200, previewed.Allowance);
        Assert.Equal(260, previewed.Consumed);
        Assert.Equal(0, previewed.Remaining);
        Assert.True(previewed.Exhausted);
    }

    // [AC-7.2] An already-cancelled entry carries NO preview: it is not an action the vendor can take, and a control
    // that opens onto a refusal is a dead control.
    [Fact]
    public async Task An_Already_Cancelled_Entry_Carries_No_Preview()
    {
        var (entry, _) = _harness.GivenStanding(200, ThisMonth);
        entry.Cancel("Déjà corrigée", "console|abc", DateTime.UtcNow);

        var detail = await Detail().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = MessagingVendorHarness.ClinicId }, CancellationToken.None);

        var row = detail.Value!.Messaging!.Entries.Single(e => e.EntryId == entry.Id);
        Assert.True(row.IsCancelled);
        Assert.Null(row.IfCancelled);
        Assert.Equal("Déjà corrigée", row.CancelReason);
    }

    // [AC-8.3] « Non mesuré » is not zero. A cabinet with no counting row reports three NULLS and `Measured: false` —
    // filling them from the ledger would paper over the one fault the vendor most needs to see, and would make the
    // reading unreachable.
    [Fact]
    public async Task A_Cabinet_With_No_Counting_Row_Reads_Non_Mesure_Rather_Than_Zero()
    {
        // A standing allocation, but no month row: what a cabinet looks like when the daily pass has not run.
        _harness.Allowances.Seed(MessagingAllowanceEntry.Provisioned(
            MessagingVendorHarness.ClinicId, 200, ThisMonth, DateTime.UtcNow));

        var detail = await Detail(measured: false).Handle(
            new GetPlatformClinicDetailQuery { ClinicId = MessagingVendorHarness.ClinicId }, CancellationToken.None);

        var messaging = detail.Value!.Messaging!;
        Assert.False(messaging.Measured);
        Assert.Null(messaging.Allowance);
        Assert.Null(messaging.Consumed);
        Assert.Null(messaging.Remaining);
        Assert.False(messaging.Exhausted);

        // The standing figure still reads, because it comes from the ledger rather than from the missing row — which is
        // exactly what tells the vendor « we owe them 200 and nothing is counting » rather than « they have nothing ».
        Assert.Equal(200, messaging.StandingAllowance);
    }

    // [EC-16] Where the deployment does not sell vendor messaging the whole section is ABSENT, not a set of zeros — so
    // the console renders no heading at all and neither messaging repository is read.
    [Fact]
    public async Task The_Section_Is_Absent_Where_The_Deployment_Does_Not_Sell_Messaging()
    {
        _harness.GivenStanding(200, ThisMonth);

        var detail = await Detail(sellsMessaging: false).Handle(
            new GetPlatformClinicDetailQuery { ClinicId = MessagingVendorHarness.ClinicId }, CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Null(detail.Value!.Messaging);
    }

    /// <summary>
    /// The detail read, wired onto the same in-memory ledger the writes use — which is what makes the
    /// preview-equals-write case above possible at all.
    /// </summary>
    private GetPlatformClinicDetailQueryHandler Detail(bool sellsMessaging = true, bool measured = true)
    {
        var activity = new Mock<IClinicActivityRepository>();
        var month = _harness.Allowances.Months.FirstOrDefault(m => m.MonthKey == ThisMonth);

        activity
            .Setup(r => r.GetClinicRowAsync(
                MessagingVendorHarness.ClinicId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PlatformClinicRow(
                MessagingVendorHarness.ClinicId, MessagingVendorHarness.ClinicName, "Tunis",
                new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                HasEntitlement: true,
                Plan: SubscriptionPlan.Cabinet,
                SubscriptionEndsOn: null,
                SubscriptionIsSuspended: false,
                LatestCoverKind: SubscriptionPeriodKind.Grandfathered,
                Users: 3, Patients: 412, Appointments30d: 96, Writes7d: 4, Writes30d: 12, ActiveDays30d: 9,
                LastWriteAt: null, LastLoginAt: null, CollectedThisMonth: 0m, CountersComputedAt: null,
                HasMessagingMonth: measured && month is not null,
                MessagingAllowance: month?.AllowanceMessages ?? 0,
                MessagingConsumed: month?.ConsumedMessages ?? 0));

        activity
            .Setup(r => r.GetDaysAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ClinicActivityDay>());

        var users = new Mock<IUserRepository>();
        users
            .Setup(r => r.GetPrimaryAdminContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAdminContact?)null);

        var subscriptions = new Mock<IClinicSubscriptionRepository>();
        subscriptions
            .Setup(r => r.GetEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SubscriptionPeriod>());
        subscriptions
            .Setup(r => r.GetByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicSubscription?)null);

        var availability = new Mock<IVendorMessagingAvailability>();
        availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsMessaging);

        return new GetPlatformClinicDetailQueryHandler(
            activity.Object, users.Object, subscriptions.Object, _ledger, SignedIn(),
            _harness.UnitOfWork.Object, SystemWideScope(),
            _harness.Allowances, PlatformMessagingReadStubs.NoReminderSettings(), availability.Object,
            NullLogger<GetPlatformClinicDetailQueryHandler>.Instance);
    }

    /// <summary>
    /// An in-memory access ledger that records <b>when</b> a row was staged relative to the save — which is the only way
    /// to assert AC-6.8's « in the same operation » rather than merely « both happened ».
    /// </summary>
    private sealed class FakePlatformAccessLedger : IPlatformAccessEntryRepository
    {
        private readonly List<PlatformAccessEntry> _entries = new();

        /// <summary>Rows a racing request already committed, visible to the idempotency lookup only.</summary>
        public List<PlatformAccessEntry> Hidden { get; } = new();

        public IReadOnlyList<PlatformAccessEntry> Entries => _entries;

        public int StagedBeforeSave { get; private set; }

        public Task AddAsync(PlatformAccessEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Add(entry);
            StagedBeforeSave = _entries.Count;
            return Task.CompletedTask;
        }

        public Task<PlatformAccessEntry?> GetByIdempotencyKeyAsync(
            string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _entries.Concat(Hidden).FirstOrDefault(e => e.IdempotencyKey == idempotencyKey));

        public Task<PagedResult<PlatformAccessEntry>> GetPageAsync(
            Guid? platformAccountId, Guid? clinicId, PageRequest? paging = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The messaging writes never page the ledger.");

        public Task<IReadOnlyList<PlatformAccessActor>> GetRecordedActorsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The messaging writes never list the journal's actors.");
    }
}
