using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The portfolio read (<c>platform-console</c> US-2): what reaches the repository, what comes back, and the two
/// things the screen cannot get wrong without lying to the vendor — freshness (AC-2.8) and the admission that
/// subscriptions are not managed here yet.
///
/// <para><b>The filtering and sorting themselves are SQL and therefore out of this suite's reach</b> — a mocked
/// repository applies no predicate, so « hand it rows and assert the handler narrows them » would test a
/// capability the handler correctly does not have. What is still worth holding is that every argument arrives
/// <b>verbatim</b>: a silently dropped <c>dormant</c> or a sort the handler quietly rewrites is a real defect
/// nothing else in this project can see.</para>
/// </summary>
public class PlatformPortfolioQueryTests
{
    private readonly Mock<IClinicActivityRepository> _activity = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly ITenantScope _scope = SystemWideScope();

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private ListPlatformClinicsQueryHandler ListHandler() =>
        new(_activity.Object, _scope, NullLogger<ListPlatformClinicsQueryHandler>.Instance);

    private GetPlatformSummaryQueryHandler SummaryHandler() =>
        new(_activity.Object, _subscriptions.Object, _scope,
            NullLogger<GetPlatformSummaryQueryHandler>.Instance);

    private static PlatformClinicRow Row(
        string name = "Cabinet Ben Ali",
        int writes30d = 12,
        DateTime? computedAt = null,
        bool hasEntitlement = true,
        DateTime? endsOn = null,
        bool suspended = false,
        SubscriptionPeriodKind? coverKind = SubscriptionPeriodKind.Paid) =>
        new(Guid.NewGuid(), name, "Tunis", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            HasEntitlement: hasEntitlement,
            Plan: SubscriptionPlan.Cabinet,
            SubscriptionEndsOn: endsOn,
            SubscriptionIsSuspended: suspended,
            LatestCoverKind: coverKind,
            Users: 3, Patients: 412, Appointments30d: 96, Writes7d: 4, Writes30d: writes30d, ActiveDays30d: 9,
            LastWriteAt: new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
            LastLoginAt: new DateTime(2026, 8, 10, 7, 0, 0, DateTimeKind.Utc),
            CollectedThisMonth: 14320.000m,
            CountersComputedAt: computedAt);

    private void WirePage(params PlatformClinicRow[] rows) =>
        _activity.Setup(r => r.GetPortfolioAsync(
                It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlatformClinicRow>(rows, page: 1, pageSize: 25, totalCount: rows.Length));

    // [AC-2.3][AC-2.5] Every filter reaches the repository verbatim — including an UNTRIMMED term, because
    // normalisation belongs to SearchTerm inside the read and a handler that « helpfully » trims is a second
    // authority on what was typed.
    [Fact]
    public async Task Every_Filter_Reaches_The_Repository()
    {
        PlatformPortfolioFilter? captured = null;
        _activity.Setup(r => r.GetPortfolioAsync(
                It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPortfolioFilter f, PageRequest? _, CancellationToken _) => captured = f)
            .ReturnsAsync(new PagedResult<PlatformClinicRow>(Array.Empty<PlatformClinicRow>(), 1, 25, 0));

        await ListHandler().Handle(
            new ListPlatformClinicsQuery { Dormant = true, Q = "  Béchir  ", Sort = "activity" },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.True(captured!.DormantOnly);
        Assert.Equal(PlatformPortfolioSort.Activity, captured.Sort);
        // Folded and wrapped for SQL, wildcards escaped — the term the database will match on.
        Assert.Equal("%bechir%", captured.SearchPattern);
    }

    // [AC-2.4] An unrecognised sort falls back to `name` rather than refusing — the same tolerance the lab-order
    // stage filter and the audit action filter apply, so a stale bookmark shows rows instead of a French error.
    [Theory]
    [InlineData(null, PlatformPortfolioSort.Name)]
    [InlineData("", PlatformPortfolioSort.Name)]
    [InlineData("par date de fin", PlatformPortfolioSort.Name)]
    [InlineData("ACTIVITY", PlatformPortfolioSort.Activity)]
    [InlineData("createdAt", PlatformPortfolioSort.CreatedAt)]
    [InlineData("endsOn", PlatformPortfolioSort.EndsOn)]
    public async Task An_Unrecognised_Sort_Falls_Back_Rather_Than_Refusing(string? sort, PlatformPortfolioSort expected)
    {
        PlatformPortfolioFilter? captured = null;
        _activity.Setup(r => r.GetPortfolioAsync(
                It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPortfolioFilter f, PageRequest? _, CancellationToken _) => captured = f)
            .ReturnsAsync(new PagedResult<PlatformClinicRow>(Array.Empty<PlatformClinicRow>(), 1, 25, 0));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery { Sort = sort }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, captured!.Sort);
    }

    // [AC-2.4] Omitting the paging parameters gets the FIRST PAGE, not everything — deliberately the opposite of
    // the clinic app's list reads, and for the audit ledger's reason: « every cabinet at once » is a hosted
    // deployment's whole portfolio in one response, and no caller legitimately wants it.
    [Fact]
    public async Task Omitting_Paging_Reads_A_Page_Not_Everything()
    {
        PageRequest? captured = null;
        _activity.Setup(r => r.GetPortfolioAsync(
                It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPortfolioFilter _, PageRequest? p, CancellationToken _) => captured = p)
            .ReturnsAsync(new PagedResult<PlatformClinicRow>(Array.Empty<PlatformClinicRow>(), 1, 25, 0));

        await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Value.Page);
        Assert.Equal(PageRequest.DefaultPageSize, captured.Value.PageSize);
    }

    // [AC-2.4] An oversized page is clamped rather than refused, through PageRequest — the single authority.
    [Fact]
    public async Task An_Oversized_Page_Size_Is_Clamped()
    {
        PageRequest? captured = null;
        _activity.Setup(r => r.GetPortfolioAsync(
                It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPortfolioFilter _, PageRequest? p, CancellationToken _) => captured = p)
            .ReturnsAsync(new PagedResult<PlatformClinicRow>(Array.Empty<PlatformClinicRow>(), 1, 25, 0));

        await ListHandler().Handle(
            new ListPlatformClinicsQuery { Page = 2, PageSize = 100_000 }, CancellationToken.None);

        Assert.Equal(2, captured!.Value.Page);
        Assert.Equal(PageRequest.MaxPageSize, captured.Value.PageSize);
    }

    // [AC-2.8] Freshness is the OLDEST measurement on the page, so the sentence on screen is a floor. Taking the
    // newest would let one cabinet measured this morning vouch for thirty measured last week.
    [Fact]
    public async Task Freshness_Is_The_Oldest_Measurement_On_The_Page()
    {
        var older = new DateTime(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc);
        WirePage(Row(computedAt: newer), Row(computedAt: older));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        Assert.Equal(older, result.Value!.CountersAsOf);
    }

    // [EC-15] A portfolio whose counters were never written says so — `countersAsOf` is null and every row's own
    // `countersComputedAt` is null — rather than presenting zeros that read as « every cabinet is dormant ».
    [Fact]
    public async Task A_Portfolio_That_Was_Never_Measured_Says_So_Instead_Of_Reading_As_Dormant()
    {
        WirePage(Row(computedAt: null), Row(computedAt: null));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        Assert.Null(result.Value!.CountersAsOf);
        Assert.All(result.Value.Items, i => Assert.Null(i.CountersComputedAt));
    }

    // [EC-15] And a page mixing the two takes the measured one as its floor rather than falling to null: some of
    // these figures ARE measured, and saying « jamais mesuré » about the page would be the opposite error.
    [Fact]
    public async Task A_Mixed_Page_Reports_The_Measured_Floor()
    {
        var measured = new DateTime(2026, 8, 9, 3, 0, 0, DateTimeKind.Utc);
        WirePage(Row(computedAt: measured), Row(computedAt: null));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        Assert.Equal(measured, result.Value!.CountersAsOf);
    }

    // [FR-4][AC-2.1] The state is DERIVED by SubscriptionStateReader — the same rule the gate, the cabinet's own
    // screen and the warning job read — never by the console deciding what « expiré » means. This case is the one
    // that fails if a second answer is ever written here: a date in the past reads « Expiré » and surfaces no
    // countdown, exactly as the cabinet's own banner does.
    [Fact]
    public async Task A_Lapsed_Cabinet_Reads_Expired_With_No_Countdown()
    {
        WirePage(Row(endsOn: ClinicClock.ClinicToday().AddDays(-3)));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(nameof(SubscriptionState.Expired), row.State);
        Assert.Equal("Expiré", row.StateLabel);
        Assert.Null(row.DaysRemaining);
    }

    // [Q-1] « En essai » is a label on a covered cabinet, and it comes from the stored LatestCoverKind — the
    // clock-free denormalisation the SQL filter reads. Deriving it here from anything else would put a second
    // answer beside the one the portfolio is filtered by.
    [Fact]
    public async Task A_Cabinet_Whose_Latest_Cover_Is_The_Trial_Reads_Essai()
    {
        WirePage(Row(endsOn: ClinicClock.ClinicToday().AddDays(12), coverKind: SubscriptionPeriodKind.Trial));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(nameof(SubscriptionState.Trial), row.State);
        Assert.Equal(12, row.DaysRemaining);
    }

    // [FR-13] A cabinet with NO entitlement row is its own answer, in words. It is not « Expiré » — nobody chose
    // it and no payment explains it — and it is not « sans échéance », which is what a grandfathered cabinet reads.
    // Reading either as the other would report a fault as an arrangement or an arrangement as a fault.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Says_So_Rather_Than_Reading_As_Expired()
    {
        WirePage(Row(hasEntitlement: false, coverKind: null));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Null(row.State);
        Assert.Equal(PlatformClinicRowMapper.NoEntitlementLabel, row.StateLabel);
        Assert.Null(row.EndsOn);
    }

    // [EC-14] « Sans échéance » is an entitlement with no end date — Active for ever, and no countdown to show.
    [Fact]
    public async Task A_Never_Expiring_Cabinet_Is_Active_With_No_End_Date()
    {
        WirePage(Row(endsOn: null, coverKind: SubscriptionPeriodKind.Grandfathered));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(nameof(SubscriptionState.Active), row.State);
        Assert.Null(row.EndsOn);
        Assert.Null(row.DaysRemaining);
    }

    // [EC-11] Suspension outranks an expiry in the console exactly as it does in the gate: a suspended cabinet
    // whose date has also passed reads « Suspendu », because paying will not unblock it.
    [Fact]
    public async Task A_Suspended_Cabinet_Reads_Suspended_Even_When_Its_Date_Has_Passed()
    {
        WirePage(Row(endsOn: ClinicClock.ClinicToday().AddDays(-30), suspended: true));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        Assert.Equal(nameof(SubscriptionState.Suspended), Assert.Single(result.Value!.Items).State);
    }

    // [AC-2.3] The state filter reaches the repository verbatim, and an unrecognised value narrows NOTHING rather
    // than matching nothing — a stale bookmark should show the portfolio, not a deployment that appears to have
    // lost every client.
    [Theory]
    [InlineData("trial", PlatformSubscriptionFilter.Trial)]
    [InlineData("EXPIRED", PlatformSubscriptionFilter.Expired)]
    [InlineData("expiringSoon", PlatformSubscriptionFilter.ExpiringSoon)]
    [InlineData("suspended", PlatformSubscriptionFilter.Suspended)]
    [InlineData("missing", PlatformSubscriptionFilter.Missing)]
    [InlineData("en essai", null)]
    [InlineData(null, null)]
    public async Task The_State_Filter_Reaches_The_Repository_Or_Narrows_Nothing(
        string? state, PlatformSubscriptionFilter? expected)
    {
        PlatformPortfolioFilter? captured = null;
        _activity.Setup(r => r.GetPortfolioAsync(
                It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPortfolioFilter f, PageRequest? _, CancellationToken _) => captured = f)
            .ReturnsAsync(new PagedResult<PlatformClinicRow>(Array.Empty<PlatformClinicRow>(), 1, 25, 0));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery { State = state }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, captured!.Subscription);
    }

    // [AC-2.1] The activity figures, by contrast, are real and are carried through untouched.
    [Fact]
    public async Task The_Activity_Figures_Are_Carried_Through_Untouched()
    {
        WirePage(Row(computedAt: new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)));

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);
        var row = Assert.Single(result.Value!.Items);

        Assert.Equal(412, row.Patients);
        Assert.Equal(96, row.Appointments30d);
        Assert.Equal(12, row.Writes30d);
        Assert.Equal(9, row.ActiveDays30d);
        Assert.Equal(14320.000m, row.ClinicCollectedThisMonthDt);
    }

    // [AC-2.7] The summary is one read over the portfolio, and the vendor's revenue comes from the VENDOR's own
    // ledger — never from summing the cabinets' turnover, which would put the practices' takings on screen
    // labelled as the vendor's income. The two figures live on different repositories for exactly that reason.
    [Fact]
    public async Task The_Summary_Reports_Real_Counts_And_The_Vendors_Own_Revenue()
    {
        WireTotals(new PlatformPortfolioTotals(
            Clinics: 37, Dormant: 4, NeverMeasured: 2,
            InTrial: 5, Active: 24, ExpiringWithin14Days: 6, Expired: 6, Suspended: 1, NoEntitlement: 1));

        _subscriptions
            .Setup(r => r.GetVendorCollectedBetweenAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(9_450.000m);

        var result = await SummaryHandler().Handle(new GetPlatformSummaryQuery(), CancellationToken.None);

        Assert.Equal(37, result.Value!.Clinics);
        Assert.Equal(4, result.Value.Dormant);
        Assert.Equal(2, result.Value.NeverMeasured);
        Assert.Equal(9_450.000m, result.Value.VendorCollectedThisMonthDt);
    }

    // [AC-2.7] The five state buckets are mutually exclusive and sum to the portfolio. Without that a cabinet
    // counted in none — or in two — leaves a strip whose lines do not add up to the number above them, which is
    // the one property that makes the strip readable at a glance.
    [Fact]
    public async Task The_Five_State_Counts_Sum_To_The_Portfolio()
    {
        WireTotals(new PlatformPortfolioTotals(
            Clinics: 37, Dormant: 4, NeverMeasured: 2,
            InTrial: 5, Active: 24, ExpiringWithin14Days: 6, Expired: 6, Suspended: 1, NoEntitlement: 1));

        var summary = (await SummaryHandler().Handle(new GetPlatformSummaryQuery(), CancellationToken.None)).Value!;

        Assert.Equal(
            summary.Clinics,
            summary.InTrial + summary.Active + summary.Expired + summary.Suspended + summary.NoEntitlement);
    }

    // [AC-2.7] The window handed to the vendor-revenue read is the CLINIC's month, not UTC's — a payment recorded
    // at 00:30 on the 1st belongs to the month that has just opened, which is finding #20 one table over.
    [Fact]
    public async Task The_Vendor_Revenue_Window_Is_The_Clinic_Month()
    {
        WireTotals(new PlatformPortfolioTotals(0, 0, 0, 0, 0, 0, 0, 0, 0));

        DateTime from = default, to = default;
        _subscriptions
            .Setup(r => r.GetVendorCollectedBetweenAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((DateTime f, DateTime t, CancellationToken _) => (from, to) = (f, t))
            .ReturnsAsync(0m);

        await SummaryHandler().Handle(new GetPlatformSummaryQuery(), CancellationToken.None);

        var today = ClinicClock.ClinicToday();
        Assert.Equal(ClinicClock.StartOfLocalDayUtc(new DateTime(today.Year, today.Month, 1)), from);
        Assert.Equal(ClinicClock.LastTickOfLocalDayUtc(today), to);
    }

    private void WireTotals(PlatformPortfolioTotals totals) =>
        _activity.Setup(r => r.GetPortfolioTotalsAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

    // [EC-12] A console read that reached a handler with no cross-cabinet scope declared THROWS. It must not read
    // zero rows and report success: « je n'ai pas pu lire » and « aucun cabinet » are different answers, and the
    // silent version is the one that would have the vendor calling a full portfolio about churn.
    [Fact]
    public async Task A_Read_Without_A_Declared_Scope_Refuses_Instead_Of_Reading_Nothing()
    {
        var handler = new ListPlatformClinicsQueryHandler(
            _activity.Object, new TenantScope(NullLogger<TenantScope>.Instance),
            NullLogger<ListPlatformClinicsQueryHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ListPlatformClinicsQuery(), CancellationToken.None));

        _activity.Verify(r => r.GetPortfolioAsync(
            It.IsAny<PlatformPortfolioFilter>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
