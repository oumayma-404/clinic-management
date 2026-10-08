using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The heartbeat's half of the lease (<c>clinic-pc-copy</c> D13, D14): what the PC confirms is recorded before the new
/// ack is issued, the ack is saved before it is answered, and it arms only a ready PC on the cloud's build.
/// </summary>
public class RelayHeartbeatLeaseTests
{
    private const string Build = "20261008_Lease+1.0.0+aaaaaaaaaaaa";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<string> _order = new();

    private static ClinicRelay ReadyRelay()
    {
        var now = DateTime.UtcNow;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", now.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, Build, now.AddDays(-1));
        relay.RecordHeartbeat(new RelayHeartbeat(40, 100, true, 0, 0, null, false, Build, null, null, null, null), 40, now.AddSeconds(-10));
        return relay;
    }

    private async Task<RelayHeartbeatAck> BeatAsync(
        ClinicRelay relay, string build = Build, bool standDown = false, long confirmed = 0, bool confirmedArmed = false,
        bool holding = false, DateTime? holdingSince = null)
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(relay.Subject);
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetByIdAcrossClinicsAsync(relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        var rows = new Mock<IClinicRelayRowStore>();
        rows.Setup(r => r.HighWaterAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(40);
        rows.Setup(r => r.FeedEpochAsync(It.IsAny<CancellationToken>())).ReturnsAsync("e1");
        var buildInfo = new Mock<IRelayBuildInfo>();
        buildInfo.Setup(b => b.Current).Returns(Build);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _order.Add($"saved ack {relay.LastAckSeq}"))
            .ReturnsAsync(1);

        var handler = new RelayHeartbeatCommandHandler(context.Object, relays.Object, new TenantScope(NullLogger<TenantScope>.Instance), rows.Object,
            buildInfo.Object, new Mock<IAuditEntryRepository>().Object, _unitOfWork.Object,
            NullLogger<RelayHeartbeatCommandHandler>.Instance);

        var result = await handler.Handle(new RelayHeartbeatCommand(new RelayHeartbeatRequest(
            40, 100, true, 0, 0, null, false, build, DateTime.UtcNow, null, null, null, null,
            ConfirmedAckSeq: confirmed, ConfirmedAckArmed: confirmedArmed, WantsToStandDown: standDown,
            Holding: holding, HoldingSinceUtc: holdingSince)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task A_Ready_Pc_On_The_Clouds_Build_Is_Armed_And_The_Ack_Is_Saved_Before_It_Is_Answered()
    {
        var relay = ReadyRelay();

        var ack = await BeatAsync(relay);

        Assert.True(ack.Armed);
        Assert.True(ack.AckSeq > 0);
        Assert.Equal(ack.AckSeq, relay.LastAckSeq);
        Assert.Equal($"saved ack {ack.AckSeq}", Assert.Single(_order));
    }

    [Fact]
    public async Task A_Pc_On_Another_Build_Or_Standing_Down_Is_Not_Armed()
    {
        Assert.False((await BeatAsync(ReadyRelay(), build: "20261001_Old+1.0.0+bbbbbbbbbbbb")).Armed);
        Assert.False((await BeatAsync(ReadyRelay(), standDown: true)).Armed);
    }

    // D14: the next heartbeat confirms the ack; a stand-down confirmed this way is what frees the cloud.
    [Fact]
    public async Task The_Confirmation_Is_Recorded_And_A_Confirmed_Stand_Down_Unfences()
    {
        var relay = ReadyRelay();
        var armed = await BeatAsync(relay);
        var ask = await BeatAsync(relay, standDown: true, confirmed: armed.AckSeq, confirmedArmed: true);
        await BeatAsync(relay, standDown: true, confirmed: ask.AckSeq, confirmedArmed: false);

        Assert.Equal(ask.AckSeq, relay.ConfirmedAckSeq);
        Assert.False(relay.ConfirmedAckArmed);
        Assert.False(relay.MayBeArmed);
        Assert.False(ClinicWriteLease.IsCloudFenced(relay, DateTime.UtcNow.AddHours(1)));
    }

    // [D13] The PC's word that it holds the saves is recorded with the heartbeat and fences the cloud at once — and a
    // stand-down asked afterwards cannot undo it.
    [Fact]
    public async Task A_Pc_Saying_It_Holds_The_Saves_Fences_The_Cloud_For_Good()
    {
        var relay = ReadyRelay();
        var since = DateTime.UtcNow.AddMinutes(-3);

        await BeatAsync(relay, holding: true, holdingSince: since);
        var ask = await BeatAsync(relay, standDown: true);
        await BeatAsync(relay, standDown: true, confirmed: ask.AckSeq, confirmedArmed: false);

        Assert.Equal(since, relay.PcHoldingSinceUtc);
        Assert.True(ClinicWriteLease.IsCloudFenced(relay, DateTime.UtcNow));
    }

    // [AC-4.2] « depuis 10:42 » is the cabinet's clock; a takeover from another day says which day.
    [Theory]
    [InlineData("2026-10-08T09:42:00Z", "2026-10-08T15:00:00Z", "10:42")]
    [InlineData("2026-10-08T23:30:00Z", "2026-10-09T08:00:00Z", "00:30")]
    [InlineData("2026-10-06T09:42:00Z", "2026-10-08T09:00:00Z", "le 06/10 à 10:42")]
    public void The_Takeover_Time_Is_Said_On_The_Cabinets_Clock(string since, string now, string expected)
    {
        Assert.Equal(expected, RelayRefusals.SinceClinicTime(
            DateTime.Parse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
            DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal)));
    }
}
