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
    private readonly RelayFollowerStateStore _store;
    private readonly (string Public, byte[] Private) _keys;
    private readonly FakeInstaller _installer = new();
    private readonly FakeLauncher _launcher = new();

    public RelayFollowerTests()
    {
        _store = new RelayFollowerStateStore(_dir);
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
            () => new RelayHostReport(new[] { "192.168.1.10" }, "FP", 100L * 1024 * 1024 * 1024),
            new RelayUpdater(_installer, _launcher, Path.Combine(_dir, "updates"), Path.Combine(_dir, "logs"),
                NullLogger.Instance, () => _now),
            NullLogger.Instance, () => _now);
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
        Assert.Equal(new[] { false, true }, _cloud.Reports.Select(r => r.IsUpdating));
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
            return Task.FromResult(HeartbeatStatus == RelayCallStatus.Ok
                ? new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Ok,
                    new RelayHeartbeatAck(T0, HighWater, AckEpoch, false, UpdateNeeded, CloudBuild))
                : new RelayCall<RelayHeartbeatAck>(HeartbeatStatus));
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
