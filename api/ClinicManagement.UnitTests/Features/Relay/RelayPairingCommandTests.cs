using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// Setting up a PC de secours (<c>clinic-pc-copy</c> AC-1.4, AC-1.10, AC-1.11, EC-8, EC-9): the one-time code, the
/// clinic's one place, and the installer's exchange of that code for the PC's own secret.
/// </summary>
public class RelayPairingCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static User Admin(string role = "admin") =>
        User.CreateLocalUser(ClinicId, role, "admin@cabinet.tn", "hash", "Dr Admin");

    private sealed class IssueHarness
    {
        public Mock<IClinicContext> Context { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IClinicRelayRepository> Relays { get; } = new();
        public Mock<IAuditEntryRepository> Audit { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<ClinicRelay> Added { get; } = new();
        public List<AuditEntry> Journal { get; } = new();

        public IssueHarness(User caller, ClinicRelay? current = null)
        {
            Context.Setup(c => c.GetUserId()).Returns(caller.Id);
            Users.Setup(u => u.GetByAuth0SubAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);
            Relays.Setup(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(current);
            Relays.Setup(r => r.AddAsync(It.IsAny<ClinicRelay>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicRelay, CancellationToken>((r, _) => Added.Add(r))
                .Returns(Task.CompletedTask);
            Audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => Journal.AddRange(e))
                .Returns(Task.CompletedTask);
        }

        public IssueRelayPairingCodeCommandHandler Handler()
        {
            var actor = new Mock<IAuditActorProvider>();
            actor.SetupGet(a => a.Current).Returns(new AuditActor("local|admin", "admin@cabinet.tn"));
            return new IssueRelayPairingCodeCommandHandler(Context.Object, Users.Object, Relays.Object, Audit.Object,
                actor.Object, UnitOfWork.Object, NullLogger<IssueRelayPairingCodeCommandHandler>.Instance);
        }
    }

    // [AC-1.4] An administrator gets a one-time code; nothing else about the PC exists yet.
    [Fact]
    public async Task An_Admin_Starts_A_Setup_And_Receives_A_Code()
    {
        var harness = new IssueHarness(Admin());

        var result = await harness.Handler().Handle(new IssueRelayPairingCodeCommand("PC-ACCUEIL"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var relay = Assert.Single(harness.Added);
        Assert.Equal(ClinicRelayStatus.Pairing, relay.Status);
        Assert.True(relay.CodeMatches(result.Value!.Code, DateTime.UtcNow));
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task Only_An_Admin_May_Start_A_Setup(string role)
    {
        var harness = new IssueHarness(Admin(role));

        var result = await harness.Handler().Handle(new IssueRelayPairingCodeCommand("PC"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RelayRefusals.NotAdminCode, result.Code);
        Assert.Empty(harness.Added);
    }

    // [AC-1.10] The clinic has one place, and a live PC holds it.
    [Fact]
    public async Task A_Second_Setup_Is_Refused_While_A_Pc_Holds_The_Place()
    {
        var (live, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|other", DateTime.UtcNow);
        var harness = new IssueHarness(Admin(), live);

        var result = await harness.Handler().Handle(new IssueRelayPairingCodeCommand("PC-SALLE"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RelayRefusals.AlreadyPairedCode, result.Code);
        Assert.Contains("PC-ACCUEIL", result.Error);
        Assert.Empty(harness.Added);
    }

    // [AC-1.11][EC-9] An expired, unused code frees the place: it is retired as abandoned, journalled, and replaced.
    [Fact]
    public async Task An_Expired_Code_Frees_The_Place_And_Is_Journalled()
    {
        var (stale, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ANCIEN", "local|other", DateTime.UtcNow.AddHours(-1));
        var harness = new IssueHarness(Admin(), stale);

        var result = await harness.Handler().Handle(new IssueRelayPairingCodeCommand("PC-NOUVEAU"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ClinicRelayStatus.Retired, stale.Status);
        Assert.Equal(ClinicRelayRetirement.Abandoned, stale.RetiredReason);
        var row = Assert.Single(harness.Journal);
        Assert.Contains(RelayJournal.Abandoned, row.ChangedFields);
        Assert.Single(harness.Added);
        // The release commits before the new row claims the place: one save could order the INSERT first.
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // [EC-8] Two admins at once: the filtered unique index refuses the second, and it is told who won.
    [Fact]
    public async Task A_Lost_Race_Is_Reported_As_Already_Paired()
    {
        var harness = new IssueHarness(Admin());
        harness.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("unique"));

        var result = await harness.Handler().Handle(new IssueRelayPairingCodeCommand("PC"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RelayRefusals.AlreadyPairedCode, result.Code);
    }

    private sealed class PairHarness
    {
        public Mock<IClinicRelayRepository> Relays { get; } = new();
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IClinicRelayRowStore> Rows { get; } = new();
        public Mock<IRelayKeyValidator> Keys { get; } = new();
        public Mock<IAuditEntryRepository> Audit { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<AuditEntry> Journal { get; } = new();

        public PairHarness(ClinicRelay? relay, bool validKey = true)
        {
            Relays.Setup(r => r.FindByPairingCodeAcrossClinicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(relay);
            Keys.Setup(k => k.IsValidPublicKey(It.IsAny<string?>())).Returns(validKey);
            Audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => Journal.AddRange(e))
                .Returns(Task.CompletedTask);
        }

        public PairRelayCommandHandler Handler() => new(Relays.Object, Clinics.Object, Users.Object, Rows.Object,
            Keys.Object, Audit.Object, UnitOfWork.Object, NullLogger<PairRelayCommandHandler>.Instance);
    }

    private static PairRelayCommand Pair(string code) => new(code, "PC-ACCUEIL", "spki-key", null, "192.168.1.10", "1.4.0");

    // [D8] The installer's code pairs the PC once: it gets its secret, the change log opens, and the step is journalled.
    [Fact]
    public async Task A_Valid_Code_Pairs_The_Pc_And_Opens_The_Change_Log()
    {
        var (relay, code) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        var harness = new PairHarness(relay);

        var result = await harness.Handler().Handle(Pair(code), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ClinicRelayStatus.Seeding, relay.Status);
        Assert.True(relay.SecretMatches(result.Value!.Secret));
        harness.Rows.Verify(r => r.EnsureCursorAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(RelayJournal.Setup, Assert.Single(harness.Journal).ChangedFields);
    }

    [Fact]
    public async Task An_Unknown_Code_Is_Refused_As_Expired()
    {
        var harness = new PairHarness(relay: null);

        var result = await harness.Handler().Handle(Pair("inconnu"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RelayRefusals.PairingCodeExpiredCode, result.Code);
        harness.Rows.Verify(r => r.EnsureCursorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Expired_Code_Is_Refused()
    {
        var (relay, code) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", DateTime.UtcNow.AddHours(-1));
        var harness = new PairHarness(relay);

        var result = await harness.Handler().Handle(Pair(code), CancellationToken.None);

        Assert.Equal(RelayRefusals.PairingCodeExpiredCode, result.Code);
        Assert.Equal(ClinicRelayStatus.Pairing, relay.Status);
    }

    // A key the cloud cannot seal for would leave the PC unable to open any second factor.
    [Fact]
    public async Task A_Public_Key_The_Cloud_Cannot_Seal_For_Is_Refused_Before_Pairing()
    {
        var (relay, code) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", DateTime.UtcNow);
        var harness = new PairHarness(relay, validKey: false);

        var result = await harness.Handler().Handle(Pair(code), CancellationToken.None);

        Assert.Equal(RelayRefusals.InvalidRequestCode, result.Code);
        Assert.Equal(ClinicRelayStatus.Pairing, relay.Status);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
