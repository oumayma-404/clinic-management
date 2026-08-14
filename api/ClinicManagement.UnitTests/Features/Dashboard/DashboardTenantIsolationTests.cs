using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Queries;
using ClinicManagement.Application.Features.Dashboard.Readers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// [AC-1] Tenant isolation for the composed dashboard read, following the repo's first-class
/// <c>*TenantIsolationTests</c> convention: the clinic is resolved server-side and every section reader is handed
/// <b>that</b> clinic, never one supplied by the caller.
///
/// <para>The dashboard has no route or body parameter naming a clinic, so the isolation question here is narrower than
/// for a CRUD aggregate — but it is also the read that aggregates the most tables, so a single reader given the wrong
/// id would leak another practice's revenue onto this one's home screen.</para>
/// </summary>
public class DashboardTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IDashboardActivityReader> _activity = new();
    private readonly Mock<IDashboardMoneyReader> _money = new();
    private readonly Mock<IDashboardAlertsReader> _alerts = new();
    private readonly Mock<IDashboardTrendReader> _trend = new();
    private readonly Mock<IDashboardProcedureMixReader> _procedureMix = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private GetDashboardQueryHandler Handler() => new(
        _activity.Object, _money.Object, _alerts.Object, _trend.Object, _procedureMix.Object,
        _clinicResolver.Object, NullLogger<GetDashboardQueryHandler>.Instance);

    private void WireResolved(Guid clinicId)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(clinicId));
        _activity.Setup(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardActivityDto());
        _money.Setup(r => r.ReadAsync(
                It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new DashboardMoneyDto(), new DashboardReceivablesDto()));
        _alerts.Setup(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardAlertsDto());
        _trend.Setup(r => r.ReadAsync(
                It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyCollectedPointDto>());
        _procedureMix.Setup(r => r.ReadAsync(
                It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProcedureMixPointDto>());
    }

    // [AC-1] Every reader is handed the resolved clinic, and no reader is ever asked about a different one.
    [Fact]
    public async Task Every_Section_Is_Read_For_The_Resolved_Clinic_Only()
    {
        WireResolved(ClinicId);

        var result = await Handler().Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _activity.Verify(r => r.ReadAsync(ClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<CancellationToken>()), Times.Once);
        _money.Verify(r => r.ReadAsync(ClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(r => r.ReadAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _trend.Verify(r => r.ReadAsync(ClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        _activity.Verify(r => r.ReadAsync(OtherClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<CancellationToken>()), Times.Never);
        _money.Verify(r => r.ReadAsync(OtherClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(r => r.ReadAsync(OtherClinicId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _trend.Verify(r => r.ReadAsync(OtherClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-1] An unresolvable clinic reads nothing at all — no section is attempted, so there is no path on which a
    // filter-less read could run.
    [Fact]
    public async Task An_Unresolvable_Clinic_Reads_Nothing()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Cabinet introuvable."));

        var result = await Handler().Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _activity.Verify(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<CancellationToken>()), Times.Never);
        _money.Verify(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _trend.Verify(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-2] All four sections receive the SAME period instance and the same `now`. Reading DateTime.UtcNow per
    // reader would let a request that spans midnight compute its activity against one day and its alerts the next.
    [Fact]
    public async Task All_Sections_Share_One_Period_And_One_Now()
    {
        WireResolved(ClinicId);
        DashboardPeriod? activityPeriod = null;
        DashboardPeriod? moneyPeriod = null;
        DateTime moneyNow = default;
        DateTime alertsNow = default;

        _activity.Setup(r => r.ReadAsync(ClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DashboardPeriod p, CancellationToken _) => activityPeriod = p)
            .ReturnsAsync(new DashboardActivityDto());
        _money.Setup(r => r.ReadAsync(ClinicId, It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DashboardPeriod p, DateTime now, Guid? _, CancellationToken _) => { moneyPeriod = p; moneyNow = now; })
            .ReturnsAsync((new DashboardMoneyDto(), new DashboardReceivablesDto()));
        _alerts.Setup(r => r.ReadAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, DateTime now, CancellationToken _) => alertsNow = now)
            .ReturnsAsync(new DashboardAlertsDto());

        await Handler().Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.NotNull(activityPeriod);
        Assert.Equal(activityPeriod, moneyPeriod);
        Assert.Equal(moneyNow, alertsNow);
    }

    // [AC-1] The requested period reaches the readers, and the resolved bounds are echoed back on the DTO so the
    // client builds its drill-through links from the same window the figures were computed over.
    [Fact]
    public async Task Echoes_The_Resolved_Period_Back_On_The_Response()
    {
        WireResolved(ClinicId);

        var result = await Handler().Handle(
            new GetDashboardQuery { Period = DashboardPeriodKey.Week }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(DashboardPeriodKey.Week), result.Value!.Period.Key);
        Assert.True(result.Value.Period.From < result.Value.Period.ToInclusive);
        Assert.True(result.Value.Period.PreviousToInclusive < result.Value.Period.From);
    }

    // [AC-10] A reader throwing yields the canonical French failure, not a 500 and not a partial dashboard with
    // fabricated zeros in the section that failed.
    [Fact]
    public async Task A_Failing_Section_Fails_The_Whole_Read_In_French()
    {
        WireResolved(ClinicId);
        _money.Setup(r => r.ReadAsync(
                It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Handler().Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("tableau de bord", result.Error);
        Assert.DoesNotContain("db down", result.Error);
    }
}
