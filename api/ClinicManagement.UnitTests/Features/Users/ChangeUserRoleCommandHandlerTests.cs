using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

/// <summary>
/// [AC-P2.23–2.27] Changing a clinic user's role. No role could ever be changed before, so all five ACs are new
/// behaviour: the change itself, validation against the closed set (A-11), email/full name surviving it (A-11),
/// the only-active-admin self-lockout guard, and the <c>TokenVersion</c> bump without which the old role stays
/// live for the token's whole remaining lifetime.
/// </summary>
public class ChangeUserRoleCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ChangeUserRoleCommandHandler Handler() =>
        new(_users.Object, _context.Object, _uow.Object, NullLogger<ChangeUserRoleCommandHandler>.Instance);

    private static User Local(string role, Guid clinicId) =>
        User.CreateLocalUser(clinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void AsAdmin(User admin, params User[] clinicUsers)
    {
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _users.Setup(r => r.GetByClinicIdAsync(admin.ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((clinicUsers.Length > 0 ? clinicUsers : new[] { admin }).AsPage());
    }

    private Task<Application.Common.Models.Result<Application.DTOs.ClinicUserDto>> ChangeAsync(
        string targetId, string role) =>
        Handler().Handle(new ChangeUserRoleCommand { TargetUserId = targetId, Role = role }, CancellationToken.None);

    // [AC-P2.23] An admin moves a secretary to doctor.
    [Fact]
    public async Task Handle_Changes_The_Role()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var target = Local(User.RoleSecretary, ClinicId);
        AsAdmin(admin, admin, target);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await ChangeAsync(target.Id, User.RoleDoctor);

        Assert.True(result.IsSuccess);
        Assert.Equal(User.RoleDoctor, target.Role);
        Assert.Equal(User.RoleDoctor, result.Value!.Role);
        _users.Verify(r => r.Update(target), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-P2.25 / A-11] The old `User.Update(role)` defaulted email and fullName to null and assigned them, so
    // the natural one-argument call silently wiped both. A role change must touch the role and nothing else.
    [Fact]
    public async Task Handle_Keeps_Email_And_Full_Name()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var target = Local(User.RoleSecretary, ClinicId);
        var email = target.Email;
        var fullName = target.FullName;
        AsAdmin(admin, admin, target);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await ChangeAsync(target.Id, User.RoleDoctor);

        Assert.True(result.IsSuccess);
        Assert.Equal(email, target.Email);
        Assert.Equal(fullName, target.FullName);
        Assert.NotNull(target.Email);
        Assert.NotNull(target.FullName);
    }

    // [AC-P2.27] The JWT is stateless and carries the role, so the change only takes effect on the target's next
    // request if their existing tokens are revoked.
    [Fact]
    public async Task Handle_Bumps_TokenVersion()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var target = Local(User.RoleSecretary, ClinicId);
        var before = target.TokenVersion;
        AsAdmin(admin, admin, target);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        await ChangeAsync(target.Id, User.RoleDoctor);

        Assert.Equal(before + 1, target.TokenVersion);
    }

    // Re-selecting the role the user already holds is a no-op — bumping TokenVersion there would log a user out
    // for a change that never happened.
    [Fact]
    public async Task Handle_Is_A_No_Op_When_The_Role_Is_Unchanged()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var target = Local(User.RoleDoctor, ClinicId);
        var before = target.TokenVersion;
        AsAdmin(admin, admin, target);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await ChangeAsync(target.Id, User.RoleDoctor);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, target.TokenVersion);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-P2.24 / A-11] Any string was accepted before, including empty — and an account whose role matches no
    // policy silently loses every surface.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner")]
    [InlineData("Administrateur")]
    [InlineData("admin ; drop")]
    public async Task Handle_Rejects_A_Role_Outside_The_Closed_Set(string role)
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var target = Local(User.RoleSecretary, ClinicId);
        AsAdmin(admin, admin, target);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await ChangeAsync(target.Id, role);

        Assert.True(result.IsFailure);
        Assert.Equal(User.RoleSecretary, target.Role);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The closed set is matched case-insensitively and stored in its canonical spelling, so a client sending
    // « Doctor » cannot create a second, policy-invisible spelling of a real role.
    [Fact]
    public async Task Handle_Normalizes_The_Casing()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var target = Local(User.RoleSecretary, ClinicId);
        AsAdmin(admin, admin, target);
        _users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await ChangeAsync(target.Id, "  DoCtOr ");

        Assert.True(result.IsSuccess);
        Assert.Equal(User.RoleDoctor, target.Role);
    }

    // [AC-P2.26] The only active admin cannot demote themselves — nobody would be left able to manage users,
    // and the offline recovery utility resets a password, it does not grant a role.
    [Fact]
    public async Task Handle_Refuses_Self_Demotion_Of_The_Only_Active_Admin()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var secretary = Local(User.RoleSecretary, ClinicId);
        AsAdmin(admin, admin, secretary);
        _users.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var result = await ChangeAsync(admin.Id, User.RoleDoctor);

        Assert.True(result.IsFailure);
        Assert.Contains("seul administrateur actif", result.Error ?? string.Empty);
        Assert.Equal(User.RoleAdmin, admin.Role);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The guard counts only ACTIVE admins: a deactivated one cannot log in, so it is not a way out of a lockout.
    [Fact]
    public async Task Handle_Refuses_Self_Demotion_When_The_Other_Admin_Is_Deactivated()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var dormantAdmin = Local(User.RoleAdmin, ClinicId);
        dormantAdmin.Deactivate();
        AsAdmin(admin, admin, dormantAdmin);
        _users.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var result = await ChangeAsync(admin.Id, User.RoleSecretary);

        Assert.True(result.IsFailure);
        Assert.Equal(User.RoleAdmin, admin.Role);
    }

    // With a second active admin in place, standing down is legitimate — the guard is about the clinic keeping
    // an administrator, not about forbidding self-demotion.
    [Fact]
    public async Task Handle_Allows_Self_Demotion_When_Another_Active_Admin_Remains()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var coAdmin = Local(User.RoleAdmin, ClinicId);
        AsAdmin(admin, admin, coAdmin);
        _users.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var result = await ChangeAsync(admin.Id, User.RoleDoctor);

        Assert.True(result.IsSuccess);
        Assert.Equal(User.RoleDoctor, admin.Role);
    }

    // Promoting oneself is not a lockout risk and must not be caught by the guard.
    [Fact]
    public async Task Handle_Allows_An_Admin_To_Reselect_Admin_For_Themselves()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        AsAdmin(admin, admin);
        _users.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var result = await ChangeAsync(admin.Id, User.RoleAdmin);

        Assert.True(result.IsSuccess);
        Assert.Equal(User.RoleAdmin, admin.Role);
    }

    // A non-admin caller is refused even though the controller policy would already have stopped them — the DB
    // role is the authoritative check, not the JWT claim.
    [Fact]
    public async Task Handle_Rejects_A_Non_Admin_Caller()
    {
        var secretary = Local(User.RoleSecretary, ClinicId);
        AsAdmin(secretary, secretary);

        var result = await ChangeAsync("local|whoever", User.RoleAdmin);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Tenant isolation: another clinic's user reads as not found and is not touched.
    [Fact]
    public async Task Handle_Does_Not_Touch_A_User_In_Another_Clinic()
    {
        var admin = Local(User.RoleAdmin, ClinicId);
        var foreign = Local(User.RoleSecretary, OtherClinicId);
        AsAdmin(admin, admin);
        _users.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await ChangeAsync(foreign.Id, User.RoleAdmin);

        Assert.True(result.IsFailure);
        Assert.Equal(User.RoleSecretary, foreign.Role);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
