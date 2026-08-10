using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Common;
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
        new(_activity.Object, _scope, NullLogger<GetPlatformSummaryQueryHandler>.Instance);

    private static PlatformClinicRow Row(
        string name = "Cabinet Ben Ali",
        int writes30d = 12,
        DateTime? computedAt = null) =>
        new(Guid.NewGuid(), name, "Tunis", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
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
    [InlineData("endsOn", PlatformPortfolioSort.Name)]
    [InlineData("ACTIVITY", PlatformPortfolioSort.Activity)]
    [InlineData("createdAt", PlatformPortfolioSort.CreatedAt)]
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

    // [FR-4] Entitlement is the companion feature's, and until it ships the console says so instead of guessing.
    // Every one of the four members is null — never a defaulted « Actif » or a zero « days remaining », either of
    // which would be the console asserting something about a cabinet's right to work.
    [Fact]
    public async Task Subscription_Is_Reported_As_Unavailable_Rather_Than_Guessed()
    {
        WirePage(Row());

        var result = await ListHandler().Handle(new ListPlatformClinicsQuery(), CancellationToken.None);

        Assert.False(result.Value!.SubscriptionDataAvailable);
        var row = Assert.Single(result.Value.Items);
        Assert.Null(row.Plan);
        Assert.Null(row.State);
        Assert.Null(row.EndsOn);
        Assert.Null(row.DaysRemaining);
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

    // [AC-2.7] The summary is one read over the portfolio, and its vendor-revenue figure is absent rather than
    // approximated: summing the cabinets' own turnover would put the practices' takings on screen labelled as the
    // vendor's income, which is the one confusion AC-2.7 exists to forbid.
    [Fact]
    public async Task The_Summary_Reports_Real_Counts_And_No_Invented_Vendor_Revenue()
    {
        _activity.Setup(r => r.GetPortfolioTotalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPortfolioTotals(Clinics: 37, Dormant: 4, NeverMeasured: 2));

        var result = await SummaryHandler().Handle(new GetPlatformSummaryQuery(), CancellationToken.None);

        Assert.Equal(37, result.Value!.Clinics);
        Assert.Equal(4, result.Value.Dormant);
        Assert.Equal(2, result.Value.NeverMeasured);
        Assert.Null(result.Value.VendorCollectedThisMonthDt);
        Assert.False(result.Value.SubscriptionDataAvailable);
    }

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
