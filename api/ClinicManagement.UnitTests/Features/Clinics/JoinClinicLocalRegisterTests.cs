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

    /// <summary>
    /// [AC-4.2][FR-B4][I5] Valid code + new email → a local account is created… <b>inactive</b>.
    ///
    /// <para>This assertion was inverted by <c>adoption-qa-i-access-control-and-audit</c> (I5), and the inversion
    /// is the feature. The account used to be created live, so the only thing between a stranger and every
    /// patient record in the practice was a <b>6-character</b> code over a 36-symbol alphabet — displayed on a
    /// settings screen, and known to everyone who had ever worked there, including the ones who left. Rotating
    /// the code (which the product supports) does not retract an account already minted with it. The code now
    /// decides which clinic you are asking to join; an admin decides whether you join it.</para>
    /// </summary>
    [Fact]
    public async Task Register_Should_Create_A_Pending_Local_Account()
    {
        ValidClinicAndFreshEmail();

        var result = await Handler().Handle(SecretaryCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_capturedUser);
        Assert.Equal("secretary", _capturedUser!.Role);
        Assert.True(_capturedUser.IsLocalAccount());
        Assert.Equal("HASHED", _capturedUser.PasswordHash);
        Assert.StartsWith("local|", _capturedUser.Id);

        // The whole point of I5: it exists, and it cannot log in yet.
        Assert.False(_capturedUser.IsActive);
        Assert.True(_capturedUser.IsPendingActivation);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [I5] A doctor self-registration is pending too — the role chosen at registration does not buy an exemption.
    [Fact]
    public async Task Register_Doctor_Is_Also_Pending()
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
        };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(_capturedUser!.IsActive);
        Assert.True(_capturedUser.IsPendingActivation);
    }

    /// <summary>
    /// [I5][Edge case] A first-run <c>setup</c> admin is <b>never</b> pending.
    ///
    /// <para>The spec's own critical edge case: « a clinic whose only account is the owner must not lock itself
    /// out ». A pending first admin would have nobody able to approve them — the clinic would be locked out of
    /// itself before it had a second account. Asserted on the factory rather than through
    /// <c>CreateClinicCommand</c> because the factory <i>is</i> the difference between the two paths, and it is
    /// what a future caller could get wrong.</para>
    /// </summary>
    [Fact]
    public void The_First_Run_Setup_Admin_Is_Never_Pending()
    {
        var admin = User.CreateLocalUser(Guid.NewGuid(), "admin", "owner@clinic.com", "HASH", "Owner");

        Assert.True(admin.IsActive);
        Assert.False(admin.IsPendingActivation);
    }

    // [I5] The two factories differ in exactly one respect. Pinned side by side because they are otherwise
    // identical, and a future caller reaching for the wrong one is the whole risk the named factory exists to
    // remove.
    [Fact]
    public void The_Two_Local_Factories_Differ_Only_In_Their_Active_State()
    {
        var clinicId = Guid.NewGuid();
        var live = User.CreateLocalUser(clinicId, "secretary", "a@clinic.com", "HASH", "Sam");
        var pending = User.CreateSelfRegistered(clinicId, "secretary", "b@clinic.com", "HASH", "Sam");

        Assert.True(live.IsActive);
        Assert.False(pending.IsActive);

        // Everything else is the same account shape.
        Assert.Equal(live.Role, pending.Role);
        Assert.Equal(live.ClinicId, pending.ClinicId);
        Assert.Equal(live.PasswordHash, pending.PasswordHash);
        Assert.True(pending.IsLocalAccount());
        Assert.StartsWith("local|", pending.Id);
    }

    // [I5] « Never let in » and « switched off after use » are both !IsActive, and the screen says different
    // things about them — so the distinction has to survive a login.
    [Fact]
    public void A_Deactivated_Account_That_Has_Logged_In_Is_Not_Pending()
    {
        var user = User.CreateSelfRegistered(Guid.NewGuid(), "secretary", "sec@clinic.com", "HASH", "Sam");
        Assert.True(user.IsPendingActivation);

        user.Activate();
        user.RecordSuccessfulLogin();
        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.False(user.IsPendingActivation);
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
