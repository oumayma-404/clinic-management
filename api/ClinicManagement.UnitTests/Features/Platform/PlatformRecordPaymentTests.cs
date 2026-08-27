using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console records a payment and the cabinet is unlocked (<c>platform-console</c> US-4).
///
/// <para><b>It runs the real command over the companion's own in-memory ledger</b> (<c>SubscriptionVendorHarness</c>),
/// not over a mocked repository, because every AC here is about what the ledger ends up holding and what the fold
/// makes of it. A mock would prove a method was called and nothing about the date the cabinet now stands on.</para>
///
/// <para><b>The load-bearing case is <see cref="A_Double_Click_Records_One_Entry_And_Returns_The_First_Answer"/>,
/// and its sibling <see cref="A_Repeated_Submission_That_Loses_The_Race_Replays_Rather_Than_Failing"/>.</b> AC-4.6
/// is satisfiable two ways and only one of them is real: reading first passes whenever the two taps are far enough
/// apart, and fails exactly when they are not — which is the case a double-click *is*. The second test drives the
/// path where both submissions read « rien encore enregistré » and the unique index refuses one, so the guard being
/// exercised is the database's rather than the handler's.</para>
///
/// <para>⚠️ Fixtures anchor on <c>ClinicClock.ClinicToday()</c>, the opposite of <c>SubscriptionGateMiddlewareTests</c>'
/// decades-away dates and for the same reason the vendor command tests do: the handler stamps the entry's recorded
/// day from the real clock, and that day <i>is</i> the fold's anchor.</para>
/// </summary>
public class PlatformRecordPaymentTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";
    private const string ClinicName = "Cabinet Ben Ali";

    private readonly SubscriptionVendorHarness _harness = new();
    private readonly FakeAccessLedger _ledger = new();

    public PlatformRecordPaymentTests()
    {
        _harness.Clinics
            .Setup(c => c.GetByIdAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TheClinic());
    }

    // ------------------------------------------------------------------ harness

    private static Clinic TheClinic() =>
        new(SubscriptionVendorHarness.ClinicId, ClinicName, city: "Tunis");

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private RecordSubscriptionPeriodCommandHandler Handler(
        IPlatformSessionContext? session = null, ITenantScope? scope = null) =>
        new(_harness.Clinics.Object, _harness.Users.Object, _harness.Subscriptions, _ledger,
            session ?? new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<RecordSubscriptionPeriodCommandHandler>.Instance);

    private static RecordSubscriptionPeriodCommand Payment(
        int? months = 12,
        decimal? amount = 1_200.000m,
        string? key = null,
        bool complimentary = false) =>
        new()
        {
            ClinicId = SubscriptionVendorHarness.ClinicId,
            DurationMonths = months,
            AmountDt = amount,
            Method = complimentary ? null : nameof(SubscriptionPaymentMethod.Transfer),
            Reference = complimentary ? null : "VIR-9931",
            Complimentary = complimentary,
            IdempotencyKey = key,
        };

    // ------------------------------------------------------------------ AC-4.1 / AC-4.2 / AC-4.3

    // [AC-4.2] The console computes NO date. The end date it reports must be exactly what re-folding the whole
    // ledger yields — asserted against the real fold rather than against a retyped literal, which would be a second
    // copy of the arithmetic and could agree with a mistake.
    [Fact]
    public async Task The_End_Date_Is_The_Ledgers_Own_Fold()
    {
        var today = ClinicClock.ClinicToday();
        _harness.GivenEntitlement(today.AddMonths(-6), durationMonths: 12);

        var result = await Handler().Handle(Payment(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            SubscriptionLedger.Fold(_harness.Subscriptions.Entries.Select(e => e.ToLedgerEntry())),
            result.Value!.EndsOn);
    }

    // [EC-3] Paying while still covered never costs days: the new stretch resumes where the old one ran out rather
    // than restarting from today. It is the fold's property, and this is the console's own guard on it — the failure
    // is silent money.
    [Fact]
    public async Task Paying_Early_Never_Costs_Days()
    {
        var today = ClinicClock.ClinicToday();
        var subscription = _harness.GivenEntitlement(today.AddDays(-10), durationMonths: 12);
        var endBefore = subscription.EndsOn!.Value;

        var result = await Handler().Handle(Payment(months: 12), CancellationToken.None);

        Assert.Equal(endBefore, result.Value!.PreviousEndsOn);
        Assert.True(result.Value.EndsOn > endBefore.AddMonths(11));
    }

    // [AC-4.3] The state comes back from SubscriptionStateReader, not from « c'est payé, donc actif ». This case is
    // the one that fails if the console ever infers it: a SUSPENDED cabinet stays suspended after a payment, and
    // telling the vendor otherwise is the worst possible moment to be wrong.
    [Fact]
    public async Task A_Suspended_Cabinet_Still_Reads_Suspended_After_A_Payment()
    {
        var today = ClinicClock.ClinicToday();
        var subscription = _harness.GivenEntitlement(today.AddDays(-10), durationMonths: 1);
        subscription.Suspend("Usage abusif", "console|op", DateTime.UtcNow);

        var result = await Handler().Handle(Payment(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(SubscriptionState.Suspended), result.Value!.State);
    }

    // [AC-4.8] « Offert » is recorded as a complimentary period with NO amount — never as a payment of zero.
    [Fact]
    public async Task A_Complimentary_Period_Carries_No_Amount()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-2), durationMonths: 1);

        var result = await Handler().Handle(
            Payment(months: 1, amount: null, complimentary: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = _harness.Subscriptions.Entries.Single(e => e.Id == result.Value!.EntryId);
        Assert.Equal(SubscriptionPeriodKind.Complimentary, entry.Kind);
        Assert.Null(entry.Amount);
    }

    // An amount on a complimentary period is refused rather than silently dropped: « offert » and « payé 1 200 DT »
    // are different statements about money, and guessing which one the operator meant is not this command's job.
    [Fact]
    public async Task A_Complimentary_Period_With_An_Amount_Is_Refused()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-2), durationMonths: 1);

        var result = await Handler().Handle(
            Payment(months: 1, amount: 1_200.000m, complimentary: true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(RecordSubscriptionPeriodCommandHandler.ComplimentaryWithAmountError, result.Error);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ AC-4.5 refusals

    // [AC-4.5] No duration at all would be « sans échéance » — permanent free cover, reachable by forgetting one
    // field and unnoticeable afterwards. A cabinet that should never expire is grandfathered by a migration.
    [Fact]
    public async Task A_Payment_With_No_Duration_Is_Refused_By_Name()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday(), durationMonths: 12);

        var result = await Handler().Handle(Payment(months: null), CancellationToken.None);

        Assert.Equal(RecordSubscriptionPeriodCommandHandler.NoDurationError, result.Error);
        Assert.Empty(_ledger.Rows);
    }

    // [AC-4.5] A non-positive duration is refused by the entity's own French guard, so the console and the five
    // console verbs cannot disagree about what a valid period is.
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_Non_Positive_Duration_Is_Refused(int months)
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday(), durationMonths: 12);

        var result = await Handler().Handle(Payment(months: months), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("positive", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // [AC-4.5] An unknown cabinet is refused under a CODE the console branches on, never on the French sentence.
    [Fact]
    public async Task An_Unknown_Cabinet_Is_Refused_With_A_Code()
    {
        var unknown = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        _harness.Clinics.Setup(c => c.ExistsAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var command = Payment();
        command.ClinicId = unknown;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(RecordSubscriptionPeriodCommandHandler.UnknownClinicCode, result.Code);
        Assert.Empty(_ledger.Rows);
    }

    // An unknown forfait or method is REFUSED rather than ignored — unlike a stale filter value, which should
    // narrow nothing. This one is a fact being written into a ledger nobody can edit afterwards.
    [Theory]
    [InlineData("Premium", null)]
    [InlineData(null, "Bitcoin")]
    public async Task An_Unknown_Plan_Or_Method_Is_Refused(string? plan, string? method)
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday(), durationMonths: 12);

        var command = Payment();
        command.Plan = plan;
        if (method is not null)
        {
            command.Method = method;
        }

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ AC-4.6 / EC-5 / EC-6 idempotency

    // [AC-4.6] The second tap of a double-click carries the first tap's key, finds the money already taken, and is
    // answered with the FIRST outcome — one ledger entry, one access row, and a success rather than a refusal.
    [Fact]
    public async Task A_Double_Click_Records_One_Entry_And_Returns_The_First_Answer()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var first = await Handler().Handle(Payment(key: "sheet-42"), CancellationToken.None);
        var second = await Handler().Handle(Payment(key: "sheet-42"), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.False(first.Value!.AlreadyRecorded);
        Assert.True(second.Value!.AlreadyRecorded);
        Assert.Equal(first.Value.EntryId, second.Value.EntryId);
        Assert.Equal(first.Value.EndsOn, second.Value.EndsOn);

        Assert.Single(_ledger.Rows);
        Assert.Single(_harness.Subscriptions.Entries.Where(e => e.Amount == 1_200.000m));
    }

    // [EC-5] And the race the read-first check cannot catch: both submissions see « rien encore enregistré », both
    // insert, and the UNIQUE INDEX refuses one. That refusal is not an error to show — it is the first submission's
    // answer. Driven here by emptying the read while leaving the index in place.
    [Fact]
    public async Task A_Repeated_Submission_That_Loses_The_Race_Replays_Rather_Than_Failing()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var winner = await Handler().Handle(Payment(key: "sheet-42"), CancellationToken.None);
        Assert.True(winner.IsSuccess);

        // The loser's own lookup happened before the winner committed, so it proceeds to the insert — which the
        // ledger's unique key refuses exactly as PostgreSQL's does. Blinding only that first read is what puts the
        // test on the index's path rather than back on the read-first check.
        _ledger.BlindReadsRemaining = 1;

        var loser = await Handler().Handle(Payment(key: "sheet-42"), CancellationToken.None);

        Assert.True(loser.IsSuccess);
        Assert.True(loser.Value!.AlreadyRecorded);
        Assert.Equal(winner.Value!.EntryId, loser.Value.EntryId);
        Assert.Single(_ledger.Rows);
    }

    // [EC-6] Two DIFFERENT grants landing together are two entries in an append-only ledger, both kept, with no
    // conflict response — the surplus one is corrected by a cancellation, never by refusing money already received.
    [Fact]
    public async Task Two_Different_Payments_Both_Land_And_Both_Are_Kept()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var first = await Handler().Handle(Payment(key: "sheet-a"), CancellationToken.None);
        var second = await Handler().Handle(Payment(key: "sheet-b"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.EntryId, second.Value!.EntryId);
        Assert.Equal(2, _ledger.Rows.Count);
    }

    // An unkeyed submission is honoured — it simply has no replay protection. Refusing one would make the key
    // mandatory on the wire, which no console verb and no script would supply.
    [Fact]
    public async Task An_Unkeyed_Payment_Is_Recorded()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var result = await Handler().Handle(Payment(key: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(_ledger.Rows).IdempotencyKey);
    }

    // ------------------------------------------------------------------ AC-4.7 / AC-7.3 attribution

    // [AC-4.7][AC-2.2] The entry is stamped `console|{accountId}` through AuditActor's own constant — the prefix the
    // counter pass's exclusion also reads. A clinic user id here would make a dormant cabinet read as active the
    // morning after it was granted a subscription, on exactly the cabinet the « dormant » filter surfaced.
    [Fact]
    public async Task The_Entry_Is_Attributed_To_The_Console_Account()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var result = await Handler().Handle(Payment(), CancellationToken.None);

        var entry = _harness.Subscriptions.Entries.Single(e => e.Id == result.Value!.EntryId);
        Assert.Equal(AuditActor.Console(AccountId).UserId, entry.RecordedBy);
        Assert.StartsWith(AuditActor.ConsolePrefix, entry.RecordedBy!, StringComparison.Ordinal);
    }

    // [AC-7.3] The write is recorded in the console's own ledger, naming both parties and the entry it produced —
    // which is what lets the journal answer « qui a encaissé quoi, pour quel cabinet ? » years later.
    [Fact]
    public async Task The_Write_Is_Recorded_In_The_Access_Ledger()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var result = await Handler().Handle(Payment(key: "sheet-42"), CancellationToken.None);

        var row = Assert.Single(_ledger.Rows);
        Assert.Equal(PlatformAccessAction.GrantedPeriod, row.Action);
        Assert.Equal(AccountId, row.PlatformAccountId);
        Assert.Equal(AccountEmail, row.AccountEmail);
        Assert.Equal(ClinicName, row.ClinicName);
        Assert.Equal(result.Value!.EntryId, row.SubscriptionPeriodId);
        Assert.Equal("sheet-42", row.IdempotencyKey);
    }

    // [AC-7.3] An unattributable write must not aboutir — the read path's rule, applied to a write. It fails rather
    // than throwing out of the handler (the catch-all turns it into a French refusal, as Part 3's read does), and
    // the check runs BEFORE the entry is built, because `RecordedBy` is written into a row nobody can edit later.
    [Fact]
    public async Task An_Unattributable_Payment_Fails_Rather_Than_Being_Recorded_Anonymously()
    {
        _harness.GivenEntitlement(ClinicClock.ClinicToday().AddMonths(-6), durationMonths: 12);

        var handler = Handler(session: new FakePlatformSession { AccountId = null, Email = null });

        var result = await handler.Handle(Payment(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_ledger.Rows);
        Assert.Empty(_harness.Subscriptions.Entries.Where(e => e.Amount == 1_200.000m));
    }

    // [EC-12] A write reached with no cross-cabinet scope declared THROWS rather than reading zero rows and
    // reporting « cabinet introuvable » — the same guard every console read carries.
    [Fact]
    public async Task A_Write_Without_A_Declared_Scope_Refuses_Instead_Of_Reading_Nothing()
    {
        var handler = Handler(scope: new TenantScope(NullLogger<TenantScope>.Instance));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(Payment(), CancellationToken.None));
    }
}
