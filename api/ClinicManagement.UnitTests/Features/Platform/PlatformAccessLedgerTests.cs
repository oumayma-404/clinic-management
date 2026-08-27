using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The cabinet detail and the console's own access ledger (<c>platform-console</c> US-3, FR-5, AC-7.3).
///
/// <para><b>The load-bearing case is <see cref="The_Journal_Returns_What_Opening_A_Cabinet_Recorded"/>.</b> It runs
/// the real detail handler and the real journal handler over <b>one</b> ledger, so the row the write produced is
/// compared with the row the read serves — rather than against a hand-written expectation, which would be a second
/// authority and the drift it allowed would be a ledger quietly disagreeing with itself. A write-only ledger and a
/// ledger nobody reads back look identical from outside.</para>
///
/// <para><b>The second is <see cref="Loading_The_List_Cannot_Write_A_Ledger_Row"/>, asserted on the
/// constructor.</b> AC-3.5 is a promise about something that does <i>not</i> happen, and « I ran the list and no
/// row appeared » passes just as well when the ledger is broken. That the list handler cannot even reach the
/// repository is the only form of that assertion which cannot pass for the wrong reason.</para>
/// </summary>
public class PlatformAccessLedgerTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";

    private readonly FakeAccessLedger _ledger = new();
    private readonly Mock<IClinicActivityRepository> _activity = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    // ------------------------------------------------------------------ harness

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private static IPlatformSessionContext SignedIn() =>
        new FakePlatformSession { AccountId = AccountId, Email = AccountEmail };

    /// <summary>
    /// ⚠️ The empty-ledger default is set <b>here</b>, not inside <c>DetailHandler()</c>: Moq lets the last matching
    /// setup win, so an <c>It.IsAny</c> stub applied when the handler is built would silently override the specific
    /// ledger a test had just wired — and the test would assert against no rows while looking correct.
    /// (Moq's default for an unstubbed collection-returning read is <c>null</c>, which the handler dereferences and
    /// the catch-all turns into a French business error nowhere near the real cause.)
    /// </summary>
    public PlatformAccessLedgerTests() =>
        _subscriptions
            .Setup(r => r.GetEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SubscriptionPeriod>());

    private GetPlatformClinicDetailQueryHandler DetailHandler(
        IPlatformSessionContext? session = null, ITenantScope? scope = null) =>
        new(_activity.Object, _users.Object, _subscriptions.Object, _ledger, session ?? SignedIn(),
            _unitOfWork.Object, scope ?? SystemWideScope(),
            PlatformMessagingReadStubs.NoAllowances(),
            PlatformMessagingReadStubs.NoReminderSettings(),
            PlatformMessagingReadStubs.NotSold(),
            NullLogger<GetPlatformClinicDetailQueryHandler>.Instance);

    private GetPlatformAccessLogQueryHandler JournalHandler() =>
        new(_ledger, SystemWideScope(), NullLogger<GetPlatformAccessLogQueryHandler>.Instance);

    private static PlatformClinicRow Row(string name = "Cabinet Ben Ali") =>
        new(ClinicId, name, "Tunis", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            HasEntitlement: true,
            Plan: SubscriptionPlan.Cabinet,
            SubscriptionEndsOn: null,
            SubscriptionIsSuspended: false,
            LatestCoverKind: SubscriptionPeriodKind.Grandfathered,
            Users: 3, Patients: 412, Appointments30d: 96, Writes7d: 4, Writes30d: 12, ActiveDays30d: 9,
            LastWriteAt: new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
            LastLoginAt: new DateTime(2026, 8, 10, 7, 0, 0, DateTimeKind.Utc),
            CollectedThisMonth: 14320.000m,
            CountersComputedAt: new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc));

    private void WireCabinet(PlatformClinicRow? row, params ClinicActivityDay[] days)
    {
        _activity.Setup(r => r.GetClinicRowAsync(ClinicId, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _activity.Setup(r => r.GetDaysAsync(
                ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(days);
    }

    private void WireAdmin(ClinicAdminContact? admin) =>
        _users.Setup(r => r.GetPrimaryAdminContactAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(admin);

    // ------------------------------------------------------------------ the ledger

    // [AC-7.3] Opening one cabinet records exactly one row, and it names all four things the AC asks for: who,
    // which cabinet, which action, when. The email and the cabinet's name are copied ONTO the row, which is what
    // keeps it readable after either party is deleted.
    [Fact]
    public async Task Opening_A_Cabinet_Records_Who_Looked_At_What()
    {
        WireCabinet(Row("Cabinet Ben Ali"));
        WireAdmin(new ClinicAdminContact("Salma Ben Ali", "salma@cabinet.tn", IsActive: true));

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_ledger.Rows);
        Assert.Equal(AccountId, entry.PlatformAccountId);
        Assert.Equal(AccountEmail, entry.AccountEmail);
        Assert.Equal(ClinicId, entry.ClinicId);
        Assert.Equal("Cabinet Ben Ali", entry.ClinicName);
        Assert.Equal(PlatformAccessAction.ViewedClinic, entry.Action);
        // Staged and committed by the read itself — the row is not a post-commit best-effort side effect, because
        // the operation being recorded IS this read (see the handler's own remarks).
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [FR-5][AC-7.3] The ledger is readable, not write-only — and what comes back is what went in. Both handlers
    // are real and share one ledger, so this compares the write with the read rather than with a retyped table.
    [Fact]
    public async Task The_Journal_Returns_What_Opening_A_Cabinet_Recorded()
    {
        WireCabinet(Row("Cabinet Ben Ali"));
        WireAdmin(null);

        await DetailHandler().Handle(new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);
        var written = Assert.Single(_ledger.Rows);

        var journal = await JournalHandler().Handle(new GetPlatformAccessLogQuery(), CancellationToken.None);

        Assert.True(journal.IsSuccess);
        var read = Assert.Single(journal.Value!.Items);
        Assert.Equal(written.Id, read.EntryId);
        Assert.Equal(written.PlatformAccountId, read.PlatformAccountId);
        Assert.Equal(written.AccountEmail, read.AccountEmail);
        Assert.Equal(written.ClinicId, read.ClinicId);
        Assert.Equal(written.ClinicName, read.ClinicName);
        Assert.Equal(written.OccurredAt, read.OccurredAt);
        Assert.Equal(nameof(PlatformAccessAction.ViewedClinic), read.Action);
        // The French label travels with the row rather than being a map the browser keeps a copy of.
        Assert.Equal(PlatformAccessLabels.Action(PlatformAccessAction.ViewedClinic), read.ActionLabel);
        // And the « Compte » filter's options come from the rows, so the account that just acted is offered.
        var actor = Assert.Single(journal.Value.Actors);
        Assert.Equal(AccountId, actor.PlatformAccountId);
        Assert.Equal(AccountEmail, actor.AccountEmail);
    }

    // [AC-3.5] Listing cabinets is NOT recorded — one list read touches every cabinet, so a row per cabinet per
    // page load would drown every reading anyone wants, including the ones above.
    //
    // ⚠️ Asserted on the CONSTRUCTOR, not by running the list and finding no row: the behavioural version passes
    // just as happily when the ledger is broken for every caller. A handler that cannot reach the repository cannot
    // write to it for any reason, now or after a later edit.
    [Fact]
    public void Loading_The_List_Cannot_Write_A_Ledger_Row()
    {
        var listDependencies = typeof(ListPlatformClinicsQueryHandler)
            .GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType);

        var summaryDependencies = typeof(GetPlatformSummaryQueryHandler)
            .GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IPlatformAccessEntryRepository), listDependencies);
        Assert.DoesNotContain(typeof(IPlatformAccessEntryRepository), summaryDependencies);
    }

    // [AC-7.3] An action nobody can be attributed to does not happen. This is the one place in the codebase where a
    // failed ledger write fails its operation rather than being swallowed: « every detail read is recorded » is
    // false the moment an unrecorded read succeeds.
    [Fact]
    public async Task A_Read_With_No_Identifiable_Console_Account_Refuses_Rather_Than_Recording_Nothing()
    {
        WireCabinet(Row());
        WireAdmin(null);

        var handler = DetailHandler(session: new FakePlatformSession { AccountId = null, Email = null });

        var result = await handler.Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_ledger.Rows);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [EC-13] A cabinet deleted since the list was drawn is a refusal carrying a CODE — the console renders « ce
    // cabinet n'existe plus » off that, never off the French sentence, which is the `Contains("déjà facturée")`
    // defect this codebase has already paid for once. And nothing is recorded: there was no cabinet to look at.
    [Fact]
    public async Task A_Vanished_Cabinet_Is_Refused_By_Code_And_Records_Nothing()
    {
        WireCabinet(null);

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GetPlatformClinicDetailQuery.NotFoundCode, result.Code);
        Assert.Empty(_ledger.Rows);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-5] Both filters reach the repository verbatim. The matching itself is SQL and out of this suite's reach,
    // so what is worth holding is that neither is silently dropped — a journal that ignores « ce cabinet » shows
    // every access ever made and looks like it is working.
    [Fact]
    public async Task Both_Journal_Filters_Reach_The_Repository()
    {
        var repository = new Mock<IPlatformAccessEntryRepository>();
        Guid? capturedAccount = null;
        Guid? capturedClinic = null;
        PageRequest? capturedPaging = null;

        repository.Setup(r => r.GetPageAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid? a, Guid? c, PageRequest? p, CancellationToken _) =>
            {
                capturedAccount = a;
                capturedClinic = c;
                capturedPaging = p;
            })
            .ReturnsAsync(new PagedResult<PlatformAccessEntry>(Array.Empty<PlatformAccessEntry>(), 1, 25, 0));
        repository.Setup(r => r.GetRecordedActorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformAccessActor>());

        var handler = new GetPlatformAccessLogQueryHandler(
            repository.Object, SystemWideScope(), NullLogger<GetPlatformAccessLogQueryHandler>.Instance);

        await handler.Handle(
            new GetPlatformAccessLogQuery { PlatformAccountId = AccountId, ClinicId = ClinicId },
            CancellationToken.None);

        Assert.Equal(AccountId, capturedAccount);
        Assert.Equal(ClinicId, capturedClinic);
        // Omitting the paging parameters reads the FIRST PAGE, not the whole ledger — the audit ledger's rule.
        Assert.Equal(1, capturedPaging!.Value.Page);
        Assert.Equal(PageRequest.DefaultPageSize, capturedPaging.Value.PageSize);
    }

    // [EC-12] Both console reads refuse an undeclared cross-cabinet scope instead of reading zero rows and
    // reporting success — the distinction PlatformPortfolioQueryTests makes for the list, held here for the two
    // reads Part 3 adds.
    [Fact]
    public async Task Neither_Part_3_Read_Runs_Without_A_Declared_Scope()
    {
        WireCabinet(Row());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DetailHandler(scope: new TenantScope(NullLogger<TenantScope>.Instance))
                .Handle(new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None));

        var journal = new GetPlatformAccessLogQueryHandler(
            _ledger, new TenantScope(NullLogger<TenantScope>.Instance),
            NullLogger<GetPlatformAccessLogQueryHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            journal.Handle(new GetPlatformAccessLogQuery(), CancellationToken.None));

        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ the detail itself

    // [AC-3.3] The administrator's name and address reach the screen, and « il y en a un mais il est désactivé » is
    // a distinct answer from « il n'y en a pas » — a support call needs to know which.
    [Fact]
    public async Task The_Administrators_Contact_Is_Carried_Through()
    {
        WireCabinet(Row());
        WireAdmin(new ClinicAdminContact("Salma Ben Ali", "salma@cabinet.tn", IsActive: false));

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        Assert.Equal("Salma Ben Ali", result.Value!.AdminName);
        Assert.Equal("salma@cabinet.tn", result.Value.AdminEmail);
        Assert.False(result.Value.AdminIsActive);
    }

    // [AC-3.3] A cabinet with no admin account at all reads as unreachable rather than as a live contact: the name
    // is null AND the active flag is false, so no screen can render a blank name as somebody to ring.
    [Fact]
    public async Task A_Cabinet_With_No_Administrator_Does_Not_Read_As_Reachable()
    {
        WireCabinet(Row());
        WireAdmin(null);

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        Assert.Null(result.Value!.AdminName);
        Assert.Null(result.Value.AdminEmail);
        Assert.False(result.Value.AdminIsActive);
    }

    // [AC-3.1] The detail's figures are the LIST's figures — AC-3.1 says « the same », and the repository serves one
    // shared projection so a cabinet cannot read one way in the portfolio and another when opened.
    [Fact]
    public async Task The_Detail_Reports_The_Same_Figures_As_The_List()
    {
        WireCabinet(Row());
        WireAdmin(null);

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        var clinic = result.Value!.Clinic;
        Assert.Equal(412, clinic.Patients);
        Assert.Equal(96, clinic.Appointments30d);
        Assert.Equal(12, clinic.Writes30d);
        Assert.Equal(14320.000m, clinic.ClinicCollectedThisMonthDt);
    }

    // [AC-3.1][EC-15] Six months, oldest first, every one present — and a month the counter pass never covered
    // carries DaysMeasured = 0 rather than being absent or drawn as a real zero. The pass writes a rolling 30-day
    // window (progress.md DEV-5), so five of these six are genuinely unmeasured on a young deployment, and a chart
    // that drew them as zero would show every cabinet collapsing the further back the reader looks.
    //
    // ⚠️ The window is derived from ClinicClock rather than hard-coded, deliberately: what is under test here is
    // the bucketing and the measured/unmeasured distinction. « What is today in Tunis » is ClinicClockTests'
    // business, and asserting it here against a literal would flake for one hour of every day.
    [Fact]
    public async Task The_Trend_Has_Six_Months_And_An_Unmeasured_One_Is_Not_A_Zero()
    {
        var today = DateOnly.FromDateTime(ClinicClock.ClinicToday());
        var computedAt = new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc);

        // Two measured days in the current clinic-local month; every earlier month left uncovered.
        WireCabinet(
            Row(),
            new ClinicActivityDay(ClinicId, new DateOnly(today.Year, today.Month, 1), 7, 3, 2, computedAt),
            new ClinicActivityDay(ClinicId, new DateOnly(today.Year, today.Month, 2), 5, 1, 0, computedAt));
        WireAdmin(null);

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        var trend = result.Value!.Trend;
        Assert.Equal(GetPlatformClinicDetailQuery.TrendMonths, trend.Count);

        // Oldest first, strictly consecutive, ending on the clinic's current month.
        for (var i = 1; i < trend.Count; i++)
        {
            var previous = new DateOnly(trend[i - 1].Year, trend[i - 1].Month, 1);
            Assert.Equal(previous.AddMonths(1), new DateOnly(trend[i].Year, trend[i].Month, 1));
        }

        var current = trend[^1];
        Assert.Equal(today.Year, current.Year);
        Assert.Equal(today.Month, current.Month);
        Assert.Equal(12, current.Writes);
        Assert.Equal(4, current.Appointments);
        Assert.Equal(2, current.PatientsCreated);
        Assert.Equal(2, current.DaysMeasured);

        // Every earlier month: measured on no day at all, which is what the screen must state as such.
        Assert.All(trend.Take(trend.Count - 1), month =>
        {
            Assert.Equal(0, month.DaysMeasured);
            Assert.Equal(0, month.Writes);
        });

        // The label is French and server-built, so the axis and its text alternative cannot disagree.
        Assert.Equal(ClinicClock.MonthLabelFr(current.Year, current.Month), current.MonthLabel);
    }

    // [AC-3.2] The payment history is the companion's ledger, read — newest first, with the « période couverte »
    // each entry covers taken from the FOLD rather than computed from the entry alone. Computing it here would
    // produce entirely plausible dates describing periods the cabinet was never entitled to.
    [Fact]
    public async Task The_Payment_History_Is_The_Ledger_With_Its_Folded_Periods()
    {
        WireCabinet(Row());
        WireAdmin(null);

        var trial = SubscriptionPeriod.Trial(ClinicId, new DateTime(2026, 1, 5), 30,
            new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc));
        var paid = SubscriptionPeriod.Create(ClinicId, SubscriptionPeriodKind.Paid, new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc), durationMonths: 12, amount: 1_200.000m,
            method: SubscriptionPaymentMethod.Transfer, reference: "VIR-9931");

        _subscriptions
            .Setup(r => r.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { trial, paid });

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        var payments = result.Value!.Payments;
        Assert.Equal(2, payments.Count);

        // Newest first.
        Assert.Equal(paid.Id, payments[0].EntryId);
        Assert.Equal(1_200.000m, payments[0].AmountDt);
        Assert.Equal("VIR-9931", payments[0].Reference);
        Assert.Equal("Virement", payments[0].MethodLabel);

        // The spans are the fold's, asserted against the real fold rather than against retyped dates — a literal
        // here would be a second copy of the arithmetic and could agree with a mistake.
        var spans = SubscriptionLedger.FoldWithSpans(new[] { trial.ToLedgerEntry(), paid.ToLedgerEntry() }).Spans
            .ToDictionary(s => s.EntryId);
        Assert.Equal(spans[paid.Id].FromDay, payments[0].CoversFrom);
        Assert.Equal(spans[paid.Id].ThroughDay, payments[0].CoversThrough);
    }

    // [AC-4.8] « Offert » carries NO amount rather than an amount of zero — the two are different statements, and
    // only one of them is ever true of a complimentary period.
    [Fact]
    public async Task A_Complimentary_Period_Reads_As_Offert_With_No_Amount()
    {
        WireCabinet(Row());
        WireAdmin(null);

        var gift = SubscriptionPeriod.Create(ClinicId, SubscriptionPeriodKind.Complimentary,
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc), durationMonths: 1);

        _subscriptions
            .Setup(r => r.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { gift });

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        var entry = Assert.Single(result.Value!.Payments);
        Assert.Equal("Offert", entry.KindLabel);
        Assert.Null(entry.AmountDt);
    }

    // [AC-5.2] A cancelled entry stays listed with its reason, canceller and moment. Hiding it would answer
    // « what were we paid, and for what? » with a tidied version of the truth, on the one screen built to check.
    [Fact]
    public async Task A_Cancelled_Entry_Stays_Listed_With_Its_Reason()
    {
        WireCabinet(Row());
        WireAdmin(null);

        var mistake = SubscriptionPeriod.Create(ClinicId, SubscriptionPeriodKind.Paid, new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), durationMonths: 12, amount: 1_200.000m);
        mistake.Cancel("Montant saisi deux fois", "console|op", new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc));

        _subscriptions
            .Setup(r => r.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mistake });

        var result = await DetailHandler().Handle(
            new GetPlatformClinicDetailQuery { ClinicId = ClinicId }, CancellationToken.None);

        var entry = Assert.Single(result.Value!.Payments);
        Assert.True(entry.IsCancelled);
        Assert.Equal("Montant saisi deux fois", entry.CancelReason);
        Assert.Equal("console|op", entry.CancelledBy);
        // It covers nothing, which is what the fold says about it — struck through on screen, and worth no days.
        Assert.Null(entry.CoversFrom);
        Assert.Null(entry.CoversThrough);
    }
}
