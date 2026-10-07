using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>The cloud's minutely PC de secours watch (<c>clinic-pc-copy</c> AC-2.2).</summary>
public class RelayWatchJobTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IStaffNotificationRepository> _notifications = new();
    private readonly Mock<INotificationGenerator> _generator = new();
    private readonly Mock<ITenantScope> _scope = new();

    private RelayWatchJob Job() => new(
        _relays.Object, _clinics.Object, _notifications.Object, _generator.Object,
        Mock.Of<IAuditActorProvider>(), _scope.Object, NullLogger<RelayWatchJob>.Instance);

    // A cabinet whose PC was retired has no live relay any more; its rows must still leave the bell.
    [Fact]
    public async Task The_Rows_Of_A_Cabinet_With_No_Live_Pc_Are_Withdrawn()
    {
        var (live, _) = ClinicRelay.BeginPairing(ClinicA, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        _relays.Setup(r => r.GetLiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { live });
        _notifications.Setup(n => n.GetRelayAlertsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffNotification>());
        _notifications.Setup(n => n.GetClinicIdsWithRelayAlertsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ClinicA, ClinicB });

        await Job().WatchRelays();

        _scope.Verify(s => s.UseSystemWide(It.IsAny<string>()), Times.Once);
        _generator.Verify(g => g.SyncRelayAlertsAsync(ClinicA, It.IsAny<IReadOnlyList<RelayAlertRow>>(), It.IsAny<CancellationToken>()), Times.Once);
        _generator.Verify(g => g.SyncRelayAlertsAsync(
            ClinicB, It.Is<IReadOnlyList<RelayAlertRow>>(w => w.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    // A code that lapsed unused sits beside the PC actually installed: the installed one is the one watched.
    [Fact]
    public void The_Pc_Holding_The_Place_Is_Watched_Before_A_Lapsed_Code()
    {
        var t0 = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
        var (installed, _) = ClinicRelay.BeginPairing(ClinicA, "PC-ACCUEIL", "local|admin", t0);
        installed.Pair("PC-ACCUEIL", "key", null, null, null, t0);
        var (lapsed, _) = ClinicRelay.BeginPairing(ClinicA, "PC-2", "local|admin", t0.AddHours(1));

        var watched = Assert.Single(RelayWatchJob.CurrentPerClinic(new[] { installed, lapsed }, t0.AddHours(2)));

        Assert.Equal(installed.Id, watched.Id);
    }
}
