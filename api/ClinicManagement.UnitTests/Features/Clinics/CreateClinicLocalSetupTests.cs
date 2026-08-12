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
/// Story 2: Local (offline) first-run — create clinic + first admin from email+password.
/// Only the Local branch of <see cref="CreateClinicCommandHandler"/> is exercised here.
/// </summary>
public class CreateClinicLocalSetupTests
{
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IAuth0ManagementService> _auth0 = new();
    private readonly Mock<IFileStorage> _fileStorage = new();
    private readonly Mock<ILocalAuthService> _localAuth = new();
    private readonly Mock<IClinicCatalogSeeder> _catalogSeeder = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPolicy> _subscriptionPolicy = new();
    private readonly Mock<IMessagingAllowanceRepository> _messagingAllowances = new();
    private readonly Mock<IMessagingAllowancePolicy> _messagingPolicy = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private User? _capturedUser;
    private Clinic? _capturedClinic;

    private CreateClinicCommandHandler Handler() => new(
        _clinics.Object, _procedureTypes.Object, _users.Object, _doctors.Object, _clinicContext.Object,
        _auth0.Object, _fileStorage.Object, _localAuth.Object, _catalogSeeder.Object, _subscriptions.Object,
        _subscriptionPolicy.Object, _messagingAllowances.Object, _messagingPolicy.Object, _uow.Object,
        NullLogger<CreateClinicCommandHandler>.Instance);

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
        // clinic-subscription: the Local branch is a SelfHostedLan first run, so the policy answers « not
        // enforced » and the cabinet gets an open-ended entitlement. TrialDays is still stubbed because the
        // helper reads it either way.
        _subscriptionPolicy.SetupGet(p => p.RequiresSubscription).Returns(false);
        _subscriptionPolicy.SetupGet(p => p.TrialDays).Returns(30);
        // vendor-whatsapp-messaging-quota: the forfait is staged whatever the deployment kind (FR-3), so this is read
        // on a SelfHostedLan first run too — where nothing meters against it.
        _messagingPolicy.SetupGet(p => p.DefaultMessagesPerMonth).Returns(200);
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

    // fix-single-dentist-identity #15: when the admin is also the practitioner, a linked Doctor is created.
    [Fact]
    public async Task Setup_With_Practitioner_Creates_Linked_Doctor()
    {
        FreshInstall();
        var command = SetupCommand();
        command.DoctorInfo = new DoctorPersonalInfoDto { FirstName = "Jane", LastName = "Doe", Specialty = "Dentiste" };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _doctors.Verify(r => r.AddAsync(It.IsAny<Doctor>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // #15: a practitioner setup missing the first or last name must NOT persist a nameless Doctor — and
    // fails before creating the clinic/admin.
    [Theory]
    [InlineData("", "Doe")]
    [InlineData("Jane", "")]
    public async Task Setup_With_Practitioner_Missing_Name_Is_Rejected(string firstName, string lastName)
    {
        FreshInstall();
        var command = SetupCommand();
        command.DoctorInfo = new DoctorPersonalInfoDto { FirstName = firstName, LastName = lastName, Specialty = "Dentiste" };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _doctors.Verify(r => r.AddAsync(It.IsAny<Doctor>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
