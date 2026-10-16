using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The Windows app's offer (<c>clinic-pc-copy</c> AC-1.1, AC-1.5, AC-1.8, AC-1.10, AC-1.11): whether a setup may start,
/// how much room it needs, the « admin on any PC » door, and giving back a code the installer never presented.
/// </summary>
public class RelayInstallOfferTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const long FileBytes = 5L << 30;

    private static User Account(string role = "admin", bool enrolled = true)
    {
        var user = User.CreateLocalUser(ClinicId, role, "admin@cabinet.tn", "hash", "Dr Admin");
        if (enrolled)
        {
            user.IssueTotpSecret("sealed-secret");
            user.CompleteTotpEnrolment(Array.Empty<string>());
        }

        return user;
    }

    // ── The room a PC de secours needs (AC-1.8) ─────────────────────────────────────────────────────────

    [Fact]
    public void The_Room_Needed_Is_The_Records_And_Files_Twice_Over()
    {
        Assert.Equal(2 * (FileBytes + RelayFootprint.RecordsAllowanceBytes), RelayFootprint.NeedBytes(FileBytes));
        Assert.Equal(2 * RelayFootprint.RecordsAllowanceBytes, RelayFootprint.NeedBytes(0));
    }

    // ── Whether the offer may appear (AC-1.1, AC-1.10) ──────────────────────────────────────────────────

    private static async Task<RelayStatusDto> StatusFor(ClinicRelay? latest)
    {
        var admin = Account();
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(admin.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetLatestForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(latest);
        relays.Setup(r => r.GetHostedFileBytesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(FileBytes);

        var result = await new GetRelayStatusQueryHandler(context.Object, users.Object, relays.Object)
            .Handle(new GetRelayStatusQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task A_Clinic_With_No_Pc_May_Install_One_And_Is_Told_The_Room_It_Needs()
    {
        var status = await StatusFor(latest: null);

        Assert.True(status.CanInstall);
        Assert.Equal(RelayFootprint.NeedBytes(FileBytes), status.NeedBytes);
    }

    [Fact]
    public async Task A_Setup_In_Progress_Holds_The_Place_On_Every_Device()
    {
        var (pairing, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);

        var status = await StatusFor(pairing);

        Assert.False(status.CanInstall);
        Assert.Equal(0, status.NeedBytes);
    }

    // [AC-1.11][EC-9] A code nobody presented lapses after ten minutes and frees the place.
    [Fact]
    public async Task A_Lapsed_Unused_Code_Leaves_The_Place_Free()
    {
        var (stale, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ANCIEN", "local|admin", DateTime.UtcNow.AddHours(-1));

        Assert.True((await StatusFor(stale)).CanInstall);
    }

    [Fact]
    public async Task A_Retired_Pc_Leaves_The_Place_Free()
    {
        var (retired, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ANCIEN", "local|admin", DateTime.UtcNow);
        retired.Retire(ClinicRelayRetirement.Retired, "local|admin", DateTime.UtcNow);

        Assert.True((await StatusFor(retired)).CanInstall);
    }

    // [AC-1.8] The code carries the cloud's figure, which the installer refuses on (/NEEDBYTES=).
    [Fact]
    public async Task A_Pairing_Code_Carries_The_Room_The_Pc_Needs()
    {
        var harness = new DoorHarness(Account());

        var result = await harness.Handle();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(RelayFootprint.NeedBytes(FileBytes), result.Value!.NeedBytes);
    }

    // ── « Installer le PC de secours ici… » on any PC (AC-1.5) ─────────────────────────────────────────

    private sealed class DoorHarness
    {
        public Mock<ISender> Sender { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IClinicRelayRepository> Relays { get; } = new();
        public Mock<IAuditEntryRepository> Audit { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public TenantScope Scope { get; } = new(NullLogger<TenantScope>.Instance);
        public List<ClinicRelay> Added { get; } = new();
        public List<string> EndedSessions { get; } = new();
        public int SignIns { get; private set; }

        public DoorHarness(User account, Result<LoginResultDto>? login = null, bool mustChangePassword = false)
        {
            login ??= Result<LoginResultDto>.Success(new LoginResultDto
            {
                RefreshToken = "refresh-of-the-door",
                MustChangePassword = mustChangePassword,
                User = new UserDto { Id = account.Id, ClinicId = account.ClinicId, Role = account.Role },
            });

            Sender.Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
                .Callback(() => SignIns++)
                .ReturnsAsync(login);
            Sender.Setup(s => s.Send(It.IsAny<EndSessionCommand>(), It.IsAny<CancellationToken>()))
                .Callback<IRequest<Result>, CancellationToken>((c, _) => EndedSessions.Add(((EndSessionCommand)c).RefreshToken))
                .ReturnsAsync(Result.Success());
            Users.Setup(u => u.GetByAuth0SubAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
            Relays.Setup(r => r.GetHostedFileBytesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(FileBytes);
            Relays.Setup(r => r.AddAsync(It.IsAny<ClinicRelay>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicRelay, CancellationToken>((r, _) => Added.Add(r))
                .Returns(Task.CompletedTask);
        }

        public Task<Result<RelayPairingCodeDto>> Handle(string totpCode = "123456") =>
            new IssueRelayPairingCodeWithCredentialsCommandHandler(Sender.Object, Users.Object, Relays.Object,
                    Audit.Object, UnitOfWork.Object, Scope,
                    NullLogger<IssueRelayPairingCodeWithCredentialsCommandHandler>.Instance)
                .Handle(new IssueRelayPairingCodeWithCredentialsCommand("admin@cabinet.tn", "pw", totpCode, "PC-ACCUEIL"),
                    CancellationToken.None);
    }

    [Fact]
    public async Task An_Admins_Credentials_Start_A_Setup_And_Leave_No_Session_Behind()
    {
        var harness = new DoorHarness(Account());

        var result = await harness.Handle();

        Assert.True(result.IsSuccess, result.Error);
        var relay = Assert.Single(harness.Added);
        Assert.Equal(ClinicId, relay.ClinicId);
        Assert.True(relay.CodeMatches(result.Value!.Code, DateTime.UtcNow));
        Assert.Equal("refresh-of-the-door", Assert.Single(harness.EndedSessions));
        // An anonymous request reads zero rows unless the clinic is declared before the clinic's PC is looked up.
        Assert.Equal(TenantScopeKind.Clinic, harness.Scope.Kind);
        Assert.Equal(ClinicId, harness.Scope.ClinicId);
    }

    // The sign-in's own refusal — wrong password, wrong code, lockout — reaches the caller untouched.
    [Fact]
    public async Task A_Refused_Sign_In_Is_Passed_Through_And_Starts_Nothing()
    {
        var refused = Result<LoginResultDto>.Failure(
            ClinicAuthRefusals.MessageFor(ClinicAuthRefusals.InvalidCredentials)!, ClinicAuthRefusals.InvalidCredentials);
        var harness = new DoorHarness(Account(), refused);

        var result = await harness.Handle();

        Assert.Equal(ClinicAuthRefusals.InvalidCredentials, result.Code);
        Assert.Empty(harness.Added);
        Assert.Empty(harness.EndedSessions);
    }

    // A code is the point of this door: none, and no attempt is spent on the password.
    [Fact]
    public async Task No_Code_Is_Refused_Before_The_Password_Is_Checked()
    {
        var harness = new DoorHarness(Account());

        var result = await harness.Handle(totpCode: " ");

        Assert.Equal(ClinicAuthRefusals.TotpRequired, result.Code);
        Assert.Equal(0, harness.SignIns);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task A_Colleague_Who_Is_Not_An_Admin_Is_Refused_And_Their_Session_Ended(string role)
    {
        var harness = new DoorHarness(Account(role));

        var result = await harness.Handle();

        Assert.Equal(RelayRefusals.NotAdminCode, result.Code);
        Assert.Empty(harness.Added);
        Assert.Single(harness.EndedSessions);
    }

    // Where the deployment does not demand a factor of an admin, the sign-in accepts a password alone; this door does not.
    [Fact]
    public async Task An_Admin_Without_An_Authenticator_Is_Refused()
    {
        var harness = new DoorHarness(Account(enrolled: false));

        var result = await harness.Handle();

        Assert.Equal(RelayRefusals.AuthenticatorRequiredCode, result.Code);
        Assert.Empty(harness.Added);
        Assert.Single(harness.EndedSessions);
    }

    // ⚠️ Not `must_change_password`: the browser routes that code to a page this door has no session for.
    [Fact]
    public async Task An_Admin_Who_Must_Change_Their_Password_Is_Refused_With_The_Relays_Own_Code()
    {
        var harness = new DoorHarness(Account(), mustChangePassword: true);

        var result = await harness.Handle();

        Assert.Equal(RelayRefusals.PasswordChangeRequiredCode, result.Code);
        Assert.NotEqual("must_change_password", result.Code);
        Assert.Empty(harness.Added);
        Assert.Single(harness.EndedSessions);
    }

    // ── Giving back a code the installer never presented (AC-1.11) ─────────────────────────────────────

    private sealed class ReleaseHarness
    {
        public Mock<IClinicRelayRepository> Relays { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IAuditEntryRepository> Audit { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<AuditEntry> Journal { get; } = new();

        public ReleaseHarness(ClinicRelay? relay)
        {
            Relays.Setup(r => r.FindByPairingCodeAcrossClinicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(relay);
            Audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => Journal.AddRange(e))
                .Returns(Task.CompletedTask);
        }

        public Task<Result> Release(string code) =>
            new ReleaseRelayPairingCodeCommandHandler(Relays.Object, Users.Object, Audit.Object, UnitOfWork.Object,
                    NullLogger<ReleaseRelayPairingCodeCommandHandler>.Instance)
                .Handle(new ReleaseRelayPairingCodeCommand(code), CancellationToken.None);
    }

    [Fact]
    public async Task A_Code_Given_Back_Frees_The_Place_At_Once_And_Is_Journalled()
    {
        var (relay, code) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        var harness = new ReleaseHarness(relay);

        var result = await harness.Release(code);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClinicRelayStatus.Retired, relay.Status);
        Assert.Equal(ClinicRelayRetirement.Abandoned, relay.RetiredReason);
        Assert.False(relay.OccupiesTheClinic(DateTime.UtcNow));
        Assert.Contains(RelayJournal.Abandoned, Assert.Single(harness.Journal).ChangedFields);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Found live: a « Non » to Windows' prompt read « Installation abandonnée » — a problem state that rings the admins'
    // bell — for a setup that never existed. It reads like a code that lapsed: no PC, and the offer back.
    [Fact]
    public async Task A_Code_Given_Back_Reads_As_No_Pc_Never_As_An_Abandoned_Setup()
    {
        var (relay, code) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        await new ReleaseHarness(relay).Release(code);

        var status = await StatusFor(relay);

        Assert.False(status.Exists);
        Assert.Equal("none", status.State);
        Assert.False(status.IsProblem);
        Assert.True(status.CanInstall);
    }

    // A PC that WAS paired and never finished its first copy stays « abandoned » (EC-9): that one is a problem.
    [Fact]
    public async Task A_Paired_Setup_Retired_As_Abandoned_Is_Still_Reported()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        relay.Pair("PC-ACCUEIL", "spki", null, null, "1.4.0", DateTime.UtcNow);
        relay.Retire(ClinicRelayRetirement.Abandoned, "local|admin", DateTime.UtcNow);

        var status = await StatusFor(relay);

        Assert.Equal("abandoned", status.State);
    }

    // Whoever holds a code may not retire a PC it already paired: that is an admin's act, from « Paramètres ».
    [Fact]
    public async Task A_Code_Already_Presented_Releases_Nothing()
    {
        var (relay, code) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", DateTime.UtcNow);
        relay.Pair("PC-ACCUEIL", "spki", null, null, "1.4.0", DateTime.UtcNow);
        var harness = new ReleaseHarness(relay);

        var result = await harness.Release(code);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClinicRelayStatus.Seeding, relay.Status);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The same answer for every code, so the endpoint cannot be used to learn whether one exists.
    [Theory]
    [InlineData("inconnu")]
    [InlineData("")]
    public async Task An_Unknown_Code_Answers_Like_Any_Other(string code)
    {
        var harness = new ReleaseHarness(relay: null);

        var result = await harness.Release(code);

        Assert.True(result.IsSuccess);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_Paired_Pc_Cannot_Be_Released_By_Its_Code()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", DateTime.UtcNow);
        relay.Pair("PC", "spki", null, null, "1.4.0", DateTime.UtcNow);

        Assert.False(relay.ReleaseUnusedCode(DateTime.UtcNow));
        Assert.NotEqual(ClinicRelayStatus.Retired, relay.Status);
    }
}
