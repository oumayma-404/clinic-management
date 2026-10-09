using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// FR-2's one predicate (<c>clinic-pc-copy</c>): the state « Paramètres », the bell and the vendor console all read.
/// Each case is one row of the state table, from a fixed instant.
/// </summary>
public class ClinicRelayHealthTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private static RelayHeartbeat Beat(
        long applied = 10, bool seedComplete = true, string? error = null, IReadOnlyList<string>? mismatch = null,
        long? diskFree = 100L * 1024 * 1024 * 1024, bool updating = false) =>
        new(applied, 100, seedComplete, 0, 0, diskFree, updating, "1.4.0", null, null, mismatch, error);

    private static ClinicRelay Active(DateTime lastBeat, long applied = 10, long highWater = 10, RelayHeartbeat? beat = null)
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0);
        relay.Pair("PC-ACCUEIL", "key", null, null, null, T0);
        relay.RecordHeartbeat(Beat(applied: highWater), highWater, T0.AddMinutes(1));
        relay.RecordHeartbeat(beat ?? Beat(applied: applied), highWater, lastBeat);
        return relay;
    }

    private static ClinicRelayState StateOf(ClinicRelay? relay, DateTime now) => ClinicRelayHealth.Read(relay, now).State;

    [Fact]
    public void No_Relay_Is_None()
    {
        Assert.Equal(ClinicRelayState.None, StateOf(null, T0));
    }

    [Fact]
    public void A_Caught_Up_Relay_Heard_Recently_Is_Ready()
    {
        var now = T0.AddMinutes(10);
        var reading = ClinicRelayHealth.Read(Active(now.AddSeconds(-10)), now);

        Assert.Equal(ClinicRelayState.Ready, reading.State);
        Assert.False(reading.IsProblem);
    }

    [Fact]
    public void A_Relay_Silent_For_Over_Two_Minutes_Is_Off()
    {
        var now = T0.AddMinutes(10);

        Assert.Equal(ClinicRelayState.Off, StateOf(Active(now - ClinicRelayHealth.SilentAfter - TimeSpan.FromSeconds(1)), now));
        Assert.Equal(ClinicRelayState.Ready, StateOf(Active(now - ClinicRelayHealth.SilentAfter), now));
    }

    [Fact]
    public void A_Relay_Behind_For_Over_Two_Minutes_Is_Late()
    {
        var relay = Active(T0.AddMinutes(1), applied: 10, highWater: 10);
        relay.RecordHeartbeat(Beat(applied: 10), highWater: 50, T0.AddMinutes(2));
        relay.RecordHeartbeat(Beat(applied: 12), highWater: 50, T0.AddMinutes(4));

        var reading = ClinicRelayHealth.Read(relay, T0.AddMinutes(4).AddSeconds(5));

        Assert.Equal(ClinicRelayState.Late, reading.State);
        Assert.Equal(T0.AddMinutes(1), reading.Since);
        Assert.True(reading.IsProblem);
    }

    // Briefly behind is ordinary — a save just landed — and is not a problem.
    [Fact]
    public void A_Relay_Briefly_Behind_Is_Still_Ready()
    {
        var relay = Active(T0.AddMinutes(1));
        relay.RecordHeartbeat(Beat(applied: 10), highWater: 11, T0.AddMinutes(2));

        Assert.Equal(ClinicRelayState.Ready, StateOf(relay, T0.AddMinutes(2).AddSeconds(5)));
    }

    [Fact]
    public void Updating_Outranks_Late_And_Mismatch()
    {
        var now = T0.AddMinutes(10);
        var relay = Active(now.AddSeconds(-5), beat: Beat(updating: true, mismatch: new[] { "Patient" }));

        Assert.Equal(ClinicRelayState.Updating, StateOf(relay, now));
    }

    // [D10b] The installer stops the PC's services: « Mise à jour » stays its state through a bounded silence, and a
    // PC that never comes back from it reads « Éteint » like any other.
    [Fact]
    public void An_Updating_Pc_Is_Updating_Through_Its_Restart_And_Off_After_It()
    {
        var now = T0.AddHours(1);
        var silentTen = Active(now.AddMinutes(-10), beat: Beat(updating: true));
        var silentTooLong = Active(now - ClinicRelayHealth.UpdateSilenceAllowed - TimeSpan.FromSeconds(1), beat: Beat(updating: true));
        var notUpdating = Active(now.AddMinutes(-10));

        Assert.Equal(ClinicRelayState.Updating, StateOf(silentTen, now));
        Assert.False(ClinicRelayHealth.Read(silentTen, now).IsProblem);
        Assert.Equal(ClinicRelayState.Off, StateOf(silentTooLong, now));
        Assert.Equal(ClinicRelayState.Off, StateOf(notUpdating, now));
    }

    [Fact]
    public void An_Unrepaired_Difference_Is_Mismatch()
    {
        var now = T0.AddMinutes(10);
        var relay = Active(now.AddSeconds(-5), beat: Beat(mismatch: new[] { "Invoice" }));

        var reading = ClinicRelayHealth.Read(relay, now);

        Assert.Equal(ClinicRelayState.Mismatch, reading.State);
        Assert.True(reading.IsProblem);
    }

    [Fact]
    public void Less_Than_Five_Gigabytes_Free_Is_Disk_Nearly_Full()
    {
        var now = T0.AddMinutes(10);
        var relay = Active(now.AddSeconds(-5), beat: Beat(diskFree: ClinicRelayHealth.DiskNearlyFullBytes - 1));

        Assert.Equal(ClinicRelayState.DiskNearlyFull, StateOf(relay, now));
    }

    [Fact]
    public void A_Setup_In_Progress_Is_Installing_With_Its_Percentage()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", T0);
        relay.Pair("PC", "key", null, null, null, T0);
        relay.RecordHeartbeat(new RelayHeartbeat(0, 37, false, 0, 0, null, false, null, null, null, null, null), 0, T0.AddMinutes(1));

        var reading = ClinicRelayHealth.Read(relay, T0.AddMinutes(2));

        Assert.Equal(ClinicRelayState.Installing, reading.State);
        Assert.Equal(37, reading.SeedPercent);
    }

    [Fact]
    public void A_Setup_Reporting_An_Error_Is_Install_Failed()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", T0);
        relay.Pair("PC", "key", null, null, null, T0);
        relay.RecordHeartbeat(Beat(seedComplete: false, error: "Disque plein"), 0, T0.AddMinutes(1));

        Assert.Equal(ClinicRelayState.InstallFailed, StateOf(relay, T0.AddMinutes(2)));
    }

    // AC-9.4: a copy stopped by a cloud that went back holds MORE than the cloud, so « caught up » would say « Prêt ».
    [Fact]
    public void A_Copy_Stopped_By_A_Cloud_That_Went_Back_Is_Stopped_Not_Ready()
    {
        var now = T0.AddMinutes(10);
        var relay = Active(now.AddSeconds(-20), applied: 40, highWater: 10,
            beat: Beat(applied: 40) with { CopyStopped = true });
        relay.RecordHeartbeat(Beat(applied: 40) with { CopyStopped = true }, 10, now.AddSeconds(-10));

        var reading = ClinicRelayHealth.Read(relay, now);
        Assert.Equal(ClinicRelayState.Stopped, reading.State);
        Assert.Equal(now.AddSeconds(-20), reading.Since);
        Assert.True(reading.IsProblem);

        relay.RecordHeartbeat(Beat(applied: 40), 40, now);
        Assert.Equal(ClinicRelayState.Ready, StateOf(relay, now));
    }

    // A lapsed, never-used code read « Copie en cours (0 %) » on the card for ever.
    [Fact]
    public void A_Code_That_Lapsed_Unused_Is_No_Pc_At_All()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", T0);

        Assert.Equal(ClinicRelayState.Installing, StateOf(relay, T0.AddMinutes(1)));
        Assert.Equal(ClinicRelayState.None, StateOf(relay, T0 + ClinicRelay.PairingCodeLifetime));
    }

    [Fact]
    public void A_Setup_Silent_For_A_Day_Is_Abandoned()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", T0);
        relay.Pair("PC", "key", null, null, null, T0);

        Assert.Equal(ClinicRelayState.Abandoned, StateOf(relay, T0 + ClinicRelay.AbandonedAfter));
    }

    [Theory]
    [InlineData(ClinicRelayRetirement.Retired, ClinicRelayState.Retired)]
    [InlineData(ClinicRelayRetirement.Abandoned, ClinicRelayState.Abandoned)]
    public void A_Retired_Row_Reads_As_Its_Reason(ClinicRelayRetirement reason, ClinicRelayState expected)
    {
        var relay = Active(T0.AddMinutes(2));
        relay.Retire(reason, "local|admin", T0.AddMinutes(3));

        var reading = ClinicRelayHealth.Read(relay, T0.AddMinutes(4));

        Assert.Equal(expected, reading.State);
        Assert.Equal(T0.AddMinutes(3), reading.Since);
    }
}
