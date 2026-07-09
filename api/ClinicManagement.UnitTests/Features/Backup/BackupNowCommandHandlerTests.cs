using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// The admin-only one-click backup command (US-8 / AC-8.1–8.3). Verifies the admin guard, that the
/// backup service is invoked for an admin, and that a service failure surfaces as a clear
/// <c>Result.Failure</c> (never a silent success).
/// </summary>
public class BackupNowCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IBackupService> _backup = new();

    private BackupNowCommandHandler Handler() => new(_users.Object, _context.Object, _backup.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void AsCaller(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    // [AC-8.1] An admin can run a backup; the service is invoked and the result surfaced.
    [Fact]
    public async Task Handle_Should_Run_Backup_For_Admin()
    {
        var admin = Local("admin");
        AsCaller(admin);
        var dto = new BackupResultDto { DestinationPath = @"D:\backups\clinic-backup-x", SizeBytes = 1234, TimestampUtc = DateTime.UtcNow };
        _backup.Setup(b => b.CreateBackupAsync(@"D:\backups", It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await Handler().Handle(new BackupNowCommand { DestinationFolder = @"D:\backups" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"D:\backups\clinic-backup-x", result.Value!.DestinationPath);
        _backup.Verify(b => b.CreateBackupAsync(@"D:\backups", It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-8.1] A non-admin cannot run a backup — the service is never invoked.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        var secretary = Local("secretary");
        AsCaller(secretary);

        var result = await Handler().Handle(new BackupNowCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _backup.Verify(b => b.CreateBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-8.2/8.3] A backup failure is surfaced as a clear failure, not a silent success.
    [Fact]
    public async Task Handle_Should_Surface_Backup_Failure()
    {
        var admin = Local("admin");
        AsCaller(admin);
        _backup.Setup(b => b.CreateBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Espace disque insuffisant"));

        var result = await Handler().Handle(new BackupNowCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Espace disque insuffisant", result.Error);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Caller_Not_Found()
    {
        _context.Setup(c => c.GetUserId()).Returns("local|missing");
        _users.Setup(r => r.GetByAuth0SubAsync("local|missing", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await Handler().Handle(new BackupNowCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _backup.Verify(b => b.CreateBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
