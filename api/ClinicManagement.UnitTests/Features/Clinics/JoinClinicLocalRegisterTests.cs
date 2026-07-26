using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// Story 4: Local (offline) staff self-registration — join a clinic by code with
/// email+password. Exercises the Local branch of <see cref="JoinClinicCommandHandler"/>.
/// </summary>
public class JoinClinicLocalRegisterTests
{
    private const string Code = "ABC123";

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IAuth0ManagementService> _auth0 = new();
    private readonly Mock<ILocalAuthService> _localAuth = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private User? _capturedUser;
    private Doctor? _capturedDoctor;

    private JoinClinicCommandHandler Handler() => new(
        _clinics.Object, _users.Object, _doctors.Object, _clinicContext.Object,
        _auth0.Object, _localAuth.Object, _uow.Object,
        NullLogger<JoinClinicCommandHandler>.Instance);

    private void ValidClinicAndFreshEmail()
    {
        var clinic = new Clinic(Guid.NewGuid(), "Test Clinic", null, null, null, Code);
        _clinics.Setup(r => r.GetByCodeAsync(Code, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => _capturedUser = u);
        _doctors.Setup(r => r.AddAsync(It.IsAny<Doctor>(), It.IsAny<CancellationToken>()))
            .Callback<Doctor, CancellationToken>((d, _) => _capturedDoctor = d);
        _localAuth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("HASHED");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static JoinClinicCommand SecretaryCommand() => new()
    {
        Code = Code,
        Role = "secretary",
        Email = "sec@clinic.com",
        Password = "s3cret!!",
        FullName = "Sam Secretary",
    };

    // [AC-4.2][FR-B4] Valid code + new email → active local account created.
    [Fact]
    public async Task Register_Should_Create_Local_Account()
    {
        ValidClinicAndFreshEmail();

        var result = await Handler().Handle(SecretaryCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_capturedUser);
        Assert.Equal("secretary", _capturedUser!.Role);
        Assert.True(_capturedUser.IsLocalAccount());
        Assert.True(_capturedUser.IsActive);
        Assert.Equal("HASHED", _capturedUser.PasswordHash);
        Assert.StartsWith("local|", _capturedUser.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-4.4] admin is never self-assignable.
    [Fact]
    public async Task Register_Should_Reject_Admin_Role()
    {
        ValidClinicAndFreshEmail();
        var command = SecretaryCommand();
        command.Role = "admin";

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-4.2] Invalid clinic code → rejected.
    [Fact]
    public async Task Register_Should_Reject_Invalid_Code()
    {
        _clinics.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);
        _localAuth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("HASHED");

        var result = await Handler().Handle(SecretaryCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-4.3] Duplicate email in this install → rejected.
    [Fact]
    public async Task Register_Should_Reject_Duplicate_Email()
    {
        ValidClinicAndFreshEmail();
        var existing = User.CreateLocalUser(Guid.NewGuid(), "secretary", "sec@clinic.com", "H", "Existing");
        _users.Setup(r => r.GetByEmailAsync("sec@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await Handler().Handle(SecretaryCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-B2] Password policy: minimum 8 characters.
    [Fact]
    public async Task Register_Should_Reject_Short_Password()
    {
        ValidClinicAndFreshEmail();
        var command = SecretaryCommand();
        command.Password = "short";

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-4.1] Doctor self-registration creates a linked Doctor record.
    [Fact]
    public async Task Register_Doctor_Should_Create_Linked_Doctor()
    {
        ValidClinicAndFreshEmail();
        var command = SecretaryCommand();
        command.Email = "doc@clinic.com";
        command.Role = "doctor";
        command.DoctorInfo = new DoctorPersonalInfoDto
        {
            FirstName = "Jane",
            LastName = "House",
            Specialty = "Dentist",
            Phone = "+216 12 345 678",
        };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_capturedDoctor);
        Assert.Equal("doctor", _capturedUser!.Role);
        Assert.Equal(_capturedUser.Id, _capturedDoctor!.UserId);
    }

    // [AC-4.1] Doctor without required doctor info → rejected.
    [Fact]
    public async Task Register_Doctor_Should_Require_Doctor_Info()
    {
        ValidClinicAndFreshEmail();
        var command = SecretaryCommand();
        command.Role = "doctor";
        command.DoctorInfo = null;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
