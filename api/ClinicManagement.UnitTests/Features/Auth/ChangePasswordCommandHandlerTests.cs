using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

public class ChangePasswordCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ChangePasswordCommandHandler Handler() => new(_users.Object, _context.Object, _auth.Object, _uow.Object);

    private User Caller(bool mustChange = false)
    {
        var user = User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "OLD-HASH", "Dr House", mustChange);
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    // [AC-5.2] Correct current password + valid new password → changed, forced-change cleared.
    [Fact]
    public async Task Handle_Should_Change_Password_And_Clear_Force_Flag()
    {
        var user = Caller(mustChange: true);
        _auth.Setup(a => a.VerifyPassword("OLD-HASH", "current-pass")).Returns(PasswordVerificationOutcome.Success);
        _auth.Setup(a => a.HashPassword("new-strong-pass")).Returns("NEW-HASH");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(
            new ChangePasswordCommand { CurrentPassword = "current-pass", NewPassword = "new-strong-pass" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-HASH", user.PasswordHash);
        Assert.False(user.MustChangePassword);
        _users.Verify(r => r.Update(user), Times.Once);
    }

    // Wrong current password → rejected, nothing persisted.
    [Fact]
    public async Task Handle_Should_Reject_Wrong_Current_Password()
    {
        Caller();
        _auth.Setup(a => a.VerifyPassword("OLD-HASH", "wrong")).Returns(PasswordVerificationOutcome.Failed);

        var result = await Handler().Handle(
            new ChangePasswordCommand { CurrentPassword = "wrong", NewPassword = "new-strong-pass" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B2] New password below the minimum length is rejected before any verification.
    [Fact]
    public async Task Handle_Should_Reject_Short_New_Password()
    {
        Caller();

        var result = await Handler().Handle(
            new ChangePasswordCommand { CurrentPassword = "current-pass", NewPassword = "short" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
