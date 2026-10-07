using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>« Déclarer perdu ou volé » (<c>clinic-pc-copy</c> AC-8.4, AC-8.5, EC-14).</summary>
public class DeclareRelayLostCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<IClinicRelayRowStore> _rows = new();
    private readonly Mock<IAuditEntryRepository> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<AuditEntry> _journal = new();
    private readonly List<User> _members = new();

    private static User Member(string role, string email, bool withAuthenticator = false)
    {
        var user = User.CreateLocalUser(ClinicId, role, email, "hash", email);
        if (withAuthenticator)
        {
            user.IssueTotpSecret("protected:JBSWY3DPEHPK3PXP");
            user.CompleteTotpEnrolment(Enumerable.Range(0, 8).Select(_ => UserRecoveryCode.NewCode()).ToList());
        }

        return user;
    }

    private DeclareRelayLostCommandHandler Handler(User caller, ClinicRelay? relay)
    {
        _context.Setup(c => c.GetUserId()).Returns(caller.Id);
        foreach (var member in _members)
        {
            _users.Setup(u => u.GetByAuth0SubAsync(member.Id, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        }

        _users.Setup(u => u.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<User>(_members, 1, _members.Count, _members.Count));
        _relays.Setup(r => r.GetLatestForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        _audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => _journal.AddRange(e))
            .Returns(Task.CompletedTask);

        var actor = new Mock<IAuditActorProvider>();
        actor.SetupGet(a => a.Current).Returns(new AuditActor(caller.Id, caller.Email));
        return new DeclareRelayLostCommandHandler(_context.Object, _users.Object, _relays.Object, _rows.Object,
            _audit.Object, actor.Object, _unitOfWork.Object, NullLogger<DeclareRelayLostCommandHandler>.Instance);
    }

    private static ClinicRelay PairedPc()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0);
        relay.Pair("PC-ACCUEIL", "key", null, null, null, T0);
        return relay;
    }

    // AC-8.4: the PC is retired as lost, and EVERY account — the inactive one too — must choose new credentials.
    [Fact]
    public async Task The_Pc_Is_Retired_And_Every_Account_Must_Choose_New_Credentials()
    {
        var admin = Member(User.RoleAdmin, "admin@cabinet.tn", withAuthenticator: true);
        var doctor = Member(User.RoleDoctor, "dr@cabinet.tn", withAuthenticator: true);
        var secretary = Member(User.RoleSecretary, "accueil@cabinet.tn");
        secretary.Deactivate();
        _members.AddRange(new[] { admin, doctor, secretary });
        var tokenVersions = _members.ToDictionary(m => m.Id, m => m.TokenVersion);
        var pc = PairedPc();

        var result = await Handler(admin, pc).Handle(new DeclareRelayLostCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.LostOrStolen);
        Assert.Equal(ClinicRelayRetirement.LostOrStolen, pc.RetiredReason);
        Assert.StartsWith("Déclaré perdu ou volé le", result.Value.Sentence);
        foreach (var member in _members)
        {
            Assert.True(member.MustChangePassword, member.Email);
            Assert.False(member.IsTotpEnrolled, member.Email);
            Assert.True(member.TokenVersion > tokenVersions[member.Id], member.Email);
        }

        // The authenticator is REPLACED for those who had one, and nobody else is made to get one.
        Assert.True(admin.TotpReenrolmentRequired);
        Assert.True(doctor.TotpReenrolmentRequired);
        Assert.False(secretary.TotpReenrolmentRequired);
        Assert.Empty(admin.RecoveryCodes);

        // AC-8.5: one journal row, saying what happened and to how many accounts.
        var row = Assert.Single(_journal);
        Assert.Contains(RelayJournal.Lost, row.ChangedFields);
        Assert.Contains("3 compte(s)", row.ChangedFields);
        _rows.Verify(r => r.DropCursorAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // EC-14: a PC retired earlier and then stolen is the ordinary case.
    [Fact]
    public async Task A_Pc_Already_Retired_Can_Still_Be_Declared_Lost()
    {
        var admin = Member(User.RoleAdmin, "admin@cabinet.tn");
        _members.Add(admin);
        var pc = PairedPc();
        pc.Retire(ClinicRelayRetirement.Retired, admin.Id, T0.AddDays(1));

        var result = await Handler(admin, pc).Handle(new DeclareRelayLostCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ClinicRelayRetirement.LostOrStolen, pc.RetiredReason);
        Assert.Equal(T0.AddDays(1), pc.RetiredAtUtc);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task Only_An_Admin_May_Declare_It(string role)
    {
        var caller = Member(role, "x@cabinet.tn");
        _members.Add(caller);

        var result = await Handler(caller, PairedPc()).Handle(new DeclareRelayLostCommand(), CancellationToken.None);

        Assert.Equal(RelayRefusals.NotAdminCode, result.Code);
        Assert.False(caller.MustChangePassword);
    }

    // A code nobody typed in never put anything on a PC: nothing to have stolen, nobody's credentials touched.
    [Fact]
    public async Task A_Setup_Code_That_Was_Never_Used_Is_Not_A_Pc()
    {
        var admin = Member(User.RoleAdmin, "admin@cabinet.tn");
        _members.Add(admin);
        var (unused, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", T0);

        var result = await Handler(admin, unused).Handle(new DeclareRelayLostCommand(), CancellationToken.None);

        Assert.Equal(RelayRefusals.NoRelayCode, result.Code);
        Assert.False(admin.MustChangePassword);
    }

    // The flag ends with the new enrolment it asked for.
    [Fact]
    public void A_New_Enrolment_Clears_The_Requirement()
    {
        var doctor = Member(User.RoleDoctor, "dr@cabinet.tn", withAuthenticator: true);
        doctor.RequireNewCredentials();

        doctor.IssueTotpSecret("protected:NEWSECRET");
        doctor.CompleteTotpEnrolment(new[] { UserRecoveryCode.NewCode() });

        Assert.False(doctor.TotpReenrolmentRequired);
        Assert.True(doctor.IsTotpEnrolled);
    }
}
