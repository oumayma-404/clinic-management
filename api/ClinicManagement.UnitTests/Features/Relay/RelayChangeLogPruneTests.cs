using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The cloud's change log is pruned (<c>clinic-pc-copy</c> D27). What is held: only rows the PC already holds go, its
/// own position's row stays (the feed's fingerprint), nothing goes during a cut, a stopped copy or a restore gap, a
/// position from another history prunes nothing, and one cabinet's failure costs no other cabinet its pass.
/// </summary>
public class RelayChangeLogPruneTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime T0 = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private static RelayHeartbeat Beat(long applied, bool seeded = true, bool stopped = false, bool holding = false,
        DateTime? since = null, long underAck = 0) =>
        new(applied, seeded ? 100 : 40, seeded, 0, 0, null, false, "build-1", null, null, null, null, null, stopped,
            Holding: holding, HoldingSinceUtc: since, HoldingUnderAckSeq: underAck);

    private static ClinicRelay Paired(Guid clinicId, long applied, bool seeded = true)
    {
        var (relay, _) = ClinicRelay.BeginPairing(clinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-30));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-30));
        relay.RecordHeartbeat(Beat(applied, seeded), 500, T0);
        return relay;
    }

    // ---- the rule ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_Caught_Up_Pc_Lets_The_Log_Go_Below_Its_Own_Position()
    {
        Assert.Equal(400, Paired(ClinicA, 400).ChangeLogPrunableBelow(cloudHighWater: 500));
        Assert.Equal(500, Paired(ClinicA, 500).ChangeLogPrunableBelow(cloudHighWater: 500));
    }

    [Fact]
    public void Nothing_Goes_Before_The_First_Copy_Or_Before_Any_Position()
    {
        Assert.Null(Paired(ClinicA, 400, seeded: false).ChangeLogPrunableBelow(500));
        Assert.Null(Paired(ClinicA, 0).ChangeLogPrunableBelow(500));
    }

    // A seq above the cloud's high-water comes from another history (a restored cloud): it says nothing about this log.
    [Fact]
    public void A_Position_Above_The_High_Water_Prunes_Nothing()
    {
        Assert.Null(Paired(ClinicA, 900).ChangeLogPrunableBelow(500));
    }

    [Fact]
    public void A_Stopped_Copy_Prunes_Nothing()
    {
        var relay = Paired(ClinicA, 400);
        relay.RecordHeartbeat(Beat(400, stopped: true), 500, T0.AddMinutes(1));

        Assert.Null(relay.ChangeLogPrunableBelow(500));
    }

    // During a cut the return reads the cloud's log after the PC's position: kept whole until the PC is back.
    [Fact]
    public void A_Cut_Prunes_Nothing()
    {
        var relay = Paired(ClinicA, 400);
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        relay.RecordHeartbeat(Beat(400, holding: true, since: T0.AddMinutes(2), underAck: relay.LastAckSeq), 500, T0.AddMinutes(3));
        Assert.NotNull(relay.PcHoldingSinceUtc);

        Assert.Null(relay.ChangeLogPrunableBelow(500));
    }

    [Fact]
    public void A_Restore_Gap_Prunes_Nothing()
    {
        var relay = Paired(ClinicA, 400);
        relay.NoteFollowedEpoch("old", "new", pcAppliedSeq: 400, T0);

        Assert.Null(relay.ChangeLogPrunableBelow(500));
    }

    [Fact]
    public void A_Retired_Pc_Prunes_Nothing()
    {
        var relay = Paired(ClinicA, 400);
        relay.Retire(ClinicManagement.Domain.Enums.ClinicRelayRetirement.Retired, "local|admin", T0.AddMinutes(1));

        Assert.Null(relay.ChangeLogPrunableBelow(500));
    }

    // ---- the job ----------------------------------------------------------------------------------------------------

    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<IClinicRelayRowStore> _rows = new();
    private readonly Mock<ITenantScope> _scope = new();

    private PruneClinicChangesJob Job() => new(
        _relays.Object, _rows.Object, Mock.Of<IAuditActorProvider>(), _scope.Object, NullLogger<PruneClinicChangesJob>.Instance);

    [Fact]
    public async Task The_Pass_Prunes_Each_Cabinet_Below_Its_Pc_And_Older_Than_Seven_Days()
    {
        _relays.Setup(r => r.GetLiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Paired(ClinicA, 400), Paired(ClinicB, 0) });
        _rows.Setup(r => r.HighWaterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(500);

        await Job().PruneClinicChanges(T0);

        _scope.Verify(s => s.UseSystemWide(It.IsAny<string>()), Times.Once);
        _rows.Verify(r => r.PruneChangesAsync(ClinicA, 400, T0.AddDays(-7), It.IsAny<CancellationToken>()), Times.Once);
        _rows.Verify(r => r.PruneChangesAsync(ClinicB, It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task One_Cabinets_Failure_Costs_No_Other_Cabinet_Its_Pass()
    {
        _relays.Setup(r => r.GetLiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Paired(ClinicA, 400), Paired(ClinicB, 300) });
        _rows.Setup(r => r.HighWaterAsync(ClinicA, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        _rows.Setup(r => r.HighWaterAsync(ClinicB, It.IsAny<CancellationToken>())).ReturnsAsync(500);

        await Job().PruneClinicChanges(T0);

        _rows.Verify(r => r.PruneChangesAsync(ClinicB, 300, T0.AddDays(-7), It.IsAny<CancellationToken>()), Times.Once);
    }
}
