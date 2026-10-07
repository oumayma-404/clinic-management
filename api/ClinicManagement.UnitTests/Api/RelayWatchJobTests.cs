using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>The cloud's minutely PC de secours watch (<c>clinic-pc-copy</c> AC-2.2, AC-9.2, AC-9.4).</summary>
public class RelayWatchJobTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IStaffNotificationRepository> _notifications = new();
    private readonly Mock<INotificationGenerator> _generator = new();
    private readonly Mock<IRelayIncidentRepository> _incidentRepo = new();
    private readonly Mock<IPlatformAccountRepository> _accounts = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITenantScope> _scope = new();
    private readonly List<RelayIncident> _incidents = new();
    private readonly Dictionary<string, string?> _settings = new();

    public RelayWatchJobTests()
    {
        _notifications.Setup(n => n.GetRelayAlertsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffNotification>());
        _notifications.Setup(n => n.GetClinicIdsWithRelayAlertsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        _incidentRepo.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _incidents.Where(i => i.IsOpen).ToList());
        _incidentRepo.Setup(r => r.AddAsync(It.IsAny<RelayIncident>(), It.IsAny<CancellationToken>()))
            .Callback<RelayIncident, CancellationToken>((i, _) => _incidents.Add(i))
            .Returns(Task.CompletedTask);
        _accounts.Setup(a => a.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "editeur@apexa.tn" });
        _email.SetupGet(e => e.IsConfigured).Returns(true);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionalEmailResult(TransactionalEmailOutcome.Sent, null));
    }

    private RelayWatchJob Job() => new(
        _relays.Object, _clinics.Object, _notifications.Object, _generator.Object,
        _incidentRepo.Object, _accounts.Object, _email.Object, _unitOfWork.Object,
        new ConfigurationBuilder().AddInMemoryCollection(_settings).Build(),
        Mock.Of<IAuditActorProvider>(), _scope.Object, NullLogger<RelayWatchJob>.Instance);

    private void Live(params ClinicRelay[] relays) =>
        _relays.Setup(r => r.GetLiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(relays);

    /// <summary>A PC that stopped copying because the cloud went back (AC-9.4) — told at any hour.</summary>
    private static ClinicRelay StoppedPc()
    {
        var now = DateTime.UtcNow;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicA, "PC-ACCUEIL", "local|admin", now.AddDays(-2));
        relay.Pair("PC-ACCUEIL", "key", null, null, null, now.AddDays(-2));
        relay.RecordHeartbeat(
            new RelayHeartbeat(30, 100, true, 0, 0, null, false, null, null, null, null, null, null, CopyStopped: true),
            10, now.AddSeconds(-5));
        return relay;
    }

    // A cabinet whose PC was retired has no live relay any more; its rows must still leave the bell.
    [Fact]
    public async Task The_Rows_Of_A_Cabinet_With_No_Live_Pc_Are_Withdrawn()
    {
        var (live, _) = ClinicRelay.BeginPairing(ClinicA, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        Live(live);
        _notifications.Setup(n => n.GetClinicIdsWithRelayAlertsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ClinicA, ClinicB });

        await Job().WatchRelays();

        _scope.Verify(s => s.UseSystemWide(It.IsAny<string>()), Times.Once);
        _generator.Verify(g => g.SyncRelayAlertsAsync(ClinicA, It.IsAny<IReadOnlyList<RelayAlertRow>>(), It.IsAny<CancellationToken>()), Times.Once);
        _generator.Verify(g => g.SyncRelayAlertsAsync(
            ClinicB, It.Is<IReadOnlyList<RelayAlertRow>>(w => w.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    // AC-9.2 / AC-9.4: one e-mail per problem, not one a minute — the open episode is the memory.
    [Fact]
    public async Task A_Problem_Emails_The_Vendor_Once_And_Is_Recorded()
    {
        Live(StoppedPc());

        await Job().WatchRelays();
        await Job().WatchRelays();

        var incident = Assert.Single(_incidents);
        Assert.Equal(RelayIncidentKind.Stopped, incident.Kind);
        Assert.True(incident.IsOpen);
        Assert.NotNull(incident.EmailedAtUtc);
        _email.Verify(e => e.SendAsync(
            "editeur@apexa.tn", It.Is<string>(s => s.Contains("copie arrêtée")), It.IsAny<EmailContent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Problem_That_Ended_Closes_Its_Episode_Without_An_Email()
    {
        var pc = StoppedPc();
        Live(pc);
        await Job().WatchRelays();
        _email.Invocations.Clear();

        pc.RecordHeartbeat(new RelayHeartbeat(30, 100, true, 0, 0, null, false, null, null, null, null, null), 30, DateTime.UtcNow);
        await Job().WatchRelays();

        Assert.False(Assert.Single(_incidents).IsOpen);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // A retired PC's open episode is closed: it no longer says anything about the cabinet.
    [Fact]
    public async Task The_Episode_Of_A_Pc_No_Longer_Watched_Is_Closed()
    {
        Live(StoppedPc());
        await Job().WatchRelays();

        Live();
        await Job().WatchRelays();

        Assert.False(Assert.Single(_incidents).IsOpen);
    }

    // Without SMTP the episode is still recorded (the console shows it), and nothing pretends it was e-mailed.
    [Fact]
    public async Task Without_Smtp_The_Episode_Is_Recorded_And_Not_Marked_Emailed()
    {
        _email.SetupGet(e => e.IsConfigured).Returns(false);
        Live(StoppedPc());

        await Job().WatchRelays();

        Assert.Null(Assert.Single(_incidents).EmailedAtUtc);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // The shared channel: the backup alerts' address, when the operator named one.
    [Fact]
    public async Task The_Named_Alert_Address_Is_Used()
    {
        _settings[VendorAlertRecipients.AlertEmailKey] = "alertes@apexa.tn";
        Live(StoppedPc());

        await Job().WatchRelays();

        _email.Verify(e => e.SendAsync("alertes@apexa.tn", It.IsAny<string>(), It.IsAny<EmailContent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(a => a.GetActiveEmailsAsync(It.IsAny<CancellationToken>()), Times.Never);
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
