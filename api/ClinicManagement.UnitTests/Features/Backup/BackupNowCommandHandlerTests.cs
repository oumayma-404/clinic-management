using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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

    // L4d — every attempt is recorded, so the handler now owns a ledger, a UoW and the staleness generator.
    private readonly Mock<IBackupRunRepository> _runs = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<INotificationGenerator> _notifications = new();

    /// <summary>The rows the handler wrote, so a test can assert the outcome it recorded rather than only the DTO.</summary>
    private readonly List<BackupRun> _recorded = new();

    public BackupNowCommandHandlerTests()
    {
        _runs.Setup(r => r.AddAsync(It.IsAny<BackupRun>(), It.IsAny<CancellationToken>()))
            .Callback<BackupRun, CancellationToken>((run, _) => _recorded.Add(run))
            .Returns(Task.CompletedTask);
        _runs.Setup(r => r.UpdateAsync(It.IsAny<BackupRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _backup.Setup(b => b.ResolveDestinationRoot(It.IsAny<string?>())).Returns(@"D:\backups");
    }

    private BackupNowCommandHandler Handler() => new(
        _users.Object, _context.Object, _backup.Object, _runs.Object, _uow.Object, _notifications.Object);

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
        var dto = new BackupResultDto
        {
            DestinationPath = @"D:\backups\clinic-backup-x",
            SizeBytes = 1234,
            TimestampUtc = DateTime.UtcNow,
            // L4c: pg_dump exiting 0 is not proof. A success carries the count pg_restore --list read back.
            VerifiedObjectCount = 41,
        };
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

    /*
     * ── L4d ─────────────────────────────────────────────────────────────────────────────────────────────────
     * Every attempt is recorded, and the failed rows are the valuable ones: « nobody has backed up since
     * Tuesday » and « it has been trying every night and failing » are entirely different conversations.
     */

    // A successful manual backup records a Succeeded run carrying the verified object count, and clears the
    // staleness alert — the alert is about the state of the data, not about which job wrote it.
    [Fact]
    public async Task A_Successful_Backup_Is_Recorded_And_Clears_The_Staleness_Alert()
    {
        var admin = Local("admin");
        AsCaller(admin);
        _backup.Setup(b => b.CreateBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto
            {
                DestinationPath = @"D:\backups\clinic-backup-x",
                SizeBytes = 4096,
                TimestampUtc = DateTime.UtcNow,
                VerifiedObjectCount = 41,
            });

        var result = await Handler().Handle(new BackupNowCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var run = Assert.Single(_recorded);
        Assert.Equal(BackupOutcome.Succeeded, run.Outcome);
        Assert.Equal(41, run.VerifiedObjectCount);
        Assert.Equal(BackupRun.TriggerManual, run.Trigger);
        _notifications.Verify(
            n => n.ClearBackupStaleAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // A FAILED backup is recorded too, with its reason. Without this the history would show only successes,
    // which is indistinguishable from a clinic that never tried — the exact ambiguity the ledger removes.
    [Fact]
    public async Task A_Failed_Backup_Is_Recorded_With_Its_Reason()
    {
        var admin = Local("admin");
        AsCaller(admin);
        _backup.Setup(b => b.CreateBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Espace disque insuffisant"));

        var result = await Handler().Handle(new BackupNowCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        var run = Assert.Single(_recorded);
        Assert.Equal(BackupOutcome.Failed, run.Outcome);
        Assert.Contains("Espace disque insuffisant", run.Error);
        // A failed backup must NOT clear the staleness alert: nothing about the data got safer.
        _notifications.Verify(
            n => n.ClearBackupStaleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The Running row is committed BEFORE the dump starts, so a crash mid-backup leaves a visible row rather
    // than no row at all — « rien ce soir-là » being the reading that loses a practice its data.
    [Fact]
    public async Task The_Run_Is_Committed_Before_The_Dump_Starts()
    {
        var admin = Local("admin");
        AsCaller(admin);

        var savesBeforeDump = 0;
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _backup.Setup(b => b.CreateBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => savesBeforeDump =
                _uow.Invocations.Count(i => i.Method.Name == nameof(IUnitOfWork.SaveChangesAsync)))
            .ReturnsAsync(new BackupResultDto
            {
                DestinationPath = @"D:\backups\clinic-backup-x",
                SizeBytes = 1,
                TimestampUtc = DateTime.UtcNow,
                VerifiedObjectCount = 41,
            });

        await Handler().Handle(new BackupNowCommand(), CancellationToken.None);

        Assert.Equal(1, savesBeforeDump);
    }
}
