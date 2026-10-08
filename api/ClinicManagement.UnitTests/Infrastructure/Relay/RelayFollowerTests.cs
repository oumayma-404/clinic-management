using System.IO.Compression;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Infrastructure.Relay;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// One PC de secours tick (<c>clinic-pc-copy</c> Part 1 « Copy »), driven against a fake cloud, a fake database and a
/// fake disk. The cases are the ones a copy can get wrong silently: following another history, moving the position
/// past rows it did not apply, re-downloading the whole cabinet every ten seconds, and a file that never arrives.
/// </summary>
public sealed class RelayFollowerTests : IDisposable
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-follower-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCloud _cloud = new();
    private readonly FakeRows _rows = new();
    private readonly Mock<IFileStorage> _files = new();
    private readonly HashSet<string> _onDisk = new(StringComparer.Ordinal);
    private readonly List<string> _restored = new();
    private DateTime _now = T0;
    private TimeSpan _mono = TimeSpan.FromHours(1);
    private readonly RelayFollowerStateStore _store;
    private readonly RelayLease _lease;
    private readonly (string Public, byte[] Private) _keys;
    private readonly FakeInstaller _installer = new();
    private readonly FakeLauncher _launcher = new();

    public RelayFollowerTests()
    {
        _store = new RelayFollowerStateStore(_dir);
        _lease = new RelayLease(_dir, () => _now, () => _mono);
        _keys = RelaySecretEnvelope.NewKeyPair();
        _files.Setup(f => f.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _onDisk.Contains(key));
        _files.Setup(f => f.RestoreAtKeyAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((_, _, key, _) => { _onDisk.Add(key); _restored.Add(key); })
            .Returns(Task.CompletedTask);
    }

    private RelayFollower Follower(string build = "build-1")
    {
        var secrets = new Mock<IUserSecretProtector>();
        secrets.Setup(s => s.Protect(It.IsAny<string>())).Returns((string plain) => "ring:" + plain);
        var credentials = new RelayCredentials(Guid.NewGuid(), ClinicId, "Cabinet", "https://cloud.example.tn", "secret",
            Convert.ToBase64String(_keys.Private), T0);
        return new RelayFollower(_cloud, _store, credentials, secrets.Object, build,
            () => new RelayHostReport(new[] { "192.168.1.10" }, "FP", 100L * 1024 * 1024 * 1024, 5001, "192.168.1.1"),
            new RelayUpdater(_installer, _launcher, Path.Combine(_dir, "updates"), Path.Combine(_dir, "logs"),
                NullLogger.Instance, () => _now),
            _lease, NullLogger.Instance, () => _now);
    }

    private void Advance(double seconds)
    {
        _now = _now.AddSeconds(seconds);
        _mono += TimeSpan.FromSeconds(seconds);
    }

    private RelayLocalSide Local => new(_rows, _rows, _files.Object);

    private Task<RelayFollowerState> TickAsync(RelayFollower? follower = null) =>
        (follower ?? Follower()).TickAsync(Local, CancellationToken.None);

    private void Seeded(string epoch = "e1", long seq = 40, int filesTotal = 0) =>
        _store.Save(new RelayFollowerState
        {
            Epoch = epoch, AppliedSeq = seq, HeadFingerprint = "h" + seq, RowsSeededAtUtc = T0,
            FilesTotal = filesTotal, FilesCopied = filesTotal, LastDigestAtUtc = _now,
        });

    // ---- the first copy ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Fresh_Pc_Takes_The_Whole_Copy_And_Remembers_Where_It_Stands()
    {
        _cloud.Snapshot = ("e1", 42, "h42");

        var state = await TickAsync();

        Assert.Equal(new string?[] { null }, _rows.Replaced.Select(r => r is null ? null : string.Join(",", r)));
        Assert.Equal("e1", state.Epoch);
        Assert.Equal(42, state.AppliedSeq);
        Assert.Equal("h42", state.HeadFingerprint);
        Assert.Equal(state, _store.Load() with { MismatchTables = state.MismatchTables });
    }

    // A whole copy is the heaviest call there is: a failing one is not repeated every ten seconds.
    [Fact]
    public async Task A_Failed_First_Copy_Waits_Before_The_Next_Attempt()
    {
        _cloud.Snapshot = ("e1", 42, "h42");
        _rows.ReplaceThrows = true;

        var first = await TickAsync();
        _now = T0.AddMinutes(1);
        await TickAsync();

        Assert.Equal(1, _cloud.SnapshotCalls);
        Assert.NotNull(first.LastError);
        Assert.False(first.RowsSeeded);

        _rows.ReplaceThrows = false;
        _now = T0 + RelayFeedDecisions.SeedRetryAfterFailure;
        var later = await TickAsync();

        Assert.Equal(2, _cloud.SnapshotCalls);
        Assert.True(later.RowsSeeded);
        Assert.Null(later.LastError);
    }

    // ---- following -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Pc_Behind_The_Cloud_Pulls_From_Its_Position_With_Its_Fingerprint()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 45;
        _cloud.Batches.Enqueue(new RelayFeedBatch("e1", 40, 45, "h45", RelayFeedOutcome.Ok,
            new[] { new RelayRow("Patient", "k1", null) }));

        var state = await TickAsync();

        Assert.Equal((40, "h40"), _cloud.ChangesAsked.Single());
        Assert.Single(_rows.Applied);
        Assert.Equal(45, state.AppliedSeq);
        Assert.Equal("h45", state.HeadFingerprint);
    }

    [Fact]
    public async Task A_Pc_Caught_Up_Asks_For_Nothing()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;

        await TickAsync();

        Assert.Empty(_cloud.ChangesAsked);
    }

    // [D12] The batch says another history: nothing is applied, the position stays, and the copy stops for good.
    [Fact]
    public async Task A_Batch_From_Another_History_Stops_The_Copy_And_Applies_Nothing()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 45;
        _cloud.Batches.Enqueue(new RelayFeedBatch("e2", 40, 45, "h45", RelayFeedOutcome.Ok, new[] { new RelayRow("Patient", "k", null) }));

        var state = await TickAsync();

        Assert.Empty(_rows.Applied);
        Assert.Equal(40, state.AppliedSeq);
        Assert.Equal(RelayFeedDecisions.WentBackReason, state.StoppedReason);

        // The next tick only reports; it reads nothing more from that cloud.
        _cloud.Batches.Enqueue(new RelayFeedBatch("e2", 40, 46, "h46", RelayFeedOutcome.Ok, Array.Empty<RelayRow>()));
        await TickAsync();
        Assert.Single(_cloud.ChangesAsked);
        Assert.Equal(RelayFeedDecisions.WentBackReason, _cloud.Reports.Last().LastError);
        // A flag, not the sentence: the cloud must never recover the stop by matching French prose.
        Assert.True(_cloud.Reports.Last().CopyStopped);
        Assert.False(_cloud.Reports.First().CopyStopped);
    }

    [Fact]
    public async Task The_Clouds_Went_Back_Verdict_Stops_The_Copy()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 45;
        _cloud.Batches.Enqueue(new RelayFeedBatch("e1", 40, 45, null, RelayFeedOutcome.WentBack, Array.Empty<RelayRow>()));

        var state = await TickAsync();

        Assert.NotNull(state.StoppedReason);
        Assert.Empty(_rows.Applied);
    }

    // [D12] Found on the first end-to-end run: a cloud restored to an earlier state answers the heartbeat with a
    // high-water BELOW this copy's, and may not overtake it for days. The copy stops on that first answer.
    [Fact]
    public async Task A_Heartbeat_From_Behind_This_Copy_Stops_It_At_Once()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 12;

        var state = await TickAsync();

        Assert.Equal(RelayFeedDecisions.WentBackReason, state.StoppedReason);
        Assert.Equal(40, state.AppliedSeq);
        Assert.Empty(_cloud.ChangesAsked);
        Assert.Equal(0, _cloud.SnapshotCalls);
    }

    [Fact]
    public async Task A_Heartbeat_From_Another_History_Stops_The_Copy_At_Once()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.AckEpoch = "e2";

        var state = await TickAsync();

        Assert.NotNull(state.StoppedReason);
        Assert.Empty(_cloud.ChangesAsked);
    }

    // Before the first copy there is nothing to protect: a fresh PC follows whatever cloud answers.
    [Fact]
    public async Task A_Fresh_Pc_Is_Not_Stopped_By_A_Low_High_Water()
    {
        _cloud.HighWater = 0;
        _cloud.Snapshot = ("e1", 0, null);

        var state = await TickAsync();

        Assert.Null(state.StoppedReason);
        Assert.True(state.RowsSeeded);
    }

    // [D6b] Too far behind for one batch: the whole copy is taken again, in the same tick.
    [Fact]
    public async Task A_Backlog_Too_Long_Is_Taken_As_A_Whole_Copy()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 90_000;
        _cloud.Batches.Enqueue(new RelayFeedBatch("e1", 40, 90_000, "h", RelayFeedOutcome.ReseedRequired, Array.Empty<RelayRow>()));
        _cloud.Snapshot = ("e1", 90_000, "h90000");

        var state = await TickAsync();

        Assert.Single(_rows.Replaced);
        Assert.Equal(90_000, state.AppliedSeq);
        Assert.False(state.ReseedNeeded);
    }

    // [D12] A re-seed toward a cloud that is behind this copy is refused before a single row is replaced.
    [Fact]
    public async Task A_Reseed_From_A_Cloud_Behind_This_Copy_Replaces_Nothing()
    {
        _store.Save(new RelayFollowerState { Epoch = "e1", AppliedSeq = 40, RowsSeededAtUtc = T0, ReseedNeeded = true });
        _cloud.HighWater = 40; // the heartbeat is in step; only the snapshot is behind
        _cloud.Snapshot = ("e1", 12, "h12");

        var state = await TickAsync();

        Assert.Empty(_rows.Replaced);
        Assert.NotNull(state.StoppedReason);
        Assert.Equal(40, state.AppliedSeq);
    }

    // A batch that will not apply keeps the position where it was and asks for a whole copy next tick.
    [Fact]
    public async Task A_Batch_That_Does_Not_Apply_Keeps_The_Position_And_Owes_A_Reseed()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 45;
        _cloud.Batches.Enqueue(new RelayFeedBatch("e1", 40, 45, "h45", RelayFeedOutcome.Ok, new[] { new RelayRow("Patient", "k", null) }));
        _rows.ApplyThrows = true;

        var state = await TickAsync();

        Assert.Equal(40, state.AppliedSeq);
        Assert.True(state.ReseedNeeded);
        Assert.NotNull(state.LastError);

        _rows.ApplyThrows = false;
        _cloud.Snapshot = ("e1", 45, "h45");
        var next = await TickAsync();

        Assert.Single(_rows.Replaced);
        Assert.Equal(45, next.AppliedSeq);
    }

    // ---- the cloud's answers -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Pc_The_Cloud_Released_Stops_Calling_It()
    {
        Seeded();
        _cloud.HeartbeatStatus = RelayCallStatus.Released;

        var state = await TickAsync();
        _now = _now.AddMinutes(5);
        var later = await TickAsync();

        Assert.True(state.Released);
        Assert.Single(_cloud.Reports);
        // « Copie arrêtée le … » (AC-8.1): the moment it learned, kept — not moved by later ticks.
        Assert.Equal(T0, state.ReleasedAtUtc);
        Assert.Equal(T0, later.ReleasedAtUtc);
    }

    // AC-8.2: an erased PC tells the cloud until the cloud has heard — an unplugged PC tells it when plugged back in —
    // and never calls anything else again.
    [Fact]
    public async Task An_Erased_Pc_Reports_It_Until_The_Cloud_Has_Heard()
    {
        _store.Save(new RelayFollowerState { Released = true, ErasedAtUtc = T0 });
        _cloud.ErasedStatus = RelayCallStatus.Unreachable;

        var first = await TickAsync();
        Assert.False(first.ErasureReported);

        _cloud.ErasedStatus = RelayCallStatus.Ok;
        var second = await TickAsync();
        await TickAsync();

        Assert.True(second.ErasureReported);
        Assert.Equal(2, _cloud.ErasedReports);
        Assert.Empty(_cloud.Reports);
    }

    // No answer is not evidence of anything: the state is left exactly as it was.
    [Fact]
    public async Task An_Unreachable_Cloud_Changes_Nothing()
    {
        Seeded(seq: 40);
        _cloud.HeartbeatStatus = RelayCallStatus.Unreachable;

        var state = await TickAsync();

        Assert.Equal(40, state.AppliedSeq);
        Assert.Equal(0, _cloud.SnapshotCalls);
        Assert.Empty(_cloud.ChangesAsked);
    }

    // [D10b] The heartbeat never refuses on version; the copy waits for this PC's update.
    [Fact]
    public async Task An_Update_Needed_Holds_The_Copy_Until_The_Builds_Match()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 45;
        _cloud.UpdateNeeded = true;
        _cloud.CloudBuild = "build-2";

        var state = await TickAsync();

        Assert.True(state.UpdateNeeded);
        Assert.Empty(_cloud.ChangesAsked);
    }

    // ---- the write lease (D13, D14) ------------------------------------------------------------------------------

    // The cloud's silence clock moves only when the PC says which ack it holds — so every heartbeat says it.
    [Fact]
    public async Task Every_Heartbeat_Confirms_The_Last_Ack_It_Received()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.ArmsThePc = true;
        var follower = Follower();

        await TickAsync(follower);
        var first = _lease.Current;
        _now = T0.AddSeconds(10);
        await TickAsync(follower);

        Assert.Equal(0, _cloud.Reports[0].ConfirmedAckSeq);
        Assert.Equal(1001, first.LastAckSeq);
        Assert.True(first.LastAckArmed);
        Assert.Equal(T0, first.LastAckReceivedAtUtc);
        Assert.Equal((1001L, true), (_cloud.Reports[1].ConfirmedAckSeq, _cloud.Reports[1].ConfirmedAckArmed));
        Assert.False(_cloud.Reports[1].WantsToStandDown);
    }

    // An answer lost on the way back moves nothing: the PC keeps confirming what it really holds.
    [Fact]
    public async Task An_Unanswered_Heartbeat_Leaves_The_Last_Ack_Where_It_Was()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.ArmsThePc = true;
        var follower = Follower();
        await TickAsync(follower);

        _cloud.HeartbeatStatus = RelayCallStatus.Unreachable;
        _now = T0.AddSeconds(10);
        await TickAsync(follower);
        var lost = _lease.Current;
        _cloud.HeartbeatStatus = RelayCallStatus.Ok;
        _now = T0.AddSeconds(20);
        await TickAsync(follower);

        Assert.Equal(1001, lost.LastAckSeq);
        Assert.Equal(T0, lost.LastAckReceivedAtUtc);
        Assert.True(!_lease.UnansweredSinceLastAck, "the third heartbeat was answered");
        Assert.Equal(1001, _cloud.Reports[2].ConfirmedAckSeq);
    }

    // The ack register outlives the copy's own file (a restart, a re-seed): it is the lease's, on its own file.
    [Fact]
    public async Task The_Last_Ack_Survives_A_Restart_In_The_Leases_Own_File()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.ArmsThePc = true;
        await TickAsync();

        var restarted = new RelayLease(_dir, () => _now, () => TimeSpan.Zero);

        Assert.Equal((1001L, true, T0), (restarted.Current.LastAckSeq, restarted.Current.LastAckArmed,
            restarted.Current.LastAckReceivedAtUtc));
        Assert.False(File.ReadAllText(_store.FilePath).Contains("lastAck", StringComparison.OrdinalIgnoreCase));
    }

    // ---- holding the cabinet's saves (D13) -----------------------------------------------------------------------

    // [D13] A PC in charge says so on every heartbeat and takes nothing from the cloud: its rows are newer than the
    // cloud's now, so a catch-up, a file pass or an hourly repair would overwrite the cut's work.
    [Fact]
    public async Task A_Holding_Pc_Reports_It_And_Takes_Nothing_From_The_Cloud()
    {
        Seeded(seq: 40, filesTotal: 1);
        _rows.Keys = new[] { "clinics/a/new.png" };
        _cloud.HighWater = 45;
        _cloud.Digest = new[] { new RelayTableDigest("Patients", 1, "x") };
        _store.Save(_store.Load() with { LastDigestAtUtc = T0.AddDays(-1) });
        _lease.TakeOver();

        await TickAsync();

        var report = Assert.Single(_cloud.Reports);
        Assert.True(report.Holding);
        Assert.Equal(T0, report.HoldingSinceUtc);
        Assert.Empty(_cloud.ChangesAsked);
        Assert.Empty(_cloud.BlobsAsked);
        Assert.Equal(0, _cloud.SnapshotCalls);
    }

    // A holding PC never runs the cloud's installer either — it would stop the server the cabinet is working on.
    [Fact]
    public async Task A_Holding_Pc_Is_Never_Updated()
    {
        Seeded(seq: 40);
        _cloud.UpdateNeeded = true;
        _cloud.CloudBuild = "build-2";
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        _lease.TakeOver();

        var state = await TickAsync();

        Assert.Empty(_launcher.Launches);
        Assert.False(state.UpdateNeeded);
    }

    // [AC-8.6] Retired while it held the saves: it learns it, and the keeper then lets go (the work stays).
    [Fact]
    public async Task A_Holding_Pc_Learns_It_Was_Released()
    {
        Seeded(seq: 40);
        _lease.TakeOver();
        _cloud.HeartbeatStatus = RelayCallStatus.Released;

        var state = await TickAsync();

        Assert.True(state.Released);
        Assert.Equal(T0, state.ReleasedAtUtc);
    }

    // [D19, AC-7.3] An admin took the cloud back during the cut: on reconnecting, the PC stops taking work AND stops its
    // copy — no catch-up or re-seed may touch the cut's work before « À reprendre » — and the work is marked as never
    // returned, so nothing can erase it. Its report named the ack its takeover was made under.
    [Fact]
    public async Task A_Holding_Pc_Overruled_By_A_Reclaim_Stops_And_Keeps_The_Cuts_Work()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 45;
        _cloud.ArmsThePc = true;
        var follower = Follower();
        await TickAsync(follower);
        var underAck = _lease.Current.LastAckSeq;
        _lease.TakeOver();
        _cloud.Reclaimed = true;

        var pulledBefore = _cloud.ChangesAsked.Count;

        var state = await TickAsync(follower);

        Assert.Equal(underAck, _cloud.Reports[^1].HoldingUnderAckSeq);
        Assert.Equal(RelayFeedDecisions.OverruledReason, state.StoppedReason);
        Assert.False(_lease.IsHolding);
        Assert.True(_lease.HoldsUnreturnedWork);

        await TickAsync(follower);
        Assert.Equal(pulledBefore, _cloud.ChangesAsked.Count);
        Assert.True(_cloud.Reports[^1].CopyStopped);
    }

    // A holding PC does not stand down on a clean stop: the cloud must stay read-only until the cut's work is back.
    [Fact]
    public async Task A_Holding_Pc_Never_Stands_Down()
    {
        Seeded(seq: 40);
        _lease.TakeOver();

        Assert.False(await Follower().StandDownAsync(CancellationToken.None));
        Assert.Empty(_cloud.Reports);
    }

    // ---- the pulse beside a busy tick ----------------------------------------------------------------------------

    // A copy tick fetching a big radiograph must not read, to the cloud, as a cut: past 20 s without an exchange the
    // lease loop sends one, reporting the position last saved.
    [Fact]
    public async Task A_Pulse_Heartbeats_Only_Once_Twenty_Seconds_Passed_Without_An_Exchange()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.ArmsThePc = true;
        var follower = Follower();
        await TickAsync(follower);

        Advance(19);
        await follower.PulseAsync(CancellationToken.None);
        Assert.Single(_cloud.Reports);

        Advance(1);
        await follower.PulseAsync(CancellationToken.None);

        Assert.Equal(2, _cloud.Reports.Count);
        Assert.Equal(40, _cloud.Reports[1].AppliedSeq);
        Assert.Equal(1001, _cloud.Reports[1].ConfirmedAckSeq);
        Assert.Equal(1002, _lease.Current.LastAckSeq);
    }

    [Fact]
    public async Task A_Pc_That_Never_Exchanged_Does_Not_Pulse()
    {
        Seeded(seq: 40);

        await Follower().PulseAsync(CancellationToken.None);

        Assert.Empty(_cloud.Reports);
    }

    // [AC-6.1, D14] A clean stop stands down in two exchanges: ask, then confirm the disarmed answer.
    [Fact]
    public async Task Standing_Down_Asks_Then_Confirms_The_Disarmed_Ack()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.ArmsThePc = true;
        var follower = Follower();
        await TickAsync(follower);

        var stoodDown = await follower.StandDownAsync(CancellationToken.None);

        Assert.True(stoodDown);
        var (ask, confirm) = (_cloud.Reports[1], _cloud.Reports[2]);
        Assert.True(ask.WantsToStandDown && confirm.WantsToStandDown);
        Assert.Equal((1001L, true), (ask.ConfirmedAckSeq, ask.ConfirmedAckArmed));
        Assert.Equal((1002L, false), (confirm.ConfirmedAckSeq, confirm.ConfirmedAckArmed));
        Assert.False(_lease.Current.LastAckArmed);
    }

    [Fact]
    public async Task A_Stand_Down_The_Cloud_Did_Not_Answer_Is_Not_Claimed()
    {
        Seeded(seq: 40);
        _cloud.HeartbeatStatus = RelayCallStatus.Unreachable;

        Assert.False(await Follower().StandDownAsync(CancellationToken.None));
    }

    // ---- the self-update (D10b) ----------------------------------------------------------------------------------

    // The whole path in one tick: the cloud's installer is fetched, matches its hash, the cloud hears « Mise à jour »,
    // and the installer runs once — an update in place, which keeps the pairing.
    [Fact]
    public async Task An_Update_Is_Fetched_Checked_Announced_And_Run_Once()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.UpdateNeeded = true;
        _cloud.CloudBuild = "build-2";
        _installer.Serve("build-2", new byte[] { 7, 7, 7 });

        var follower = Follower();
        var state = await TickAsync(follower);

        var launch = Assert.Single(_launcher.Launches);
        Assert.Contains("/RELAY", launch.Arguments);
        Assert.DoesNotContain("/PAIRFILE", launch.Arguments);
        // The tick's heartbeat, then the two-step stand-down saying « Mise à jour » (D10b « disarm first »).
        Assert.Equal(new[] { false, true, true }, _cloud.Reports.Select(r => r.IsUpdating));
        Assert.Equal(new[] { false, true, true }, _cloud.Reports.Select(r => r.WantsToStandDown));
        Assert.Equal(_now, state.UpdateLaunchedAtUtc);
        Assert.Equal(_now, _store.Load().UpdateLaunchedAtUtc);

        _now = T0.AddMinutes(1);
        await TickAsync(follower);

        Assert.Single(_launcher.Launches);
        Assert.True(_cloud.Reports.Last().IsUpdating);
        Assert.Empty(_cloud.ChangesAsked);
    }

    // The new build heartbeats, the cloud stops asking, the copy resumes at once; the leftovers go once the installer
    // has had time to finish.
    [Fact]
    public async Task A_Landed_Update_Resumes_The_Copy_And_Is_Cleared_Afterwards()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _cloud.UpdateNeeded = true;
        _cloud.CloudBuild = "build-2";
        _installer.Serve("build-2", new byte[] { 7, 7, 7 });
        await TickAsync();

        // This process is now the new build: its first heartbeat still reads the old state file.
        var newBuild = Follower("build-2");
        _cloud.UpdateNeeded = false;
        _cloud.HighWater = 42;
        _now = T0.AddMinutes(5);
        var landed = await TickAsync(newBuild);

        Assert.Equal((40, "h40"), _cloud.ChangesAsked.First());
        Assert.Equal("build-2", landed.UpdateBuild);
        Assert.False(_cloud.Reports.Last().IsUpdating);
        Assert.Equal(0, _launcher.Forgotten);

        _now = T0 + RelayUpdater.CleanupAfterLanding + TimeSpan.FromSeconds(1);
        var settled = await TickAsync(newBuild);

        Assert.Null(settled.UpdateBuild);
        Assert.Null(settled.UpdateLaunchedAtUtc);
        Assert.Equal(1, _launcher.Forgotten);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "updates")));
    }

    // A stopped copy may hold more than the cloud: it is never updated toward that cloud either.
    [Fact]
    public async Task A_Stopped_Copy_Is_Never_Updated()
    {
        Seeded(seq: 40);
        _store.Save(_store.Load() with { StoppedReason = RelayFeedDecisions.WentBackReason });
        _cloud.HighWater = 40;
        _cloud.UpdateNeeded = true;
        _cloud.CloudBuild = "build-2";
        _installer.Serve("build-2", new byte[] { 7 });

        await TickAsync();

        Assert.Equal(0, _installer.Fetches);
        Assert.Empty(_launcher.Launches);
    }

    // A failed update is said on the heartbeat, in place of the copy's own last error.
    [Fact]
    public async Task A_Failed_Update_Is_What_The_Heartbeat_Reports()
    {
        Seeded(seq: 40);
        _store.Save(_store.Load() with
        {
            UpdateNeeded = true, UpdateBuild = "build-2", UpdateStartedAtUtc = T0,
            UpdateError = RelayUpdater.NotLandedSentence, LastError = "ancienne erreur",
        });
        _cloud.HighWater = 40;
        _cloud.UpdateNeeded = true;
        _cloud.CloudBuild = "build-2";

        await TickAsync();

        var report = _cloud.Reports.Single();
        Assert.Equal(RelayUpdater.NotLandedSentence, report.LastError);
        Assert.False(report.IsUpdating);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task The_Heartbeat_Reports_The_Copys_Position_And_Progress()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;

        await TickAsync();

        var report = _cloud.Reports.Single();
        Assert.Equal(40, report.AppliedSeq);
        Assert.True(report.SeedComplete);
        Assert.Equal(100, report.SeedPercent);
        Assert.Equal("build-1", report.Build);
        Assert.Equal("192.168.1.10", report.LanAddresses);
        Assert.Equal("FP", report.CertificateFingerprint);
        // AC-6.2: where the cabinet's devices try this PC, and the box they must share with it to count.
        Assert.Equal(5001, report.HttpsPort);
        Assert.Equal("192.168.1.1", report.GatewayAddress);
    }

    // ---- files ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Missing_Files_Are_Fetched_At_Their_Own_Key_And_Counted()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _rows.Keys = new[] { "clinics/a/1.png", "clinics/a/2.png", "clinics/a/3.png" };
        _onDisk.Add("clinics/a/1.png");

        var state = await TickAsync();

        Assert.Equal(new[] { "clinics/a/2.png", "clinics/a/3.png" }, _restored.Order());
        Assert.Equal(3, state.FilesTotal);
        Assert.Equal(3, state.FilesCopied);
        Assert.True(RelayFeedDecisions.SeedComplete(state));
    }

    [Fact]
    public async Task A_Tick_Fetches_At_Most_Its_Share_Of_Files()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _rows.Keys = Enumerable.Range(0, RelayFollower.MaxFilesPerTick + 5).Select(i => $"clinics/a/{i}.dcm").ToArray();

        var state = await TickAsync();

        Assert.Equal(RelayFollower.MaxFilesPerTick, _restored.Count);
        Assert.False(RelayFeedDecisions.SeedComplete(state));
    }

    // A file the cloud cannot serve is asked again later and does not hold the first copy « en cours » for ever.
    [Fact]
    public async Task A_File_The_Cloud_Cannot_Serve_Does_Not_Block_The_First_Copy()
    {
        Seeded(seq: 40);
        _cloud.HighWater = 40;
        _rows.Keys = new[] { "clinics/a/ok.png", "clinics/a/gone.png" };
        _cloud.UnservableBlobs.Add("clinics/a/gone.png");

        var follower = Follower();
        var state = await TickAsync(follower);

        Assert.Equal(new[] { "clinics/a/ok.png" }, _restored);
        Assert.Equal(1, state.FilesTotal);
        Assert.True(RelayFeedDecisions.SeedComplete(state));

        await TickAsync(follower);
        Assert.Equal(1, _cloud.BlobsAsked.Count(k => k == "clinics/a/gone.png"));
    }

    // ---- the hourly check ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Differing_Table_Is_Repaired_Alone_And_The_Position_Stays()
    {
        Seeded(seq: 40);
        _store.Save(_store.Load() with { LastDigestAtUtc = null });
        _cloud.HighWater = 40;
        _cloud.Digest = new[] { new RelayTableDigest("Patient", 2, "a"), new RelayTableDigest("Invoice", 1, "b") };
        _rows.Digests.Enqueue(new[] { new RelayTableDigest("Patient", 1, "z"), new RelayTableDigest("Invoice", 1, "b") });
        _rows.Digests.Enqueue(new[] { new RelayTableDigest("Patient", 2, "a"), new RelayTableDigest("Invoice", 1, "b") });
        _cloud.Snapshot = ("e1", 41, "h41");

        var state = await TickAsync();

        Assert.Equal("Patient", string.Join(",", Assert.Single(_rows.Replaced)!));
        Assert.Equal(40, state.AppliedSeq);
        Assert.Empty(state.MismatchTables);
        Assert.Equal(_now, state.LastDigestAtUtc);
    }

    [Fact]
    public async Task A_Table_That_Still_Differs_After_Repair_Is_Reported()
    {
        Seeded(seq: 40);
        _store.Save(_store.Load() with { LastDigestAtUtc = null });
        _cloud.HighWater = 40;
        _cloud.Digest = new[] { new RelayTableDigest("Patient", 2, "a") };
        _rows.Digests.Enqueue(new[] { new RelayTableDigest("Patient", 1, "z") });
        _rows.Digests.Enqueue(new[] { new RelayTableDigest("Patient", 1, "z") });
        _cloud.Snapshot = ("e1", 40, "h40");

        var state = await TickAsync();

        Assert.Equal(new[] { "Patient" }, state.MismatchTables);
    }

    // [D12] Never repair toward another history: a partial copy from a changed epoch stops instead.
    [Fact]
    public async Task A_Repair_Toward_Another_History_Stops_The_Copy()
    {
        Seeded(seq: 40);
        _store.Save(_store.Load() with { LastDigestAtUtc = null });
        _cloud.HighWater = 40;
        _cloud.Digest = new[] { new RelayTableDigest("Patient", 2, "a") };
        _rows.Digests.Enqueue(new[] { new RelayTableDigest("Patient", 1, "z") });
        _cloud.Snapshot = ("e2", 40, "h40");

        var state = await TickAsync();

        Assert.Empty(_rows.Replaced);
        Assert.NotNull(state.StoppedReason);
    }

    // ---- secrets -------------------------------------------------------------------------------------------------

    // [D8] A TOTP secret sealed for this PC is opened with its own key and re-protected under its own ring.
    [Fact]
    public void A_Sealed_Secret_Is_Reprotected_Under_This_Pcs_Ring()
    {
        var unwrap = Follower().Unwrap;

        Assert.Equal("ring:JBSWY3DP", unwrap.UnwrapToProtected(RelaySecretEnvelope.Seal("JBSWY3DP", _keys.Public)));
        Assert.Null(unwrap.UnwrapToProtected("CfDJ8-not-sealed-for-me"));
    }

    // ---- the return (D18) ----------------------------------------------------------------------------------------

    private readonly FakeHandbackStore _handbackStore = new();

    private RelayFollower HandbackFollower()
    {
        var secrets = new Mock<IUserSecretProtector>();
        var credentials = new RelayCredentials(Guid.NewGuid(), ClinicId, "Cabinet", "https://cloud.example.tn", "secret",
            Convert.ToBase64String(_keys.Private), T0);
        var handback = new RelayHandback(_cloud, _lease, ClinicId, NullLogger.Instance, () => _mono,
            (delay, _) => { Advance(delay.TotalSeconds); return Task.CompletedTask; });
        return new RelayFollower(_cloud, _store, credentials, secrets.Object, "build-1",
            () => new RelayHostReport(new[] { "192.168.1.10" }, "FP", 100L * 1024 * 1024 * 1024, 5001, "192.168.1.1"),
            new RelayUpdater(_installer, _launcher, Path.Combine(_dir, "updates"), Path.Combine(_dir, "logs"),
                NullLogger.Instance, () => _now),
            _lease, NullLogger.Instance, () => _now, handback);
    }

    private RelayLocalSide HandingBackLocal => new(_rows, _rows, _files.Object, _handbackStore);

    private async Task HoldingTicksAsync(RelayFollower follower, int ticks, double secondsApart = 10)
    {
        for (var i = 0; i < ticks; i++)
        {
            await follower.TickAsync(HandingBackLocal, CancellationToken.None);
            Advance(secondsApart);
        }
    }

    // [AC-5.1] Two minutes of answered heartbeats, then the cut goes back once: the PC forgets its log, stops holding,
    // and its very next heartbeat says so — the cloud's cue to take the saves back (phase 2).
    [Fact]
    public async Task A_Holding_Pc_Hands_The_Cut_Back_Once_The_Cloud_Has_Answered_For_Two_Minutes()
    {
        Seeded(seq: 40);
        _lease.TakeOver();
        var follower = HandbackFollower();

        await HoldingTicksAsync(follower, 12);
        Assert.Empty(_cloud.Handbacks);

        await HoldingTicksAsync(follower, 1);

        var sent = Assert.Single(_cloud.Handbacks);
        Assert.Equal(40, sent.BaseAppliedSeq);
        Assert.Equal(T0, sent.CutSinceUtc);
        Assert.Equal(1, _handbackStore.Forgotten);
        Assert.False(_lease.IsHolding);
        var confirm = _cloud.Reports[^1];
        Assert.False(confirm.Holding);
        Assert.Equal(sent.HandbackId, confirm.ReturnedHandbackId);
        Assert.Null(_lease.Current.ReturnedHandbackId);
    }

    // [AC-5.2] The cut is read with the saves refused, so nothing saved meanwhile can be missing from what is sent.
    [Fact]
    public async Task The_Cut_Is_Read_While_The_Pc_Refuses_Saves()
    {
        Seeded();
        _lease.TakeOver();
        _handbackStore.ReadProbe = () => (_lease.AcceptsSaves, _lease.IsHandingBack);
        var follower = HandbackFollower();

        await HoldingTicksAsync(follower, 14);

        Assert.Equal(new[] { (false, true) }, _handbackStore.LeaseWhileReading);
        Assert.Single(_cloud.Handbacks);
    }

    // [AC-5.1] A heartbeat left unanswered starts the two minutes again.
    [Fact]
    public async Task An_Unanswered_Heartbeat_Starts_The_Two_Minutes_Again()
    {
        Seeded();
        _lease.TakeOver();
        var follower = HandbackFollower();

        await HoldingTicksAsync(follower, 10);
        _cloud.HeartbeatStatus = RelayCallStatus.Unreachable;
        await HoldingTicksAsync(follower, 1);
        _cloud.HeartbeatStatus = RelayCallStatus.Ok;
        await HoldingTicksAsync(follower, 10);

        Assert.Empty(_cloud.Handbacks);
        Assert.True(_lease.AcceptsSaves);
    }

    // [AC-5.9] A lost answer is sent again with the SAME id (the cloud answers « already applied »), saves still refused;
    // after RetryWithin the cabinet works on the PC again and the next attempt carries a NEW id.
    [Fact]
    public async Task A_Lost_Answer_Is_Retried_With_The_Same_Id_Then_Given_Up_For_A_New_One()
    {
        Seeded();
        _lease.TakeOver();
        var follower = HandbackFollower();
        for (var i = 0; i < 20; i++)
        {
            _cloud.HandbackStatuses.Enqueue(RelayCallStatus.Unreachable);
        }

        await HoldingTicksAsync(follower, 14);

        var firstId = _cloud.Handbacks[0].HandbackId;
        Assert.True(_cloud.Handbacks.Count > 1);
        Assert.All(_cloud.Handbacks, h => Assert.Equal(firstId, h.HandbackId));
        Assert.True(_lease.AcceptsSaves);
        Assert.Null(_lease.Current.HandbackId);
        Assert.Equal(0, _handbackStore.Forgotten);

        _cloud.HandbackStatuses.Clear();
        await HoldingTicksAsync(follower, 20);

        Assert.NotEqual(firstId, _cloud.Handbacks[^1].HandbackId);
        Assert.False(_lease.IsHolding);
    }

    // [AC-5.9] A refusal gives the saves back at once, and the return is reported stuck from its first attempt.
    [Fact]
    public async Task A_Refused_Return_Gives_The_Saves_Back_And_Is_Reported()
    {
        Seeded();
        _lease.TakeOver();
        var follower = HandbackFollower();
        _cloud.HandbackStatuses.Enqueue(RelayCallStatus.Refused);

        await HoldingTicksAsync(follower, 14);

        Assert.Single(_cloud.Handbacks);
        Assert.True(_lease.AcceptsSaves);
        Assert.NotNull(_lease.Current.ReturnFirstTriedAtUtc);

        await HoldingTicksAsync(follower, 1);
        Assert.Equal(_lease.Current.ReturnFirstTriedAtUtc, _cloud.Reports[^1].ReturnStuckSinceUtc);
    }

    // [EC-11, AC-5.9] On another build the return cannot start: nothing is sent, the cabinet keeps working, it is reported.
    [Fact]
    public async Task A_Cloud_On_Another_Build_Blocks_The_Return_And_Says_So()
    {
        Seeded();
        _lease.TakeOver();
        _cloud.UpdateNeeded = true;
        var follower = HandbackFollower();

        await HoldingTicksAsync(follower, 20);

        Assert.Empty(_cloud.Handbacks);
        Assert.True(_lease.AcceptsSaves);
        Assert.NotNull(_cloud.Reports[^1].ReturnStuckSinceUtc);
        Assert.Equal(0, _installer.Fetches);
    }

    // [D18] The files the cut's rows name reach the cloud before the rows that name them.
    [Fact]
    public async Task The_Cuts_Files_Go_Before_Its_Rows()
    {
        Seeded();
        _lease.TakeOver();
        _handbackStore.Files = new[] { "clinics/a/scan.png", "clinics/a/held.png" };
        _cloud.MissingFiles.Add("clinics/a/scan.png");
        _files.Setup(f => f.DownloadAsync("clinics/a/scan.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1, 2 }));
        var follower = HandbackFollower();

        await HoldingTicksAsync(follower, 14);

        Assert.Equal(new[] { "clinics/a/scan.png" }, _cloud.Uploaded);
        Assert.Single(_cloud.Handbacks);
    }

    // [AC-5.9] A PC restarted mid-return finds it refusing saves; an unanswered heartbeat gives them back at once.
    [Fact]
    public async Task A_Return_Interrupted_By_A_Restart_Gives_Way_When_The_Cloud_Is_Gone()
    {
        Seeded();
        _lease.TakeOver();
        _lease.BeginHandback();
        Assert.False(_lease.AcceptsSaves);
        _cloud.HeartbeatStatus = RelayCallStatus.Unreachable;

        await HoldingTicksAsync(HandbackFollower(), 1);

        Assert.True(_lease.AcceptsSaves);
        Assert.Empty(_cloud.Handbacks);
    }

    // [US-7, AC-7.3] A PC overruled by « Reprendre la main » sends its cut to be listed « À reprendre » (from the
    // takeover), then drops its log and copies the cloud afresh — the one stop a machine may undo.
    [Fact]
    public async Task An_Overruled_Pc_Lists_Its_Cut_Then_Copies_The_Cloud_Afresh()
    {
        Seeded(seq: 40);
        _lease.TakeOver();
        _cloud.Reclaimed = true;
        var follower = HandbackFollower();
        await HoldingTicksAsync(follower, 1);
        Assert.True(_lease.HoldsUnreturnedWork);
        _cloud.Reclaimed = false;

        await HoldingTicksAsync(follower, 1);

        var sent = Assert.Single(_cloud.Overruled);
        Assert.Equal(T0, sent.CutSinceUtc);
        Assert.Equal(1, _handbackStore.Forgotten);
        Assert.False(_lease.HoldsUnreturnedWork);
        var state = _store.Load();
        Assert.Null(state.StoppedReason);
        Assert.True(state.ReseedNeeded);
    }

    // [US-7] Not listed (the cloud refused, or did not answer): the copy stays stopped and keeps the cut, and tries later.
    [Fact]
    public async Task An_Overruled_Pc_That_Could_Not_List_Its_Cut_Keeps_It()
    {
        Seeded(seq: 40);
        _lease.TakeOver();
        _cloud.Reclaimed = true;
        var follower = HandbackFollower();
        await HoldingTicksAsync(follower, 1);
        _cloud.Reclaimed = false;
        _cloud.OverruledStatus = RelayCallStatus.Refused;

        await HoldingTicksAsync(follower, 2);

        Assert.Single(_cloud.Overruled);
        Assert.Equal(0, _handbackStore.Forgotten);
        Assert.True(_lease.HoldsUnreturnedWork);
        Assert.Equal(RelayFeedDecisions.OverruledReason, _store.Load().StoppedReason);
    }

    // [D18] A new cut before the cloud confirmed the last return: the old handback is moot and never named again.
    [Fact]
    public void A_New_Takeover_Forgets_A_Return_Still_Awaiting_The_Cloud()
    {
        _lease.TakeOver();
        var id = _lease.BeginHandback();
        _lease.CompleteReturn(id);
        Assert.Equal(id, _lease.Current.ReturnedHandbackId);

        _lease.TakeOver();

        Assert.Null(_lease.Current.ReturnedHandbackId);
    }

    private sealed class FakeHandbackStore : IRelayHandbackStore
    {
        public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
        public List<(bool AcceptsSaves, bool HandingBack)> LeaseWhileReading { get; } = new();
        public Func<(bool, bool)>? ReadProbe { get; set; }
        public int Forgotten { get; private set; }

        public Task<RelayHandbackRequest> ReadCutAsync(
            Guid clinicId, Guid handbackId, long baseAppliedSeq, DateTime cutSinceUtc, CancellationToken cancellationToken)
        {
            if (ReadProbe is not null)
            {
                LeaseWhileReading.Add(ReadProbe());
            }

            return Task.FromResult(new RelayHandbackRequest(handbackId, baseAppliedSeq, cutSinceUtc,
                Array.Empty<RelayHandbackChange>(), Array.Empty<RelayRow>(), Array.Empty<RelayHandbackJournalEntry>(),
                Array.Empty<RelaySignInTrace>(), Array.Empty<RelayRecoveryCodeUse>()));
        }

        public IReadOnlyList<string> FileKeys(IReadOnlyList<RelayRow> rows) => Files;

        public Task ForgetCutAsync(Guid clinicId, CancellationToken cancellationToken)
        {
            Forgotten++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RelayCloudChange>> CloudChangesAfterAsync(Guid clinicId, long afterSeq, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> KeysWrittenByReturnAsync(Guid clinicId, string table, DateTime sinceUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<RelayRowKey, string>> CurrentRowsAsync(Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<RelayRowKey, string>> AuthorsAsync(Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public IEnumerable<RelayRowKey> References(string table, System.Text.Json.JsonElement row) => throw new NotSupportedException();

        public Task ApplyReturnAsync(Guid clinicId, RelayHandbackRequest request, RelayHandbackPlan plan, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    // ---- fakes ---------------------------------------------------------------------------------------------------

    /// <summary>A cloud serving one build's installer (or nothing), answering at once so a step is one tick.</summary>
    internal sealed class FakeInstaller : IRelayInstallerSource
    {
        private string? _build;
        private byte[] _bytes = Array.Empty<byte>();

        public string? Sha256Override { get; set; }
        public RelayInstallerFetchStatus? Answer { get; set; }
        public int Fetches { get; private set; }
        public List<long> Offsets { get; } = new();

        public void Serve(string build, byte[] bytes)
        {
            _build = build;
            _bytes = bytes;
        }

        public Task<RelayInstallerFetch> FetchAsync(string partPath, string build, CancellationToken cancellationToken)
        {
            Fetches++;
            Offsets.Add(File.Exists(partPath) ? new FileInfo(partPath).Length : 0);
            if (Answer is { } answer)
            {
                return Task.FromResult(new RelayInstallerFetch(answer));
            }

            if (_build is null)
            {
                return Task.FromResult(new RelayInstallerFetch(RelayInstallerFetchStatus.NotPublished));
            }

            if (_build != build)
            {
                return Task.FromResult(new RelayInstallerFetch(RelayInstallerFetchStatus.OtherBuild, Error: _build));
            }

            File.WriteAllBytes(partPath, _bytes);
            return Task.FromResult(new RelayInstallerFetch(RelayInstallerFetchStatus.Complete,
                Sha256Override ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_bytes))));
        }
    }

    internal sealed class FakeLauncher : IRelayUpdateLauncher
    {
        public List<(string Installer, string Arguments, string WorkingDirectory)> Launches { get; } = new();
        public bool Refuses { get; set; }
        public Action? OnLaunch { get; set; }
        public int Forgotten { get; private set; }

        public RelayLaunch Launch(string installerPath, string arguments, string workingDirectory)
        {
            OnLaunch?.Invoke();
            Launches.Add((installerPath, arguments, workingDirectory));
            return Refuses ? new RelayLaunch(false, "Accès refusé.") : new RelayLaunch(true);
        }

        public void Forget() => Forgotten++;
    }

    private sealed class FakeCloud : IRelayCloudClient
    {
        public RelayCallStatus HeartbeatStatus { get; set; } = RelayCallStatus.Ok;
        public long HighWater { get; set; }
        public bool UpdateNeeded { get; set; }
        public string CloudBuild { get; set; } = "build-1";

        /// <summary>Whether the cloud arms the PC — a stand-down request is always answered disarmed, as the real one does.</summary>
        public bool ArmsThePc { get; set; }

        /// <summary>An admin took the cloud back: a holding PC is answered « Reclaimed » (D19).</summary>
        public bool Reclaimed { get; set; }

        public long AcksIssued { get; private set; }
        public string AckEpoch { get; set; } = "e1";
        public Queue<RelayFeedBatch> Batches { get; } = new();
        public (string Epoch, long HighWater, string? Head) Snapshot { get; set; } = ("e1", 0, null);
        public IReadOnlyList<RelayTableDigest> Digest { get; set; } = Array.Empty<RelayTableDigest>();
        public HashSet<string> UnservableBlobs { get; } = new(StringComparer.Ordinal);

        public List<RelayHeartbeatRequest> Reports { get; } = new();
        public List<(long After, string? Fingerprint)> ChangesAsked { get; } = new();
        public List<string> BlobsAsked { get; } = new();
        public int SnapshotCalls { get; private set; }
        public RelayCallStatus ErasedStatus { get; set; } = RelayCallStatus.Ok;
        public int ErasedReports { get; private set; }

        public Task<RelayCall<bool>> ReportErasedAsync(CancellationToken cancellationToken)
        {
            ErasedReports++;
            return Task.FromResult(new RelayCall<bool>(ErasedStatus, ErasedStatus == RelayCallStatus.Ok));
        }

        // The follower never uninstalls itself; only the uninstaller's verb sends this.
        public Task<RelayCall<bool>> ReportUninstalledAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<RelayHeartbeatAck>> HeartbeatAsync(RelayHeartbeatRequest report, CancellationToken cancellationToken)
        {
            Reports.Add(report);
            if (HeartbeatStatus != RelayCallStatus.Ok)
            {
                return Task.FromResult(new RelayCall<RelayHeartbeatAck>(HeartbeatStatus));
            }

            AcksIssued++;
            return Task.FromResult(new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Ok,
                new RelayHeartbeatAck(T0, HighWater, AckEpoch, false, UpdateNeeded, CloudBuild,
                    AckSeq: 1000 + AcksIssued, Armed: ArmsThePc && !report.WantsToStandDown && !(Reclaimed && report.Holding),
                    Reclaimed: Reclaimed && report.Holding,
                    ReturnReleased: ReleasesReturn && !report.Holding && report.ReturnedHandbackId is not null)));
        }

        // ---- D18 ----

        public bool ReleasesReturn { get; set; } = true;
        public Queue<RelayCallStatus> HandbackStatuses { get; } = new();
        public List<RelayHandbackRequest> Handbacks { get; } = new();
        public HashSet<string> MissingFiles { get; } = new(StringComparer.Ordinal);
        public List<string> Uploaded { get; } = new();

        public Task<RelayCall<IReadOnlyList<string>>> MissingHandbackFilesAsync(
            IReadOnlyList<string> keys, CancellationToken cancellationToken) =>
            Task.FromResult(new RelayCall<IReadOnlyList<string>>(RelayCallStatus.Ok, keys.Where(MissingFiles.Contains).ToList()));

        public Task<RelayCall<bool>> UploadHandbackFileAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            Uploaded.Add(storageKey);
            return Task.FromResult(new RelayCall<bool>(RelayCallStatus.Ok, true));
        }

        public RelayCallStatus OverruledStatus { get; set; } = RelayCallStatus.Ok;
        public List<RelayHandbackRequest> Overruled { get; } = new();

        public Task<RelayCall<RelayHandbackResultDto>> ListOverruledCutAsync(RelayHandbackRequest request, CancellationToken cancellationToken)
        {
            Overruled.Add(request);
            return Task.FromResult(OverruledStatus == RelayCallStatus.Ok
                ? new RelayCall<RelayHandbackResultDto>(RelayCallStatus.Ok, new RelayHandbackResultDto(false, 0, 0, 3))
                : new RelayCall<RelayHandbackResultDto>(OverruledStatus, null, "non"));
        }

        public Task<RelayCall<RelayHandbackResultDto>> HandBackAsync(RelayHandbackRequest request, CancellationToken cancellationToken)
        {
            Handbacks.Add(request);
            var status = HandbackStatuses.Count > 0 ? HandbackStatuses.Dequeue() : RelayCallStatus.Ok;
            return Task.FromResult(status == RelayCallStatus.Ok
                ? new RelayCall<RelayHandbackResultDto>(status, new RelayHandbackResultDto(false, request.Rows.Count, 0, 0))
                : new RelayCall<RelayHandbackResultDto>(status, null, "non"));
        }

        public Task<RelayCall<RelayFeedBatch>> ChangesAsync(long after, string? fingerprint, CancellationToken cancellationToken)
        {
            ChangesAsked.Add((after, fingerprint));
            return Task.FromResult(Batches.Count > 0
                ? new RelayCall<RelayFeedBatch>(RelayCallStatus.Ok, Batches.Dequeue())
                : new RelayCall<RelayFeedBatch>(RelayCallStatus.Ok,
                    new RelayFeedBatch("e1", after, after, fingerprint, RelayFeedOutcome.Ok, Array.Empty<RelayRow>())));
        }

        public Task<RelayCall<string>> SnapshotAsync(IReadOnlyCollection<string>? tables, CancellationToken cancellationToken)
        {
            SnapshotCalls++;
            var path = Path.Combine(Path.GetTempPath(), $"relay-test-{Guid.NewGuid():N}.json.gz");
            var head = Snapshot.Head is null ? "null" : $"\"{Snapshot.Head}\"";
            using (var gzip = new GZipStream(File.Create(path), CompressionLevel.Fastest))
            {
                gzip.Write(Encoding.UTF8.GetBytes(
                    $"{{\"epoch\":\"{Snapshot.Epoch}\",\"highWater\":{Snapshot.HighWater},\"head\":{head},\"tables\":[]}}"));
            }

            return Task.FromResult(new RelayCall<string>(RelayCallStatus.Ok, path));
        }

        public Task<RelayCall<IReadOnlyList<RelayTableDigest>>> DigestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RelayCall<IReadOnlyList<RelayTableDigest>>(RelayCallStatus.Ok, Digest));

        public Task<RelayCall<string>> BlobAsync(string storageKey, CancellationToken cancellationToken)
        {
            BlobsAsked.Add(storageKey);
            if (UnservableBlobs.Contains(storageKey))
            {
                return Task.FromResult(new RelayCall<string>(RelayCallStatus.NotFound, null, "Fichier introuvable."));
            }

            var path = Path.Combine(Path.GetTempPath(), $"relay-test-{Guid.NewGuid():N}.part");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return Task.FromResult(new RelayCall<string>(RelayCallStatus.Ok, path));
        }
    }

    private sealed class FakeRows : IClinicRelayRowStore, IRelayBlobIndex
    {
        public bool ApplyThrows { get; set; }
        public bool ReplaceThrows { get; set; }
        public IReadOnlyList<string> Keys { get; set; } = Array.Empty<string>();
        public Queue<IReadOnlyList<RelayTableDigest>> Digests { get; } = new();
        public List<IReadOnlyList<RelayRow>> Applied { get; } = new();
        public List<IReadOnlyCollection<string>?> Replaced { get; } = new();

        public Task ApplyBatchAsync(Guid clinicId, IReadOnlyList<RelayRow> rows, RelayInboundUnwrap unwrap, CancellationToken cancellationToken)
        {
            if (ApplyThrows)
            {
                throw new InvalidOperationException("FK violation");
            }

            Applied.Add(rows);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(Guid clinicId, Stream snapshot, IReadOnlyCollection<string>? onlyTables, RelayInboundUnwrap unwrap, CancellationToken cancellationToken)
        {
            if (ReplaceThrows)
            {
                throw new InvalidOperationException("disk full");
            }

            Replaced.Add(onlyTables);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RelayTableDigest>> DigestAsync(Guid clinicId, CancellationToken cancellationToken) =>
            Task.FromResult(Digests.Count > 0 ? Digests.Dequeue() : (IReadOnlyList<RelayTableDigest>)Array.Empty<RelayTableDigest>());

        public Task<IReadOnlyList<string>> ListKeysAsync(Guid clinicId, CancellationToken cancellationToken) => Task.FromResult(Keys);

        public Task<bool> ClinicNamesKeyAsync(Guid clinicId, string storageKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureCursorAsync(Guid clinicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DropCursorAsync(Guid clinicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<long> HighWaterAsync(Guid clinicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> FeedEpochAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RelayFeedBatch> ReadChangesAsync(Guid clinicId, long after, string? fingerprint, RelayOutboundWrap wrap, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteSnapshotAsync(Guid clinicId, IReadOnlyCollection<string>? tables, RelayOutboundWrap wrap, Stream output, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> FingerprintAsync(Guid clinicId, long seq, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
