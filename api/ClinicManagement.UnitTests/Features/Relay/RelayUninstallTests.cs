using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Health = ClinicManagement.Domain.Services.ClinicRelayHealth;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// Uninstalling a PC de secours (<c>clinic-pc-copy</c> AC-8.3, AC-8.5): the cloud counts it as retiring, and the copy is
/// erased only once the cloud has answered — never on the PC that may hold the cabinet's last copy.
/// </summary>
public sealed class RelayUninstallTests : IDisposable
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-uninstall-" + Guid.NewGuid().ToString("N"));

    private static (ClinicRelay Relay, string Secret) PairedPc()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0);
        var secret = relay.Pair("PC-ACCUEIL", "key", null, null, null, T0);
        return (relay, secret);
    }

    private static (ReportRelayUninstalledCommandHandler Handler, List<AuditEntry> Journal) Cloud(ClinicRelay relay)
    {
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetByIdAcrossClinicsAsync(relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        var journal = new List<AuditEntry>();
        var audit = new Mock<IAuditEntryRepository>();
        audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => journal.AddRange(e))
            .Returns(Task.CompletedTask);
        return (new ReportRelayUninstalledCommandHandler(relays.Object, audit.Object, Mock.Of<IUnitOfWork>(),
            NullLogger<ReportRelayUninstalledCommandHandler>.Instance), journal);
    }

    private static string Sentence(ClinicRelay pc, DateTime now) => RelayLabels.Sentence(Health.Read(pc, now), pc, now);

    // AC-8.3 + AC-8.5: uninstalling counts as retiring, once, with one journal row.
    [Fact]
    public async Task An_Active_Pc_Uninstalled_Is_Retired_With_One_Journal_Row()
    {
        var (pc, secret) = PairedPc();
        var (handler, journal) = Cloud(pc);

        Assert.True((await handler.Handle(new ReportRelayUninstalledCommand(pc.Id, secret), default)).IsSuccess);
        Assert.True((await handler.Handle(new ReportRelayUninstalledCommand(pc.Id, secret), default)).IsSuccess);

        Assert.Equal(ClinicRelayStatus.Retired, pc.Status);
        Assert.Equal(ClinicRelayRetirement.Uninstalled, pc.RetiredReason);
        Assert.NotNull(pc.UninstalledAtUtc);
        Assert.Contains(RelayJournal.Uninstalled, Assert.Single(journal).ChangedFields);
        Assert.StartsWith("PC désinstallé le", Sentence(pc, T0.AddDays(2)));
        Assert.Contains("peut être restée", Sentence(pc, T0.AddDays(2)));
    }

    // Retired the day before, uninstalled today: the retirement keeps its reason and date, the uninstall still gets its row.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Pc_Retired_Earlier_Keeps_Its_Reason_And_Date(bool declaredLost)
    {
        var (pc, secret) = PairedPc();
        if (declaredLost)
        {
            pc.DeclareLost("local|admin", T0.AddDays(1));
        }
        else
        {
            pc.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddDays(1));
        }

        var (handler, journal) = Cloud(pc);
        Assert.True((await handler.Handle(new ReportRelayUninstalledCommand(pc.Id, secret), default)).IsSuccess);

        Assert.Equal(T0.AddDays(1), pc.RetiredAtUtc);
        Assert.Equal(declaredLost ? ClinicRelayRetirement.LostOrStolen : ClinicRelayRetirement.Retired, pc.RetiredReason);
        Assert.Single(journal);
        // « Perdu ou volé » outranks every other sentence: it is the one that tells the admin what to fear.
        Assert.StartsWith(declaredLost ? "Déclaré perdu ou volé" : "PC désinstallé le", Sentence(pc, T0.AddDays(2)));
    }

    [Fact]
    public async Task Only_The_Pc_Itself_May_Report_It()
    {
        var (pc, _) = PairedPc();
        var (handler, journal) = Cloud(pc);

        var refused = await handler.Handle(new ReportRelayUninstalledCommand(pc.Id, "not-the-secret"), default);

        Assert.Equal(RelayRefusals.UnknownRelayCode, refused.Code);
        Assert.NotEqual(ClinicRelayStatus.Retired, pc.Status);
        Assert.Null(pc.UninstalledAtUtc);
        Assert.Empty(journal);
    }

    [Fact]
    public void An_Uninstall_That_Erased_The_Copy_Says_So()
    {
        var (pc, _) = PairedPc();
        pc.MarkUninstalled(T0.AddDays(1));
        pc.MarkErased(T0.AddDays(1));

        Assert.StartsWith("PC désinstallé, copie effacée le", Sentence(pc, T0.AddDays(2)));
    }

    // ── The PC side ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Keeping_The_Copy_Tells_The_Cloud_And_Erases_Nothing()
    {
        var (uninstaller, cloud, store, erases) = Pc(RelayCallStatus.Ok);

        var result = await uninstaller.UninstallAsync(eraseCopy: false, T0, default);

        Assert.Equal(RelayUninstallOutcome.Recorded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "uninstalled" }, cloud.Calls);
        Assert.Empty(erases);
        Assert.True(store.Load().Released);
    }

    // The order is the design: the cloud first, the erase only after it answered, then the erase reported.
    [Theory]
    [InlineData(RelayCallStatus.Ok)]
    [InlineData(RelayCallStatus.Released)] // the cloud answered and no longer knows this PC: it is up, so not the last copy
    public async Task The_Copy_Is_Erased_Only_After_The_Cloud_Answered(RelayCallStatus answer)
    {
        var (uninstaller, cloud, store, erases) = Pc(answer);

        var result = await uninstaller.UninstallAsync(eraseCopy: true, T0, default);

        Assert.Equal(RelayUninstallOutcome.RecordedAndErased, result.Outcome);
        Assert.Equal(new[] { "uninstalled", "erase", "erased" }, cloud.Calls);
        Assert.Single(erases);
        Assert.Equal(5, result.FilesDeleted);
        Assert.True(store.Load().ErasureReported);
    }

    // A cloud that does not answer may be a lost cloud — and then this PC is the cabinet's last copy.
    [Theory]
    [InlineData(RelayCallStatus.Unreachable)]
    [InlineData(RelayCallStatus.Refused)]
    [InlineData(RelayCallStatus.NotFound)]
    public async Task Nothing_Is_Erased_Unless_The_Cloud_Answered(RelayCallStatus answer)
    {
        var (uninstaller, _, store, erases) = Pc(answer);

        var result = await uninstaller.UninstallAsync(eraseCopy: true, T0, default);

        Assert.Equal(RelayUninstallOutcome.CloudNotTold, result.Outcome);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(erases);
        Assert.Contains("la copie n'a pas été effacée", result.Sentence);
        Assert.False(store.Load().Released);
    }

    // AC-9.4's copy: stopped because the cloud went back in time, it holds records the cloud does not.
    [Fact]
    public async Task A_Copy_Newer_Than_The_Cloud_Is_Kept()
    {
        var (uninstaller, _, _, erases) = Pc(RelayCallStatus.Ok, stoppedReason: "Le cloud est revenu à un état antérieur.");

        var result = await uninstaller.UninstallAsync(eraseCopy: true, T0, default);

        Assert.Equal(RelayUninstallOutcome.KeptNewerCopy, result.Outcome);
        Assert.Equal(3, result.ExitCode);
        Assert.Empty(erases);
    }

    // The exit codes are a mirrored set: the verb returns them, the installer's uninstall step words each one. A code
    // the installer does not know falls to its catch-all sentence, which would tell the admin the cloud was not told.
    [Fact]
    public void The_Installer_Words_Every_Exit_Code_The_Verb_Returns()
    {
        var verbCodes = Enum.GetValues<RelayUninstallOutcome>()
            .Select(o => new RelayUninstallResult(o, 0, "").ExitCode)
            .Concat(new[] { 5, 6 }) // the verb's own two failure codes, around the erase (UninstallRelayConsoleCommand)
            .ToHashSet();

        var installer = File.ReadAllText(InstallerPath());
        var body = installer[installer.IndexOf("function UninstallSentence", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\nend;", StringComparison.Ordinal)];
        var worded = System.Text.RegularExpressions.Regex.Matches(body, @"^\s*(\d+):\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();

        Assert.NotEmpty(worded);
        Assert.Equal(verbCodes.OrderBy(c => c), worded.OrderBy(c => c));
    }

    private static string InstallerPath([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!,
            "..", "..", "..", "..", "packaging", "setup", "clinic-setup.iss"));
        Assert.True(File.Exists(path), $"clinic-setup.iss not found at {path}");
        return path;
    }

    [Fact]
    public async Task An_Unheard_Erase_Report_Is_Retried_Then_Said()
    {
        var (uninstaller, cloud, store, erases) = Pc(RelayCallStatus.Ok, erasedAnswer: RelayCallStatus.Unreachable);

        var result = await uninstaller.UninstallAsync(eraseCopy: true, T0, default);

        Assert.Equal(RelayUninstallOutcome.ErasedButNotReported, result.Outcome);
        Assert.Equal(4, result.ExitCode);
        Assert.Single(erases);
        Assert.Equal(3, cloud.Calls.Count(c => c == "erased"));
        Assert.False(store.Load().ErasureReported);
    }

    [Fact]
    public async Task A_Pc_Never_Paired_Has_Nothing_To_Tell()
    {
        var store = new RelayFollowerStateStore(_dir);
        var uninstaller = new RelayUninstaller(null, store, _ => throw new InvalidOperationException("nothing to erase"));

        var result = await uninstaller.UninstallAsync(eraseCopy: true, T0, default);

        Assert.Equal(RelayUninstallOutcome.NotPaired, result.Outcome);
        Assert.Equal(0, result.ExitCode);
    }

    // [AC-7.3] A PC holding work saved during a cut that never reached the cloud: that work's only copy is never erased.
    [Fact]
    public async Task A_Copy_Holding_A_Cuts_Work_Is_Kept()
    {
        var (uninstaller, _, store, erases) = Pc(RelayCallStatus.Ok, holdsCutWork: true);

        var result = await uninstaller.UninstallAsync(eraseCopy: true, T0, default);

        Assert.Equal(RelayUninstallOutcome.KeptNewerCopy, result.Outcome);
        Assert.Empty(erases);
        Assert.Contains("coupure d'internet", result.Sentence);
        Assert.True(store.Load().Released);
    }

    private (RelayUninstaller Uninstaller, FakeCloud Cloud, RelayFollowerStateStore Store, List<int> Erases) Pc(
        RelayCallStatus uninstalledAnswer, RelayCallStatus erasedAnswer = RelayCallStatus.Ok, string? stoppedReason = null,
        bool holdsCutWork = false)
    {
        var store = new RelayFollowerStateStore(_dir);
        store.Save(new RelayFollowerState { StoppedReason = stoppedReason });
        var cloud = new FakeCloud(uninstalledAnswer, erasedAnswer);
        var erases = new List<int>();
        var uninstaller = new RelayUninstaller(cloud, store, _ =>
        {
            cloud.Calls.Add("erase");
            erases.Add(1);
            store.Save(store.Load() with { ErasedAtUtc = T0 });
            return Task.FromResult(5);
        }, (_, _) => Task.CompletedTask, () => holdsCutWork);
        return (uninstaller, cloud, store, erases);
    }

    private sealed class FakeCloud : IRelayCloudClient
    {
        private readonly RelayCallStatus _uninstalled;
        private readonly RelayCallStatus _erased;

        public FakeCloud(RelayCallStatus uninstalled, RelayCallStatus erased)
        {
            _uninstalled = uninstalled;
            _erased = erased;
        }

        public List<string> Calls { get; } = new();

        public Task<RelayCall<bool>> ReportUninstalledAsync(CancellationToken cancellationToken)
        {
            Calls.Add("uninstalled");
            return Task.FromResult(new RelayCall<bool>(_uninstalled, _uninstalled == RelayCallStatus.Ok));
        }

        public Task<RelayCall<bool>> ReportErasedAsync(CancellationToken cancellationToken)
        {
            Calls.Add("erased");
            return Task.FromResult(new RelayCall<bool>(_erased, _erased == RelayCallStatus.Ok));
        }

        public Task<RelayCall<RelayHeartbeatAck>> HeartbeatAsync(RelayHeartbeatRequest report, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<RelayFeedBatch>> ChangesAsync(long after, string? fingerprint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<string>> SnapshotAsync(IReadOnlyCollection<string>? tables, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<IReadOnlyList<RelayTableDigest>>> DigestAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<string>> BlobAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<IReadOnlyList<string>>> MissingHandbackFilesAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<bool>> UploadHandbackFileAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<RelayHandbackResultDto>> HandBackAsync(RelayHandbackRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<RelayHandbackResultDto>> ListOverruledCutAsync(RelayHandbackRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<IReadOnlyList<RelayRowHashDto>>> GapHashesAsync(string table, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayCall<RelayHandbackResultDto>> ReturnGapAsync(RelayGapRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
