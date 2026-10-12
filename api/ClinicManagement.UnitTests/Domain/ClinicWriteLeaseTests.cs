using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Who may record a cabinet's work (<c>clinic-pc-copy</c> FR-3, D13, D14). Since the takeover became manual (owner,
/// 2026-10-12) the cloud locks only on the PC's word that it holds the saves — never on silence — and numbers stay
/// unique through D16. The lossy-line simulation of « never both writable » went with the automatic takeover it tested.
/// </summary>
public class ClinicWriteLeaseTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    private static ClinicRelay NewRelay()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-1));
        return relay;
    }

    private static bool Fenced(ClinicRelay relay, DateTime now) => ClinicWriteLease.IsCloudFenced(relay, now);

    // ---- manual takeover (owner, 2026-10-12) ------------------------------------------------------------------

    // A laptop asleep, a closed lid, a dead PC: silence alone never locks the cloud any more — however long, armed or not.
    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(14 * 3600)]
    public void Silence_Alone_Never_Locks_The_Cloud(int secondsLater)
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        relay.IssueAck(armed: true, T0.AddSeconds(10));

        Assert.False(Fenced(relay, T0.AddSeconds(secondsLater)));
        Assert.Null(ClinicWriteLease.LockedSinceUtc(relay, T0.AddSeconds(secondsLater)));
        // …while the numbers still wait for the PC's word (D16), so a number is never issued twice.
        Assert.True(ClinicWriteLease.PcMustConfirmNumbers(relay));
    }

    // Once the PC said it holds the saves (the button was pressed), the cloud is locked from the PC's own moment.
    [Fact]
    public void The_Cloud_Locks_Only_Once_The_Pc_Said_It_Holds()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        relay.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(3)), 10, T0.AddMinutes(4));

        Assert.True(Fenced(relay, T0.AddMinutes(4)));
        Assert.Equal(T0.AddMinutes(3), ClinicWriteLease.LockedSinceUtc(relay, T0.AddMinutes(4)));
    }

    // ---- the PC in charge (D13) ------------------------------------------------------------------------------------

    private static RelayHeartbeat Beat(bool holding, DateTime? since = null) =>
        new(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null, Holding: holding, HoldingSinceUtc: since);

    // [AC-4.2] Once the PC said it holds the saves, nothing about the acks makes the cloud writable again: a heartbeat
    // arriving as the line heals is no evidence the PC let go.
    [Fact]
    public void A_Pc_That_Said_It_Holds_Keeps_The_Cloud_Fenced_Whatever_The_Acks_Say()
    {
        var relay = NewRelay();
        relay.RecordHeartbeat(Beat(holding: true, since: T0), 10, T0.AddSeconds(5));
        relay.RecordAckConfirmation(relay.IssueAck(armed: false, T0.AddSeconds(5)), armed: false);
        relay.RecordHeartbeat(Beat(holding: false), 10, T0.AddSeconds(15));

        Assert.False(relay.MayBeArmed);
        Assert.True(Fenced(relay, T0.AddSeconds(16)));
        Assert.True(Fenced(relay, T0.AddDays(3)));
    }

    // Retiring is how the cloud takes the cabinet back (AC-8.6): the fence goes with the PC.
    [Fact]
    public void A_Retired_Pc_Fences_Nothing_Even_If_It_Held_The_Saves()
    {
        var relay = NewRelay();
        relay.RecordHeartbeat(Beat(holding: true, since: T0), 10, T0.AddSeconds(5));
        relay.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddMinutes(1));

        Assert.False(Fenced(relay, T0.AddMinutes(2)));
    }

    // The PC's own moment is kept — « depuis 10:42 » is when the cabinet started on it — but never in the cloud's future.
    [Theory]
    [InlineData(-120, -120)]
    [InlineData(300, 0)]
    public void The_Takeover_Moment_Is_The_Pcs_Own_But_Never_After_The_Clouds_Now(int pcSaysSeconds, int recordedSeconds)
    {
        var relay = NewRelay();
        var now = T0.AddMinutes(10);

        relay.RecordHeartbeat(Beat(holding: true, since: now.AddSeconds(pcSaysSeconds)), 10, now);
        relay.RecordHeartbeat(Beat(holding: true, since: now.AddSeconds(-600)), 10, now.AddSeconds(10));

        Assert.Equal(now.AddSeconds(recordedSeconds), relay.PcHoldingSinceUtc);
    }

    // ---- how long since the PC's last ack (D13) ------------------------------------------------------------------

    [Fact]
    public void An_Ack_Received_By_This_Process_Is_Timed_On_The_Monotonic_Clock()
    {
        var since = ClinicWriteLease.SinceLastAckReceived(T0.AddHours(5), T0, TimeSpan.FromSeconds(12), TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromSeconds(12), since);
    }

    // After a restart only the wall clock knows: capped by how long the process has run, never over-counted.
    [Theory]
    [InlineData(600, 30, 30)]
    [InlineData(40, 300, 40)]
    [InlineData(-60, 300, 0)]
    public void An_Ack_From_Before_A_Restart_Is_Capped_By_The_Uptime(int wallAgoSeconds, int uptimeSeconds, int expected)
    {
        var since = ClinicWriteLease.SinceLastAckReceived(
            T0, T0.AddSeconds(-wallAgoSeconds), null, TimeSpan.FromSeconds(uptimeSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expected), since);
    }

    [Fact]
    public void No_Ack_Ever_Received_Is_No_Time_At_All()
    {
        Assert.Equal(TimeSpan.Zero, ClinicWriteLease.SinceLastAckReceived(T0, null, null, TimeSpan.FromHours(2)));
    }

    // [AC-6.1, D14] A PC that stood down properly and then went quiet never locks the cabinet.
    [Fact]
    public void A_Pc_That_Stood_Down_Leaves_The_Cloud_Writable_However_Long_It_Is_Gone()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        var ask = relay.IssueAck(armed: false, T0.AddSeconds(10));
        relay.RecordAckConfirmation(ask, armed: false);
        relay.IssueAck(armed: false, T0.AddSeconds(11));

        Assert.False(Fenced(relay, T0.AddHours(3)));
    }

    // ---- the cloud's clock ----------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_Fences_A_Cabinet_Whose_Pc_Was_Never_Armed_Or_Is_Retired_Or_Absent()
    {
        var neverArmed = NewRelay();
        neverArmed.RecordAckConfirmation(neverArmed.IssueAck(armed: false, T0), armed: false);
        var retired = NewRelay();
        retired.RecordAckConfirmation(retired.IssueAck(armed: true, T0), armed: true);
        retired.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddSeconds(5));

        Assert.False(Fenced(neverArmed, T0.AddHours(1)));
        Assert.False(Fenced(retired, T0.AddHours(1)));
        Assert.False(ClinicWriteLease.IsCloudFenced(null, T0.AddHours(1)));
    }

    // The silence clock may only move forward, and only to an ack the cloud really sent.
    [Fact]
    public void An_Older_Or_Unsent_Ack_Changes_Nothing()
    {
        var relay = NewRelay();
        var first = relay.IssueAck(armed: true, T0);
        var second = relay.IssueAck(armed: true, T0.AddSeconds(10));
        relay.RecordAckConfirmation(second, armed: true);

        relay.RecordAckConfirmation(first, armed: false);
        relay.RecordAckConfirmation(second + TimeSpan.TicksPerHour, armed: false);

        Assert.Equal(second, relay.ConfirmedAckSeq);
        Assert.True(relay.ConfirmedAckArmed);
    }

    // Each ack's id is its send instant, strictly increasing even when two land in one tick or the clock steps back.
    [Fact]
    public void Ack_Ids_Only_Ever_Increase()
    {
        var relay = NewRelay();
        var a = relay.IssueAck(armed: true, T0);
        var b = relay.IssueAck(armed: true, T0);
        var c = relay.IssueAck(armed: true, T0.AddMinutes(-5));

        Assert.Equal(T0.Ticks, a);
        Assert.True(b > a && c > b);
    }

    // ---- the PC's rule and the arming rule ------------------------------------------------------------------------

    private static ClinicRelay ReadyRelay(DateTime now)
    {
        var relay = NewRelay();
        relay.RecordHeartbeat(new RelayHeartbeat(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null), 10, now);
        return relay;
    }

    [Fact]
    public void Only_A_Ready_Pc_On_The_Clouds_Build_That_Did_Not_Ask_To_Stand_Down_Is_Armed()
    {
        var now = T0.AddMinutes(1);

        Assert.True(ClinicWriteLease.ShouldArm(ReadyRelay(now), sameBuild: true, wantsToStandDown: false, now));
        Assert.False(ClinicWriteLease.ShouldArm(ReadyRelay(now), sameBuild: false, wantsToStandDown: false, now));
        Assert.False(ClinicWriteLease.ShouldArm(ReadyRelay(now), sameBuild: true, wantsToStandDown: true, now));

        var behind = ReadyRelay(now);
        behind.RecordHeartbeat(new RelayHeartbeat(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null), 500, now.AddMinutes(5));
        Assert.False(ClinicWriteLease.ShouldArm(behind, true, false, now.AddMinutes(5)));

        var seeding = NewRelay();
        Assert.False(ClinicWriteLease.ShouldArm(seeding, true, false, now));
    }
}
