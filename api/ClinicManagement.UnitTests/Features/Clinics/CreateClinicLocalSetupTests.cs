using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// Story 2: Local (offline) first-run — create clinic + first admin from email+password.
/// Only the Local branch of <see cref="CreateClinicCommandHandler"/> is exercised here.
/// </summary>
public class CreateClinicLocalSetupTests
{
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IAuth0ManagementService> _auth0 = new();
    private readonly Mock<IFileStorage> _fileStorage = new();
    private readonly Mock<ILocalAuthService> _localAuth = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private User? _capturedUser;
    private Clinic? _capturedClinic;

    private CreateClinicCommandHandler Handler() => new(
        _clinics.Object, _users.Object, _doctors.Object, _clinicContext.Object,
        _auth0.Object, _fileStorage.Object, _localAuth.Object, _uow.Object);

    private void FreshInstall()
    {
        _users.Setup(r => r.AnyUserExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _clinics.Setup(r => r.CodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _clinics.Setup(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()))
            .Callback<Clinic, CancellationToken>((c, _) => _capturedClinic = c)
            .ReturnsAsync((Clinic c, CancellationToken _) => c);
        _users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => _capturedUser = u);
        _localAuth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("HASHED");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static CreateClinicCommand SetupCommand() => new()
    {
        Name = "Cabinet Dentaire",
        Email = "Admin@Clinic.com",
        Password = "s3cret!!",
        FullName = "Dr Admin",
        Role = "admin",
        GenerateCode = true
    };

    // [AC-1.2][FR-B3] Fresh install → clinic + admin created; password hashed; code generated.
    [Fact]
    public async Task Setup_Should_Create_Clinic_And_Admin()
    {
        FreshInstall();

        var result = await Handler().Handle(SetupCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_capturedClinic);
        Assert.False(string.IsNullOrWhiteSpace(_capturedClinic!.Code)); // code generated for staff self-registration
        Assert.NotNull(_capturedUser);
        Assert.Equal("admin", _capturedUser!.Role);
        Assert.True(_capturedUser.IsAdmin());
        Assert.True(_capturedUser.IsLocalAccount());
        Assert.Equal("HASHED", _capturedUser.PasswordHash);
        Assert.Equal("admin@clinic.com", _capturedUser.Email); // normalized
        Assert.StartsWith("local|", _capturedUser.Id);
        _localAuth.Verify(a => a.HashPassword("s3cret!!"), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // First-run must not touch Auth0 (no-op in Local mode anyway).
        _auth0.Verify(a => a.UpdateUserMetadataAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-1.2a] Setup is closed once an admin already exists.
    [Fact]
    public async Task Setup_Should_Fail_When_User_Already_Exists()
    {
        FreshInstall();
        _users.Setup(r => r.AnyUserExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(SetupCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B2] Password policy: minimum 8 characters, enforced at the API.
    [Fact]
    public async Task Setup_Should_Reject_Short_Password()
    {
        FreshInstall();
        var command = SetupCommand();
        command.Password = "short";

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Dr Admin")]   // blank email
    [InlineData("a@b.com", "")]    // blank full name
    public async Task Setup_Should_Reject_Missing_Required_Fields(string email, string fullName)
    {
        FreshInstall();
        var command = SetupCommand();
        command.Email = email;
        command.FullName = fullName;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
