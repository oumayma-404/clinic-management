using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

public class ResetUserPasswordCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ResetUserPasswordCommandHandler Handler() =>
        new(_users.Object, _context.Object, _auth.Object, _uow.Object,
            NullLogger<ResetUserPasswordCommandHandler>.Instance);

    private static User Local(string role, Guid clinicId) =>
        User.CreateLocalUser(clinicId, role, $"{role}@clinic.com", "OLD-HASH", $"{role} name");

    private void AsAdmin(User admin)
    {
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
    }

    // [AC-5.2] Admin reset → temp password returned, forced-change set, persisted.
    [Fact]
    public async Task Handle_Should_Reset_And_Force_Change()
    {
        var admin = Local("admin", ClinicId);
        var target = Local("doctor", ClinicId);
        AsAdmin(admin);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        _auth.Setup(a => a.GenerateTemporaryPassword()).Returns("Temp1234abcd");
        _auth.Setup(a => a.HashPassword("Temp1234abcd")).Returns("NEW-HASH");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(new ResetUserPasswordCommand { TargetUserId = target.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Temp1234abcd", result.Value!.TemporaryPassword);
        Assert.Equal("NEW-HASH", target.PasswordHash);
        Assert.True(target.MustChangePassword);
        _users.Verify(r => r.Update(target), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-5.4] A non-admin cannot reset passwords.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        var secretary = Local("secretary", ClinicId);
        AsAdmin(secretary); // resolves as caller, but role is not admin

        var result = await Handler().Handle(new ResetUserPasswordCommand { TargetUserId = "local|x" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A target in another clinic is not found (tenant isolation).
    [Fact]
    public async Task Handle_Should_Not_Reset_User_In_Another_Clinic()
    {
        var admin = Local("admin", ClinicId);
        var foreign = Local("doctor", OtherClinicId);
        AsAdmin(admin);
        _users.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(new ResetUserPasswordCommand { TargetUserId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A Cloud (non-local) account has no local password to reset.
    [Fact]
    public async Task Handle_Should_Reject_Non_Local_Account()
    {
        var admin = Local("admin", ClinicId);
        var cloudUser = new User("auth0|1", ClinicId, "doctor", "cloud@clinic.com", "Cloud User");
        AsAdmin(admin);
        _users.Setup(r => r.GetByIdAsync(cloudUser.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cloudUser);

        var result = await Handler().Handle(new ResetUserPasswordCommand { TargetUserId = cloudUser.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.GenerateTemporaryPassword(), Times.Never);
    }
}
