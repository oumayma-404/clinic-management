using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

public class SetUserActiveCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private SetUserActiveCommandHandler Handler() => new(_users.Object, _context.Object, _uow.Object);

    private static User Local(string role, Guid clinicId) =>
        User.CreateLocalUser(clinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void AsAdmin(User admin)
    {
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    // [AC-5.3] Admin deactivates a user.
    [Fact]
    public async Task Handle_Should_Deactivate_User()
    {
        var admin = Local("admin", ClinicId);
        var target = Local("doctor", ClinicId);
        AsAdmin(admin);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await Handler().Handle(
            new SetUserActiveCommand { TargetUserId = target.Id, IsActive = false }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
        _users.Verify(r => r.Update(target), Times.Once);
    }

    // [AC-5.3] Admin reactivates a user.
    [Fact]
    public async Task Handle_Should_Reactivate_User()
    {
        var admin = Local("admin", ClinicId);
        var target = Local("doctor", ClinicId);
        target.Deactivate();
        AsAdmin(admin);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await Handler().Handle(
            new SetUserActiveCommand { TargetUserId = target.Id, IsActive = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(target.IsActive);
    }

    // An admin cannot deactivate their own account (would be an unrecoverable lockout).
    [Fact]
    public async Task Handle_Should_Reject_Self_Deactivation()
    {
        var admin = Local("admin", ClinicId);
        AsAdmin(admin);

        var result = await Handler().Handle(
            new SetUserActiveCommand { TargetUserId = admin.Id, IsActive = false }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-5.4] A non-admin cannot change a user's status.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        var secretary = Local("secretary", ClinicId);
        AsAdmin(secretary);

        var result = await Handler().Handle(
            new SetUserActiveCommand { TargetUserId = "local|x", IsActive = false }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A target in another clinic is not found (tenant isolation).
    [Fact]
    public async Task Handle_Should_Not_Touch_User_In_Another_Clinic()
    {
        var admin = Local("admin", ClinicId);
        var foreign = Local("doctor", OtherClinicId);
        AsAdmin(admin);
        _users.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(
            new SetUserActiveCommand { TargetUserId = foreign.Id, IsActive = false }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(foreign.IsActive);
    }
}
