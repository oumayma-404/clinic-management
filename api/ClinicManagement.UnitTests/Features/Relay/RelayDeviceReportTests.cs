using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// ⚠️ Inert since the takeover became manual (owner, 2026-10-12): silence no longer locks the cloud, so there is no
/// silent lock to unlock. What is kept is the domain rule itself. — AC-6.2, EC-17, EC-23 (<c>clinic-pc-copy</c>): a PC de
/// secours that goes silent without warning while the cabinet's internet works is unlocked by the cabinet's own apps — two « cloud yes, PC no » reports at least
/// 30 s apart, from the cabinet's network, with no device reaching the PC during that lock. A phone on mobile data, a
/// device at home, or one device that still reaches the PC never unlocks it: the PC may be about to take over.
/// </summary>
public class RelayDeviceReportTests
{
    private static readonly Guid ClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string CabinetLine = "197.15.20.30";
    private const string CabinetBox = "192.168.1.1";
    private static readonly string[] OnTheBox = { CabinetBox };

    private static RelayHeartbeat Beat(
        bool holding = false, DateTime? since = null, long underAck = 0, string? publicAddress = CabinetLine) =>
        new(10, 100, true, 0, 0, null, false, "build-1", null, "192.168.1.20", null, null, new string('A', 64), false,
            Holding: holding, HoldingSinceUtc: since, HoldingUnderAckSeq: underAck,
            HttpsPort: 5001, GatewayAddress: CabinetBox, PublicAddress: publicAddress);

    /// <summary>A ready PC on the cabinet's line, armed by an ack sent at <paramref name="t0"/> and confirmed, then silent.</summary>
    private static ClinicRelay ArmedThenSilent(DateTime t0)
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", t0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", t0.AddDays(-1));
        relay.RecordHeartbeat(Beat(), 10, t0);
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, t0), armed: true);
        return relay;
    }

    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    // ---- the domain ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Heartbeat_Records_Where_A_Device_Finds_The_Pc_And_The_Cabinets_Line()
    {
        var relay = ArmedThenSilent(T0);

        Assert.Equal(5001, relay.HttpsPort);
        Assert.Equal(CabinetBox, relay.GatewayAddress);
        Assert.Equal(CabinetLine, relay.PublicAddress);

        // A heartbeat that says nothing about them keeps what is known; an IPv4 seen through IPv6 is that IPv4.
        relay.RecordHeartbeat(Beat(publicAddress: "::ffff:197.15.20.31") with { HttpsPort = null, GatewayAddress = "nonsense" }, 10, T0.AddSeconds(5));
        Assert.Equal(5001, relay.HttpsPort);
        Assert.Equal(CabinetBox, relay.GatewayAddress);
        Assert.Equal("197.15.20.31", relay.PublicAddress);
    }

    [Theory]
    [InlineData(CabinetLine, true)]
    [InlineData("::ffff:197.15.20.30", true)]
    [InlineData("197.15.20.31", false)]   // the next address up: another subscriber
    [InlineData("41.230.1.2", false)]     // a phone on mobile data (EC-23)
    [InlineData("2001:db8::1", false)]    // a family mismatch never matches
    [InlineData(null, false)]
    [InlineData("unknown", false)]
    public void Only_The_Cabinets_Own_Internet_Line_Counts(string? caller, bool expected)
    {
        Assert.Equal(expected, ArmedThenSilent(T0).IsFromCabinetInternet(caller));
    }

    [Fact]
    public void On_Ipv6_Each_Device_Has_Its_Own_Address_So_The_Line_Is_The_Prefix()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", T0);
        relay.Pair("PC", "key", null, null, "build-1", T0);
        relay.RecordHeartbeat(Beat(publicAddress: "2001:db8:1:2:aaaa::10"), 10, T0);

        Assert.True(relay.IsFromCabinetInternet("2001:db8:1:2:bbbb::77"));
        Assert.False(relay.IsFromCabinetInternet("2001:db8:1:3::77"));
        Assert.False(relay.IsFromCabinetInternet(CabinetLine));
    }

    [Fact]
    public void The_Cabinets_Network_Is_Its_Line_And_Its_Box_Together()
    {
        var relay = ArmedThenSilent(T0);

        Assert.True(relay.IsOnCabinetNetwork(CabinetLine, new[] { "10.0.0.1", CabinetBox }));
        // A device at home behind the same kind of box: right gateway, wrong line.
        Assert.False(relay.IsOnCabinetNetwork("41.230.1.2", OnTheBox));
        // A guest Wi-Fi of the cabinet on its own subnet: right line, another box.
        Assert.False(relay.IsOnCabinetNetwork(CabinetLine, new[] { "192.168.50.1" }));
        Assert.False(relay.IsOnCabinetNetwork(CabinetLine, null));
        Assert.False(relay.IsOnCabinetNetwork(CabinetLine, Array.Empty<string>()));
    }

    [Fact]
    public void Two_Unreachable_Reports_Thirty_Seconds_Apart_Unlock_And_One_Alone_Never_Does()
    {
        var relay = ArmedThenSilent(T0);
        var locked = T0.AddSeconds(45);

        Assert.False(relay.RecordDeviceReport(false, locked, T0.AddSeconds(65)));
        Assert.False(relay.RecordDeviceReport(false, locked, T0.AddSeconds(80)));
        Assert.True(relay.RecordDeviceReport(false, locked, T0.AddSeconds(95)));
    }

    // One device reaching the PC during the lock is evidence the PC is alive — and may be about to take over. It blocks
    // the unlock for the rest of this lock, never just until the next report: a tablet on an isolated Wi-Fi
    // interleaving with one that reaches the PC must not open the cloud beside a PC in charge.
    [Fact]
    public void A_Device_That_Reaches_The_Pc_Blocks_The_Unlock_For_The_Whole_Lock()
    {
        var relay = ArmedThenSilent(T0);
        var locked = T0.AddSeconds(45);

        Assert.False(relay.RecordDeviceReport(false, locked, T0.AddSeconds(65)));
        Assert.False(relay.RecordDeviceReport(true, locked, T0.AddSeconds(70)));
        Assert.False(relay.RecordDeviceReport(false, locked, T0.AddSeconds(100)));
        Assert.False(relay.RecordDeviceReport(false, locked, T0.AddMinutes(10)));
    }

    // A new lock (the PC came back, then fell silent again) starts the device facts afresh — including the block.
    [Fact]
    public void A_New_Lock_Starts_The_Device_Facts_Afresh()
    {
        var relay = ArmedThenSilent(T0);
        var first = T0.AddSeconds(45);
        relay.RecordDeviceReport(true, first, T0.AddSeconds(70));

        var back = T0.AddMinutes(5);
        relay.RecordHeartbeat(Beat(), 10, back);
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, back), armed: true);
        var second = back.AddSeconds(45);
        Assert.NotEqual(first, second);

        Assert.False(relay.RecordDeviceReport(false, second, back.AddSeconds(65)));
        Assert.True(relay.RecordDeviceReport(false, second, back.AddSeconds(95)));
        Assert.Null(relay.DevicesReachedPcAtUtc);
    }

    // ---- the command ------------------------------------------------------------------------------------------------

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<IAuditEntryRepository> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<AuditEntry> _journal = new();

    private ReportRelayDeviceCommandHandler Handler(ClinicRelay? relay)
    {
        _context.Setup(c => c.GetClinicId()).Returns(ClinicId);
        _relays.Setup(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        _audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => _journal.AddRange(e))
            .Returns(Task.CompletedTask);
        return new ReportRelayDeviceCommandHandler(_context.Object, _relays.Object, _audit.Object, _unitOfWork.Object,
            NullLogger<ReportRelayDeviceCommandHandler>.Instance);
    }

    // The PC said it holds the saves: only its return, a retirement or an admin's « Reprendre la main » frees the cloud.
    [Fact]
    public async Task A_Pc_That_Said_It_Holds_The_Saves_Is_Never_Unlocked_By_Devices()
    {
        var now = DateTime.UtcNow;
        var relay = ArmedThenSilent(now.AddMinutes(-10));
        relay.RecordHeartbeat(Beat(holding: true, since: now.AddMinutes(-8), underAck: relay.LastAckSeq), 10, now.AddMinutes(-7));

        var handler = Handler(relay);
        await handler.Handle(new ReportRelayDeviceCommand(false, OnTheBox, CabinetLine), default);
        var result = await handler.Handle(new ReportRelayDeviceCommand(false, OnTheBox, CabinetLine), default);

        Assert.Equal(new RelayDeviceReportDto(false, false), result.Value);
        Assert.NotNull(relay.PcHoldingSinceUtc);
        Assert.Null(relay.DevicesUnreachableFirstAtUtc);
    }

    // Manual takeover (owner, 2026-10-12): a silent PC no longer locks the cloud, so there is nothing to unlock.
    [Fact]
    public async Task Nothing_Is_Counted_While_The_Cloud_Is_Not_Locked()
    {
        var now = DateTime.UtcNow;
        var relay = ArmedThenSilent(now.AddMinutes(-10));

        var result = await Handler(relay).Handle(new ReportRelayDeviceCommand(false, OnTheBox, CabinetLine), default);

        Assert.Equal(new RelayDeviceReportDto(false, false), result.Value);
        Assert.Null(relay.DevicesUnreachableFirstAtUtc);
    }

    // ---- the target --------------------------------------------------------------------------------------------------

    // Silence never locks the cloud any more, so a device is never sent to look for a silent PC.
    [Fact]
    public void A_Device_Is_Never_Told_To_Try_A_Silent_Pc()
    {
        var relay = ArmedThenSilent(T0);

        Assert.False(GetRelayDeviceTargetQueryHandler.For(relay, CabinetLine, T0.AddMinutes(30)).Probe);
        var none = GetRelayDeviceTargetQueryHandler.For(null, CabinetLine, T0);
        Assert.False(none.Probe);
        Assert.Equal(GetRelayDeviceTargetQueryHandler.IdleIntervalSeconds, none.IntervalSeconds);
    }
}
