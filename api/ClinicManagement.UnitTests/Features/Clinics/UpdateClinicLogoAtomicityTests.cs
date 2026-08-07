using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// FR-C3 atomicity for the clinic-logo write path. Because the logo storage key is deterministic
/// (<c>{clinicId}/logo</c>), cleanup must only remove a newly-stored blob the persisted clinic
/// won't reference — never a logo the DB still points to.
/// </summary>
public class UpdateClinicLogoAtomicityTests
{
    private const string UserId = "local|admin";
    private const string LogoKey = "LOGO-KEY";
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IFileStorage> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private UpdateClinicCommandHandler Handler() =>
        new(_clinics.Object, _users.Object, _context.Object, _fileStorage.Object, _uow.Object,
            NullLogger<UpdateClinicCommandHandler>.Instance);

    private Clinic ClinicFound()
    {
        var user = new User(UserId, ClinicId, "admin", "admin@clinic.com", "Admin");
        var clinic = new Clinic(ClinicId, "Cabinet Dentaire");
        _context.Setup(c => c.GetUserId()).Returns(UserId);
        _users.Setup(r => r.GetByAuth0SubAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        return clinic;
    }

    private void NewLogoUploadReturns(string key) =>
        _fileStorage
            .Setup(f => f.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(key);

    // Real PNG bytes with a .png name: the logo now goes through the shared upload profile, which keys on the
    // extension and refuses bytes that disagree with it. Three arbitrary bytes were fine when it validated nothing.
    private static UpdateClinicCommand WithLogo() => new()
    {
        Name = "Cabinet Dentaire",
        LogoFile = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        LogoFileName = "logo.png",
        LogoLength = 8,
    };

    private static UpdateClinicCommand WithoutLogo() => new() { Name = "Renamed Cabinet" };

    // [AC-3] First-time logo upload then a failed save → the just-stored blob is removed.
    [Fact]
    public async Task Handle_Should_Delete_New_Logo_When_Save_Fails()
    {
        ClinicFound(); // LogoUrl starts null → the uploaded key becomes an orphan on failure
        NewLogoUploadReturns(LogoKey);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db save failed"));

        var result = await Handler().Handle(WithLogo(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _fileStorage.Verify(f => f.DeleteAsync(LogoKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-3] Save fails but no new logo was uploaded → nothing is deleted (guard against
    // removing a logo the DB still references).
    [Fact]
    public async Task Handle_Should_Not_Delete_When_No_Logo_Uploaded()
    {
        ClinicFound();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db save failed"));

        var result = await Handler().Handle(WithoutLogo(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _fileStorage.Verify(
            f => f.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-3] Successful update keeps the logo — no cleanup delete, key persisted on the clinic.
    [Fact]
    public async Task Handle_Should_Keep_Logo_On_Success()
    {
        var clinic = ClinicFound();
        NewLogoUploadReturns(LogoKey);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(WithLogo(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LogoKey, clinic.LogoUrl);
        _fileStorage.Verify(
            f => f.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
