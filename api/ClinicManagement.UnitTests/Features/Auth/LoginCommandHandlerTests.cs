using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

public class LoginCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    // Per-source lockout tracker (security-hardening US-4). Default mock: never locked, so the existing
    // cases exercise the same paths as before; the per-source cases below drive it explicitly.
    private readonly Mock<ILoginAttemptTracker> _attempts = new();

    // hosted-security-hardening FR-1.1. Default: this deployment does NOT require a second factor of admins and
    // the fixture's user has not enrolled one, so every pre-existing case below exercises exactly the ladder it
    // did before. The second-factor cases live in `ClinicTotpAuthTests`, which drives these explicitly.
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<IUserSecretProtector> _secrets = new();
    private readonly Mock<ISecondFactorPolicy> _secondFactor = new();
    private readonly Mock<ISessionFamilyRepository> _sessionFamilies = new();

    /// <summary>
    /// The TOTP replay guard, permissive by default: every scenario here predates it and asserts something else,
    /// so a first presentation must behave exactly as it did. A test about the replay overrides this.
    /// </summary>
    private readonly Mock<ITotpReplayGuard> _replay = new();

    private readonly Mock<IAuditActorProvider> _auditActor = new();

    public LoginCommandHandlerTests()
    {
        _replay.Setup(g => g.TryConsume(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        // Every successful login now also issues the durable refresh token stored in the BFF cookie
        // (security-hardening US-5). Set up once here so the per-test arrangements stay focused on what they
        // are actually asserting; individual tests override it where the refresh token itself is the subject.
        _auth.Setup(a => a.GenerateRefreshToken(It.IsAny<User>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .Returns(new LocalAuthToken("refresh-jwt", DateTime.UtcNow.AddHours(12)));
    }

    private LoginCommandHandler Handler() => new(
        _users.Object, _auth.Object, _uow.Object, _attempts.Object,
        _totp.Object, _replay.Object, _secrets.Object, _secondFactor.Object,
        _sessionFamilies.Object, _auditActor.Object);

    private static User LocalUser(bool mustChangePassword = false) =>
        User.CreateLocalUser(ClinicId, "doctor", "Doc@Clinic.com", "STORED-HASH", "Dr House", mustChangePassword);

    private static LoginCommand Command() => new() { Email = "doc@clinic.com", Password = "s3cret!!" };

    private void SaveSucceeds() =>
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

    // [AC-5.3][AC-5.5] A login returns BOTH credentials: the short-lived access token the browser holds, and
    // the durable refresh token the BFF puts in its HttpOnly cookie. They must be different values — if the
    // same token were used for both, the cookie would carry a working API bearer and the whole separation
    // would be cosmetic.
    [Fact]
    public async Task Handle_Should_Issue_Both_An_Access_And_A_Refresh_Token()
    {
        var user = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);
        _auth.Setup(a => a.GenerateToken(user, It.IsAny<Guid?>())).Returns(new LocalAuthToken("access-jwt", DateTime.UtcNow.AddMinutes(30)));
        _auth.Setup(a => a.GenerateRefreshToken(user, It.IsAny<Guid?>(), It.IsAny<bool>())).Returns(new LocalAuthToken("refresh-jwt", DateTime.UtcNow.AddHours(12)));
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-jwt", result.Value!.AccessToken);
        Assert.Equal("refresh-jwt", result.Value.RefreshToken);
        Assert.NotEqual(result.Value.AccessToken, result.Value.RefreshToken);
    }

    // ---- Per-source lockout (security-hardening US-4 / AC-4.2) ----

    // [AC-4.2] This source has burned its attempts → refused BEFORE the password is verified, so a
    // brute-force attempt is actually stopped rather than merely counted.
    [Fact]
    public async Task Handle_Should_Refuse_When_This_Source_Is_Locked_Out()
    {
        var user = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _attempts.Setup(a => a.IsLockedOutForCurrentSource(user.Id)).Returns(true);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("bloqué", result.Error);
        // The password must never be checked for a source that is already refused.
        _auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // [AC-4.2] A failure is recorded against this source as well as the durable per-account counter, so the
    // offending machine is the one that gets locked out.
    [Fact]
    public async Task Handle_Should_Record_The_Failure_Against_This_Source()
    {
        var user = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Failed);
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _attempts.Verify(a => a.RecordFailure(user.Id), Times.Once);
    }

    // [AC-4.2] A user who simply mistyped should not carry a penalty into their next session.
    [Fact]
    public async Task Handle_Should_Clear_This_Source_On_Success()
    {
        var user = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);
        _auth.Setup(a => a.GenerateToken(user, It.IsAny<Guid?>())).Returns(new LocalAuthToken("jwt", DateTime.UtcNow.AddHours(12)));
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _attempts.Verify(a => a.ClearForCurrentSource(user.Id), Times.Once);
    }

    // [AC-4.2] Both lockout tiers must give the SAME message — the caller must not learn which brake
    // stopped them, or the per-source design becomes an oracle for "is this account locked elsewhere".
    [Fact]
    public async Task Both_Lockout_Tiers_Report_The_Same_Message()
    {
        var sourceLocked = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(sourceLocked);
        _attempts.Setup(a => a.IsLockedOutForCurrentSource(sourceLocked.Id)).Returns(true);
        var perSource = await Handler().Handle(Command(), CancellationToken.None);

        var accountLocked = LocalUser();
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++) accountLocked.RecordFailedLogin();
        Assert.True(accountLocked.IsLockedOut());

        var users2 = new Mock<IUserRepository>();
        users2.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(accountLocked);
        var attempts2 = new Mock<ILoginAttemptTracker>(); // not locked for this source
        var perAccount = await new LoginCommandHandler(
                users2.Object, _auth.Object, _uow.Object, attempts2.Object,
                _totp.Object, _replay.Object, _secrets.Object, _secondFactor.Object,
                _sessionFamilies.Object, _auditActor.Object)
            .Handle(Command(), CancellationToken.None);

        Assert.Equal(perSource.Error, perAccount.Error);
    }

    // [AC-3.1][AC-3.3] Valid credentials → JWT issued, login recorded, MustChangePassword surfaced.
    [Fact]
    public async Task Handle_Should_Return_Token_On_Valid_Credentials()
    {
        var user = LocalUser(mustChangePassword: true);
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);
        _auth.Setup(a => a.GenerateToken(user, It.IsAny<Guid?>())).Returns(new LocalAuthToken("jwt-token", DateTime.UtcNow.AddHours(12)));
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("jwt-token", result.Value!.AccessToken);
        Assert.True(result.Value.MustChangePassword);
        Assert.Equal(ClinicId, result.Value.User.ClinicId);
        Assert.NotNull(user.LastLoginAt);
        _users.Verify(r => r.Update(user), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-3.4] Wrong password → generic failure, failed attempt recorded and persisted, no token.
    [Fact]
    public async Task Handle_Should_Fail_And_Record_Attempt_On_Wrong_Password()
    {
        var user = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Failed);
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, user.FailedLoginAttempts);
        _auth.Verify(a => a.GenerateToken(It.IsAny<User>(), It.IsAny<Guid?>()), Times.Never);
        _users.Verify(r => r.Update(user), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-5.3] A deactivated account cannot log in. The deactivated state is disclosed only after a
    // correct password (so it can't be used to enumerate accounts); no token is issued.
    [Fact]
    public async Task Handle_Should_Reject_Inactive_User_After_Password_Check()
    {
        var user = LocalUser();
        user.Deactivate();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.VerifyPassword("STORED-HASH", "s3cret!!"), Times.Once);
        _auth.Verify(a => a.GenerateToken(It.IsAny<User>(), It.IsAny<Guid?>()), Times.Never);
    }

    // The stored hash used an outdated format: on a correct password it is upgraded in place and a
    // token is still issued.
    [Fact]
    public async Task Handle_Should_Upgrade_Hash_When_Outdated_And_Issue_Token()
    {
        var user = LocalUser();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.SuccessNeedsRehash);
        _auth.Setup(a => a.HashPassword("s3cret!!")).Returns("UPGRADED-HASH");
        _auth.Setup(a => a.GenerateToken(user, It.IsAny<Guid?>())).Returns(new LocalAuthToken("jwt-token", DateTime.UtcNow.AddHours(12)));
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("UPGRADED-HASH", user.PasswordHash);
        _auth.Verify(a => a.HashPassword("s3cret!!"), Times.Once);
    }

    // [AC-3.4] A locked-out account is rejected without checking the password.
    [Fact]
    public async Task Handle_Should_Reject_Locked_Out_User()
    {
        var user = LocalUser();
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++) user.RecordFailedLogin();
        Assert.True(user.IsLockedOut());
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // Unknown email → generic failure (no user existence leak).
    [Fact]
    public async Task Handle_Should_Fail_For_Unknown_Email()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // [AC-7.3] A Cloud (Auth0) account with no local password can never log in via local login.
    [Fact]
    public async Task Handle_Should_Reject_NonLocal_Account()
    {
        var cloudUser = new User("auth0|123", ClinicId, "doctor", "doc@clinic.com", "Dr House");
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(cloudUser);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
    /// <summary>
    /// [I5] A pending account is told an admin must activate it — not that it « a été désactivé ».
    ///
    /// <para>Both states are <c>!IsActive</c>, and the wording is the whole difference. « Ce compte a été
    /// désactivé » on an account created ninety seconds ago reads as a bug in the registration the person has
    /// just completed, and they have no way to ask anyone through the product. Disclosed only after the correct
    /// password, like the deactivated message it replaces.</para>
    /// </summary>
    [Fact]
    public async Task Handle_Should_Tell_A_Pending_Account_That_An_Admin_Must_Activate_It()
    {
        var user = User.CreateSelfRegistered(ClinicId, "secretary", "doc@clinic.com", "STORED-HASH", "Sam");
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("activé", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("désactivé", result.Error, StringComparison.OrdinalIgnoreCase);
        // No token is issued either way — the refusal is real, not cosmetic.
        _auth.Verify(a => a.GenerateRefreshToken(It.IsAny<User>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Never);
    }

    // [I5] …and an account switched off after use keeps the original wording. Two messages, two situations; if
    // both said the same thing the pending branch would be pointless.
    [Fact]
    public async Task Handle_Should_Keep_The_Deactivated_Wording_For_An_Account_That_Had_Logged_In()
    {
        var user = LocalUser();
        user.RecordSuccessfulLogin();
        user.Deactivate();
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);
        SaveSucceeds();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("désactivé", result.Error, StringComparison.OrdinalIgnoreCase);
    }

}
