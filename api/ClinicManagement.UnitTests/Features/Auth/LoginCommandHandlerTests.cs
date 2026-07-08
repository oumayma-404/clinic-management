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

    private LoginCommandHandler Handler() => new(_users.Object, _auth.Object, _uow.Object);

    private static User LocalUser(bool mustChangePassword = false) =>
        User.CreateLocalUser(ClinicId, "doctor", "Doc@Clinic.com", "STORED-HASH", "Dr House", mustChangePassword);

    private static LoginCommand Command() => new() { Email = "doc@clinic.com", Password = "s3cret!!" };

    private void SaveSucceeds() =>
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

    // [AC-3.1][AC-3.3] Valid credentials → JWT issued, login recorded, MustChangePassword surfaced.
    [Fact]
    public async Task Handle_Should_Return_Token_On_Valid_Credentials()
    {
        var user = LocalUser(mustChangePassword: true);
        _users.Setup(r => r.GetByEmailAsync("doc@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.VerifyPassword("STORED-HASH", "s3cret!!")).Returns(PasswordVerificationOutcome.Success);
        _auth.Setup(a => a.GenerateToken(user)).Returns(new LocalAuthToken("jwt-token", DateTime.UtcNow.AddHours(12)));
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
        _auth.Verify(a => a.GenerateToken(It.IsAny<User>()), Times.Never);
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
        _auth.Verify(a => a.GenerateToken(It.IsAny<User>()), Times.Never);
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
        _auth.Setup(a => a.GenerateToken(user)).Returns(new LocalAuthToken("jwt-token", DateTime.UtcNow.AddHours(12)));
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
}
