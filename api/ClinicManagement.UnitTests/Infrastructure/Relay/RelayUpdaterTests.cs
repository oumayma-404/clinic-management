using ClinicManagement.Infrastructure.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// The PC de secours's self-update (<c>clinic-pc-copy</c> D10b), one step at a time against a fake cloud and a fake
/// launcher. The cases are the ones that run the wrong thing, run it twice, or never stop trying: a file from another
/// build, a file that does not match its hash, a cloud with nothing to offer, an installer that refused.
/// </summary>
public sealed class RelayUpdaterTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-updater-" + Guid.NewGuid().ToString("N"));
    private readonly RelayFollowerTests.FakeInstaller _installer = new();
    private readonly RelayFollowerTests.FakeLauncher _launcher = new();
    private readonly List<RelayFollowerState> _saved = new();
    private readonly List<RelayFollowerState> _announced = new();
    private DateTime _now = T0;

    private string Folder => Path.Combine(_dir, "updates");

    private RelayUpdater Updater() =>
        new(_installer, _launcher, Folder, Path.Combine(_dir, "logs"), NullLogger.Instance, () => _now);

    private static RelayFollowerState Needed => new() { UpdateNeeded = true, RowsSeededAtUtc = T0, Epoch = "e1" };

    private Task<RelayFollowerState> StepAsync(RelayUpdater updater, RelayFollowerState state, string cloudBuild = "build-2") =>
        updater.StepAsync(state, cloudBuild,
            s => { _saved.Add(s); return s; },
            (s, _) => { _announced.Add(s); return Task.CompletedTask; },
            CancellationToken.None);

    // ---- what runs ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Installer_Runs_As_An_Update_In_Place_With_Its_Own_Result_And_Log()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });

        await StepAsync(Updater(), Needed);

        var launch = Assert.Single(_launcher.Launches);
        Assert.Equal(Path.Combine(Folder, RelayUpdater.InstallerFileName), launch.Installer);
        Assert.Equal(Folder, launch.WorkingDirectory);
        Assert.StartsWith("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAY ", launch.Arguments);
        Assert.Contains($"\"/RESULTFILE={Path.Combine(Folder, RelayUpdater.ResultFileName)}\"", launch.Arguments);
        Assert.Contains($"\"/LOG={Path.Combine(_dir, "logs")}", launch.Arguments);
        Assert.DoesNotContain("/PAIRFILE", launch.Arguments);
        Assert.DoesNotContain("/CLOUD", launch.Arguments);
    }

    // The start is on disk before the installer is: a crash between the two cannot lead to a second run.
    [Fact]
    public async Task The_Start_Is_Saved_And_Announced_Before_The_Installer_Runs()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        var savedAtLaunch = 0;
        var announcedAtLaunch = 0;
        _launcher.OnLaunch = () => { savedAtLaunch = _saved.Count; announcedAtLaunch = _announced.Count; };

        await StepAsync(Updater(), Needed);

        Assert.Equal(1, savedAtLaunch);
        Assert.Equal(T0, _saved.Single().UpdateLaunchedAtUtc);
        Assert.Equal(1, announcedAtLaunch);
    }

    [Fact]
    public async Task An_Installer_Of_Another_Build_Is_Never_Run()
    {
        _installer.Serve("build-3", new byte[] { 1, 2, 3 });

        var state = await StepAsync(Updater(), Needed);

        Assert.Empty(_launcher.Launches);
        Assert.False(File.Exists(Path.Combine(Folder, RelayUpdater.InstallerFileName + ".part")));
        Assert.Equal(T0 + RelayUpdater.InterruptedRetry, state.UpdateRetryAfterUtc);
    }

    [Fact]
    public async Task An_Installer_That_Does_Not_Match_Its_Hash_Is_Thrown_Away_And_Never_Run()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        _installer.Sha256Override = new string('A', 64);

        var state = await StepAsync(Updater(), Needed);

        Assert.Empty(_launcher.Launches);
        Assert.Empty(_announced);
        Assert.False(File.Exists(Path.Combine(Folder, RelayUpdater.InstallerFileName)));
        Assert.Null(state.UpdateSha256);
        Assert.Equal(T0 + RelayUpdater.BadInstallerRetry, state.UpdateRetryAfterUtc);
    }

    // ---- never a loop -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Cloud_With_Nothing_To_Offer_Is_Asked_Again_Later_And_The_Pc_Does_Not_Claim_To_Update()
    {
        var updater = Updater();

        var state = await StepAsync(updater, Needed);
        _now = T0.AddMinutes(4);
        state = await StepAsync(updater, state);

        Assert.Equal(1, _installer.Fetches);
        Assert.True(state.UpdateWaitingForInstaller);
        Assert.False(RelayUpdater.ReportsUpdating(state, "build-1", _now));

        _installer.Serve("build-2", new byte[] { 1 });
        _now = T0 + RelayUpdater.NotPublishedRetry;
        state = await StepAsync(updater, state);

        Assert.Equal(2, _installer.Fetches);
        Assert.Single(_launcher.Launches);
        Assert.False(state.UpdateWaitingForInstaller);
    }

    // A cut keeps what arrived: the next attempt asks for the rest, a minute later.
    [Fact]
    public async Task An_Interrupted_Download_Resumes_Where_It_Stopped()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Combine(Folder, RelayUpdater.InstallerFileName + ".part"), new byte[] { 1, 2 });
        _installer.Answer = RelayInstallerFetchStatus.Interrupted;
        var updater = Updater();
        var state = Needed with { UpdateBuild = "build-2", UpdateStartedAtUtc = T0 };

        state = await StepAsync(updater, state);
        _now = T0.AddSeconds(30);
        state = await StepAsync(updater, state);
        _now = T0 + RelayUpdater.InterruptedRetry;
        await StepAsync(updater, state);

        Assert.Equal(new long[] { 2, 2 }, _installer.Offsets);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task An_Installer_Is_Run_Once_Per_Cloud_Build()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        var updater = Updater();

        var state = await StepAsync(updater, Needed);
        for (var minute = 1; minute < 20; minute++)
        {
            _now = T0.AddMinutes(minute);
            state = await StepAsync(updater, state);
        }

        Assert.Single(_launcher.Launches);
        Assert.Null(state.UpdateError);
    }

    // On success the installer stops this service before it writes a word, so a sentence seen from the old build is a
    // refusal — said, and never retried.
    [Fact]
    public async Task An_Installer_That_Refused_Is_Reported_In_Its_Own_Words_And_Not_Run_Again()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        var updater = Updater();
        var state = await StepAsync(updater, Needed);
        await File.WriteAllTextAsync(Path.Combine(Folder, RelayUpdater.ResultFileName),
            "Ce PC est déjà le serveur du cabinet : il ne peut pas aussi être son PC de secours.\r\n");

        _now = T0.AddMinutes(1);
        state = await StepAsync(updater, state);
        _now = T0.AddHours(2);
        state = await StepAsync(updater, state);

        Assert.Single(_launcher.Launches);
        Assert.Equal("La mise à jour du PC de secours n'a pas pu s'installer : "
                     + "Ce PC est déjà le serveur du cabinet : il ne peut pas aussi être son PC de secours.", state.UpdateError);
        Assert.False(RelayUpdater.ReportsUpdating(state, "build-1", _now));
    }

    [Fact]
    public async Task An_Update_That_Never_Came_Back_Is_Reported_After_Half_An_Hour()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        var updater = Updater();
        var state = await StepAsync(updater, Needed);

        _now = T0 + RelayUpdater.LaunchGivesUpAfter;
        state = await StepAsync(updater, state);
        Assert.Null(state.UpdateError);

        _now = T0 + RelayUpdater.LaunchGivesUpAfter + TimeSpan.FromSeconds(1);
        state = await StepAsync(updater, state);
        Assert.Equal(RelayUpdater.NotLandedSentence, state.UpdateError);
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public async Task A_Launch_Windows_Refused_Is_Reported_And_Not_Retried()
    {
        _installer.Serve("build-2", new byte[] { 1, 2, 3 });
        _launcher.Refuses = true;
        var updater = Updater();

        var state = await StepAsync(updater, Needed);
        _now = T0.AddHours(1);
        state = await StepAsync(updater, state);

        Assert.Single(_launcher.Launches);
        Assert.Equal(RelayUpdater.NotStartedSentence, state.UpdateError);
    }

    // A failed series ends with its build: the next deploy is a new chance.
    [Fact]
    public async Task A_New_Cloud_Build_Starts_A_New_Series()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Combine(Folder, RelayUpdater.InstallerFileName + ".part"), new byte[] { 9, 9 });
        var failed = Needed with
        {
            UpdateBuild = "build-2", UpdateStartedAtUtc = T0, UpdateLaunchedAtUtc = T0, UpdateError = RelayUpdater.NotLandedSentence,
        };
        _installer.Serve("build-3", new byte[] { 1, 2, 3 });
        _now = T0.AddDays(1);

        var state = await StepAsync(Updater(), failed, cloudBuild: "build-3");

        Assert.Equal("build-3", state.UpdateBuild);
        Assert.Equal(_now, state.UpdateStartedAtUtc);
        Assert.Null(state.UpdateError);
        Assert.Equal(new long[] { 0 }, _installer.Offsets);
        Assert.Single(_launcher.Launches);
    }

    // ---- « Mise à jour » on the card ----------------------------------------------------------------------------

    [Fact]
    public void Updating_Is_Claimed_Only_While_It_Is_True()
    {
        var downloading = Needed with { UpdateBuild = "build-2", UpdateStartedAtUtc = T0 };
        var launched = downloading with { UpdateLaunchedAtUtc = T0.AddHours(2) };

        Assert.True(RelayUpdater.ReportsUpdating(downloading, "build-1", T0.AddMinutes(10)));
        Assert.False(RelayUpdater.ReportsUpdating(downloading, "build-1", T0 + RelayUpdater.ClaimsUpdatingFor));
        Assert.False(RelayUpdater.ReportsUpdating(downloading with { UpdateWaitingForInstaller = true }, "build-1", T0));
        Assert.False(RelayUpdater.ReportsUpdating(downloading with { UpdateError = "x" }, "build-1", T0));
        Assert.False(RelayUpdater.ReportsUpdating(downloading with { UpdateNeeded = false }, "build-1", T0));
        Assert.False(RelayUpdater.ReportsUpdating(downloading, "build-2", T0));
        Assert.True(RelayUpdater.ReportsUpdating(launched, "build-1", T0.AddHours(2).AddMinutes(5)));
        Assert.False(RelayUpdater.ReportsUpdating(launched, "build-1", T0.AddHours(2) + RelayUpdater.LaunchGivesUpAfter));
    }

    // A download still on its way never holds the tick: the heartbeat goes on while the bytes arrive.
    [Fact]
    public async Task A_Download_In_Progress_Does_Not_Hold_The_Tick()
    {
        var pending = new TaskCompletionSource<RelayInstallerFetch>();
        var slow = new SlowInstaller(pending.Task);
        var updater = new RelayUpdater(slow, _launcher, Folder, Path.Combine(_dir, "logs"), NullLogger.Instance, () => _now);

        var state = await StepAsync(updater, Needed);
        state = await StepAsync(updater, state);

        Assert.Equal(1, slow.Fetches);
        Assert.Empty(_launcher.Launches);
        Assert.True(RelayUpdater.ReportsUpdating(state, "build-1", _now));
    }

    private sealed class SlowInstaller : IRelayInstallerSource
    {
        private readonly Task<RelayInstallerFetch> _answer;

        public SlowInstaller(Task<RelayInstallerFetch> answer) => _answer = answer;

        public int Fetches { get; private set; }

        public Task<RelayInstallerFetch> FetchAsync(string partPath, string build, CancellationToken cancellationToken)
        {
            Fetches++;
            return _answer;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
