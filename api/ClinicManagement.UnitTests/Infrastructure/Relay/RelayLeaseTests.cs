using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// The PC de secours's half of the write lease (<c>clinic-pc-copy</c> D13): the file that says it holds the cabinet's
/// saves, and the decision to take them. The cases are the ones that fail silently — a PC that forgets it is in charge
/// after a restart, one that takes over because it was busy or asleep, and a copy erased with a cut's work on it.
/// </summary>
public sealed class RelayLeaseTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-lease-" + Guid.NewGuid().ToString("N"));
    private readonly RelayFollowerStateStore _states;
    private readonly FakeBox _box = new();
    private DateTime _now = T0;
    private TimeSpan _mono = TimeSpan.FromMinutes(30);

    public RelayLeaseTests()
    {
        _states = new RelayFollowerStateStore(_dir);
        _states.Save(new RelayFollowerState { Epoch = "e1", AppliedSeq = 40, RowsSeededAtUtc = T0.AddDays(-1) });
    }

    private RelayLease Lease(TimeSpan? exchangeTimeout = null) => new(_dir, () => _now, () => _mono, exchangeTimeout);

    private RelayLeaseKeeper Keeper(RelayLease lease) => new(lease, _states, _box, NullLogger.Instance);

    private void Advance(double seconds)
    {
        _now = _now.AddSeconds(seconds);
        _mono += TimeSpan.FromSeconds(seconds);
    }

    private static Task<RelayCall<RelayHeartbeatAck>> Answer(long seq, bool armed) =>
        Task.FromResult(new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Ok,
            new RelayHeartbeatAck(T0, 40, "e1", false, false, "build-1", seq, armed)));

    private static Task<RelayCall<RelayHeartbeatAck>> NoAnswer() =>
        Task.FromResult(new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Unreachable));

    /// <summary>An armed ack at T0, then the cloud asked again and silent — the shape of a real cut.</summary>
    private async Task<RelayLease> ArmedThenCutAsync()
    {
        var lease = Lease();
        await lease.ExchangeAsync(_ => Answer(1001, armed: true), default);
        Advance(10);
        await lease.ExchangeAsync(_ => NoAnswer(), default);
        return lease;
    }

    // ---- the takeover (FR-3, D13) ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_Armed_Pc_Whose_Cloud_Stopped_Answering_Takes_Over_At_Ninety_Seconds()
    {
        var lease = await ArmedThenCutAsync();
        var keeper = Keeper(lease);

        Advance(79.9);
        Assert.False(await keeper.TickAsync(default));
        Assert.False(lease.IsHolding);

        Advance(0.1);
        Assert.True(await keeper.TickAsync(default));
        Assert.Equal(_now, lease.HoldingSinceUtc);
        Assert.Equal(1001, lease.Current.HoldingUnderAckSeq);
    }

    // [AC-3.8] A PC that was not ready at its last contact never takes over.
    [Fact]
    public async Task A_Pc_Last_Told_It_Was_Not_Armed_Never_Takes_Over()
    {
        var lease = Lease();
        await lease.ExchangeAsync(_ => Answer(1001, armed: false), default);
        await lease.ExchangeAsync(_ => NoAnswer(), default);

        Advance(600);

        Assert.False(await Keeper(lease).TickAsync(default));
    }

    // [AC-6.6] Cut off from the cabinet's box (cable out), it never takes over — the tablets may still reach the cloud.
    [Fact]
    public async Task A_Pc_That_Cannot_Reach_The_Box_Never_Takes_Over()
    {
        var lease = await ArmedThenCutAsync();
        _box.Answers = false;

        Advance(600);

        Assert.False(await Keeper(lease).TickAsync(default));
        Assert.Equal(1, _box.Asked);
    }

    // A PC that slept, or whose copy tick was busy, has an old ack without the cloud being gone: it must ASK first. The
    // first heartbeat after it wakes is answered, and nothing changes hands.
    [Fact]
    public async Task An_Old_Ack_Alone_Is_Not_A_Cut_The_Cloud_Must_Have_Been_Asked()
    {
        var lease = Lease();
        await lease.ExchangeAsync(_ => Answer(1001, armed: true), default);

        Advance(3600);

        Assert.False(await Keeper(lease).TickAsync(default));
        Assert.Equal(0, _box.Asked);

        await lease.ExchangeAsync(_ => Answer(1002, armed: true), default);
        Assert.False(await Keeper(lease).TickAsync(default));
        Assert.False(lease.UnansweredSinceLastAck);
    }

    // A copy that is not seeded, stopped (D12) or retired never takes over, armed or not.
    [Theory]
    [InlineData("unseeded")]
    [InlineData("stopped")]
    [InlineData("released")]
    public async Task A_Copy_That_Is_Not_A_Sound_Copy_Never_Takes_Over(string why)
    {
        var lease = await ArmedThenCutAsync();
        var state = _states.Load();
        _states.Save(why switch
        {
            "unseeded" => state with { RowsSeededAtUtc = null },
            "stopped" => state with { StoppedReason = "went back" },
            _ => state with { Released = true },
        });

        Advance(600);

        Assert.False(await Keeper(lease).TickAsync(default));
    }

    // A heartbeat that hangs is a heartbeat not answered: bounded, and counted as silence.
    [Fact]
    public async Task A_Heartbeat_That_Hangs_Is_Cut_Short_And_Counts_As_Unanswered()
    {
        var lease = Lease(exchangeTimeout: TimeSpan.FromMilliseconds(50));
        await lease.ExchangeAsync(_ => Answer(1001, armed: true), default);

        var call = await lease.ExchangeAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Ok);
        }, default).WaitAsync(TimeSpan.FromMinutes(1)); // generous: a busy test run delays timers, not the rule

        Assert.Equal(RelayCallStatus.Unreachable, call.Status);
        Assert.True(lease.UnansweredSinceLastAck);
    }

    // The cloud answering « released » is an answer: it is not silence, and the follower handles it.
    [Fact]
    public async Task A_Released_Answer_Is_Not_Silence()
    {
        var lease = Lease();
        await lease.ExchangeAsync(_ => Answer(1001, armed: true), default);
        await lease.ExchangeAsync(_ => Task.FromResult(new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Released)), default);

        Assert.False(lease.UnansweredSinceLastAck);
    }

    // ---- the clock -------------------------------------------------------------------------------------------------

    // An ack this process received is timed on the monotonic clock: a wall clock jumping forward an hour changes nothing.
    [Fact]
    public async Task A_Wall_Clock_Jump_Never_Makes_The_Pc_Take_Over_Early()
    {
        var lease = await ArmedThenCutAsync();

        _now = _now.AddHours(1);

        Assert.False(await Keeper(lease).TickAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(10), lease.SinceLastAckReceived());
    }

    // [AC-3.11] After a restart the ack is only known by the wall clock, so it is capped by how long the process has
    // run: a restarted PC waits its full 90 s of silence again, never less.
    [Fact]
    public async Task After_A_Restart_The_Silence_Is_Counted_Again_From_The_Start()
    {
        var before = Lease();
        await before.ExchangeAsync(_ => Answer(1001, armed: true), default);
        Advance(600);

        var uptime = TimeSpan.Zero;
        var restarted = new RelayLease(_dir, () => _now, () => uptime);
        await restarted.ExchangeAsync(_ => NoAnswer(), default);

        Assert.False(await Keeper(restarted).TickAsync(default));

        uptime = TimeSpan.FromSeconds(90);
        Assert.True(await Keeper(restarted).TickAsync(default));
    }

    // ---- the file (AC-3.11) ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Pc_In_Charge_Is_Still_In_Charge_After_A_Restart()
    {
        var lease = await ArmedThenCutAsync();
        lease.TakeOver();

        var restarted = new RelayLease(_dir, () => _now, () => TimeSpan.Zero);

        Assert.True(restarted.IsHolding);
        Assert.Equal(lease.HoldingSinceUtc, restarted.HoldingSinceUtc);
    }

    // Forgetting would let the copy loop overwrite the cut's work; holding by mistake only keeps the cloud read-only.
    [Fact]
    public void An_Unreadable_Lease_File_Reads_As_Holding()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, RelayLease.FileName), "{ not json");

        Assert.True(Lease().IsHolding);
    }

    [Fact]
    public void A_Fresh_Pc_Holds_Nothing()
    {
        var lease = Lease();

        Assert.False(lease.IsHolding);
        Assert.False(lease.HoldsUnreturnedWork);
    }

    // ---- the cut's work is never thrown away (AC-7.3, AC-8.6) --------------------------------------------------------

    // Retired while it held the saves: it stops accepting them, and the work it holds is marked as never returned.
    [Fact]
    public async Task Released_While_Holding_It_Lets_Go_And_Keeps_The_Work_Marked()
    {
        var lease = await ArmedThenCutAsync();
        lease.TakeOver();
        var since = lease.HoldingSinceUtc;
        _states.Save(_states.Load() with { Released = true });

        await Keeper(lease).TickAsync(default);

        Assert.False(lease.IsHolding);
        Assert.True(lease.HoldsUnreturnedWork);
        Assert.Equal(since, lease.Current.UnreturnedSinceUtc);
        Assert.True(new RelayLease(_dir).HoldsUnreturnedWork);
    }

    [Fact]
    public async Task A_New_Pairing_Over_The_Cuts_Work_Is_Refused()
    {
        var lease = await ArmedThenCutAsync();
        lease.TakeOver();
        lease.End();

        var refused = Assert.Throws<InvalidOperationException>(lease.ResetForNewPairing);
        Assert.Equal(RelayRefusals.CutWorkKept, refused.Message);
    }

    [Fact]
    public async Task A_New_Pairing_Starts_Unarmed()
    {
        var lease = Lease();
        await lease.ExchangeAsync(_ => Answer(1001, armed: true), default);

        lease.ResetForNewPairing();

        Assert.Equal(new RelayLeaseState(), lease.Current);
        Assert.Equal(new RelayLeaseState(), new RelayLease(_dir).Current);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FakeBox : IRelayBoxProbe
    {
        public bool Answers { get; set; } = true;
        public int Asked { get; private set; }

        public Task<bool> AnswersAsync(CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(Answers);
        }
    }
}
