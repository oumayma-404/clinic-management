using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Maintenance;

public class AdminPasswordRecoveryServiceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AdminPasswordRecoveryService Service() => new(_users.Object, _auth.Object, _uow.Object);

    private static User LocalAdmin(string email = "admin@clinic.com") =>
        User.CreateLocalUser(ClinicId, "admin", email, "OLD-HASH", "Clinic Admin");

    private static User Local(string role, string email) =>
        User.CreateLocalUser(ClinicId, role, email, "OLD-HASH", $"{role} name");

    private void ExpectTempPasswordGeneration()
    {
        _auth.Setup(a => a.GenerateTemporaryPassword()).Returns("Temp1234abcd");
        _auth.Setup(a => a.HashPassword("Temp1234abcd")).Returns("NEW-HASH");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    // [FR-B6] Reset by email → temp password returned, forced-change set, persisted.
    [Fact]
    public async Task Reset_By_Email_Should_Reset_And_Force_Change()
    {
        var admin = LocalAdmin();
        _users.Setup(r => r.GetByEmailAsync("admin@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        ExpectTempPasswordGeneration();

        var result = await Service().ResetAdminPasswordAsync("admin@clinic.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Temp1234abcd", result.Value!.TemporaryPassword);
        Assert.Equal("admin@clinic.com", result.Value.AdminEmail);
        Assert.Equal("NEW-HASH", admin.PasswordHash);
        Assert.True(admin.MustChangePassword);
        _users.Verify(r => r.Update(admin), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [FR-B6] Email trimmed before lookup.
    [Fact]
    public async Task Reset_By_Email_Should_Trim_Input()
    {
        var admin = LocalAdmin();
        _users.Setup(r => r.GetByEmailAsync("admin@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        ExpectTempPasswordGeneration();

        var result = await Service().ResetAdminPasswordAsync("  admin@clinic.com  ", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // [FR-B6] Unknown email → clear error, nothing persisted.
    [Fact]
    public async Task Reset_By_Email_Should_Fail_When_Account_Not_Found()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await Service().ResetAdminPasswordAsync("nobody@clinic.com", CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B6] Named account exists but is not an admin → refused (utility only recovers admins).
    [Fact]
    public async Task Reset_By_Email_Should_Fail_When_Account_Not_Admin()
    {
        var doctor = Local("doctor", "doctor@clinic.com");
        _users.Setup(r => r.GetByEmailAsync("doctor@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(doctor);

        var result = await Service().ResetAdminPasswordAsync("doctor@clinic.com", CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.GenerateTemporaryPassword(), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B6] No email + exactly one local admin → the sole admin is reset.
    [Fact]
    public async Task Reset_Without_Email_Should_Reset_Sole_Admin()
    {
        var admin = LocalAdmin();
        var doctor = Local("doctor", "doctor@clinic.com");
        _users.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { admin, doctor });
        ExpectTempPasswordGeneration();

        var result = await Service().ResetAdminPasswordAsync(null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(admin.Id, result.Value!.AdminId);
        Assert.True(admin.MustChangePassword);
        _users.Verify(r => r.Update(admin), Times.Once);
    }

    // [FR-B6] No email + no admin exists → clear error.
    [Fact]
    public async Task Reset_Without_Email_Should_Fail_When_No_Admin()
    {
        var doctor = Local("doctor", "doctor@clinic.com");
        _users.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { doctor });

        var result = await Service().ResetAdminPasswordAsync(null, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B6] No email + more than one admin → ambiguous, operator must specify the email.
    [Fact]
    public async Task Reset_Without_Email_Should_Fail_When_Multiple_Admins()
    {
        var admin1 = LocalAdmin("admin1@clinic.com");
        var admin2 = LocalAdmin("admin2@clinic.com");
        _users.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { admin1, admin2 });

        var result = await Service().ResetAdminPasswordAsync(null, CancellationToken.None);

        Assert.True(result.IsFailure);
        _auth.Verify(a => a.GenerateTemporaryPassword(), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B6] A locked-out admin is unlocked by the reset (SetPassword clears lockout state).
    [Fact]
    public async Task Reset_Should_Clear_Lockout_State()
    {
        var admin = LocalAdmin();
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++)
        {
            admin.RecordFailedLogin();
        }
        Assert.True(admin.IsLockedOut());

        _users.Setup(r => r.GetByEmailAsync("admin@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        ExpectTempPasswordGeneration();

        var result = await Service().ResetAdminPasswordAsync("admin@clinic.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(admin.IsLockedOut());
        Assert.Equal(0, admin.FailedLoginAttempts);
    }
}
