using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

public class RegenerateClinicCodeCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private RegenerateClinicCodeCommandHandler Handler() => new(_clinics.Object, _users.Object, _context.Object, _uow.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    // [AC-4.5] Admin regenerates → a fresh 6-char code is minted and persisted.
    [Fact]
    public async Task Handle_Should_Regenerate_Code_For_Admin()
    {
        var admin = Local("admin");
        var clinic = new Clinic(ClinicId, "Clinic", code: "OLDCOD");
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        _clinics.Setup(r => r.CodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(new RegenerateClinicCodeCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Code);
        Assert.Equal(6, result.Value.Code!.Length);
        Assert.Equal(result.Value.Code, clinic.Code);
        _clinics.Verify(r => r.UpdateAsync(clinic, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-5.4] A non-admin cannot regenerate the code.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        var doctor = Local("doctor");
        _context.Setup(c => c.GetUserId()).Returns(doctor.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);

        var result = await Handler().Handle(new RegenerateClinicCodeCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _clinics.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
