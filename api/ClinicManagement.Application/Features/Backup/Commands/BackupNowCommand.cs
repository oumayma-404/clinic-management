using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Backup.Commands;

/// <summary>
/// Admin-only one-click "Backup now" (US-8 / FR-G / AC-8.1). Dumps the database and copies the
/// file-storage folder to a timestamped destination folder. Mirrors the caller-resolution +
/// admin guard of <see cref="Features.Users.Commands.ResetUserPasswordCommand"/> (defense in depth
/// behind the controller's <c>AdminOnly</c> policy). Failures are returned as a clear
/// <see cref="Result{T}"/> failure, never a silent success (AC-8.2 / AC-8.3).
/// </summary>
public class BackupNowCommand : IRequest<Result<BackupResultDto>>
{
    /// <summary>
    /// Destination folder the backup subfolder is written under. When null/empty the service
    /// falls back to the configured <c>Backup:DefaultDestination</c>.
    /// </summary>
    public string? DestinationFolder { get; set; }
}

public class BackupNowCommandHandler : IRequestHandler<BackupNowCommand, Result<BackupResultDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IBackupService _backupService;
    private readonly IBackupRunRepository _backupRuns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;

    public BackupNowCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IBackupService backupService,
        IBackupRunRepository backupRuns,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _backupService = backupService;
        _backupRuns = backupRuns;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
    }

    public async Task<Result<BackupResultDto>> Handle(BackupNowCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<BackupResultDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<BackupResultDto>.Failure("Utilisateur introuvable.");
            }

            // AC-8.1: only an admin may trigger a backup.
            if (!admin.IsAdmin())
            {
                return Result<BackupResultDto>.Failure("Seuls les administrateurs peuvent lancer une sauvegarde.");
            }

            // L4d — a manual backup is recorded in the same ledger as a scheduled one, and the Running row is
            // committed BEFORE the dump starts. Same reasoning as in `BackupJob`: a crash mid-dump then leaves a
            // visible row instead of no row at all. It also means « Dernière sauvegarde réussie » is true
            // whichever way the backup was taken — a headline that only knew about the nightly job would read
            // « jamais » on a clinic whose admin backs up by hand every evening.
            var run = new Domain.Entities.BackupRun(
                Guid.NewGuid(), admin.ClinicId, Domain.Entities.BackupRun.TriggerManual, DateTime.UtcNow);
            await _backupRuns.AddAsync(run, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            try
            {
                var result = await _backupService.CreateBackupAsync(request.DestinationFolder, cancellationToken);

                run.MarkSucceeded(
                    result.DestinationPath, result.SizeBytes, result.VerifiedObjectCount, DateTime.UtcNow);
                await _backupRuns.UpdateAsync(run, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // The staleness alert is about the state of the data, not about which job wrote it — so a manual
                // backup clears it. Best-effort like every other generator call; it never throws.
                await _notificationGenerator.ClearBackupStaleAsync(admin.ClinicId, cancellationToken);

                return Result<BackupResultDto>.Success(result);
            }
            catch (InvalidOperationException ex)
            {
                // IBackupService surfaces every EXPECTED failure (unwritable / disk full / pg_dump missing /
                // dump failed / unreadable dump) as InvalidOperationException with a clear operator-facing
                // reason — record it on the run and return it verbatim.
                run.MarkFailed(
                    ex.Message, DateTime.UtcNow, _backupService.ResolveDestinationRoot(request.DestinationFolder));
                await _backupRuns.UpdateAsync(run, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<BackupResultDto>.Failure(ex.Message);
            }
        }
        catch (InvalidOperationException ex)
        {
            // The outer net: a failure raised before the run row exists (or while writing it). Anything that is
            // not an InvalidOperationException (a genuine bug, or OperationCanceledException on cancellation) is
            // left to propagate to the global exception middleware rather than masked as a benign failure
            // (Finding 7).
            return Result<BackupResultDto>.Failure(ex.Message);
        }
    }
}
