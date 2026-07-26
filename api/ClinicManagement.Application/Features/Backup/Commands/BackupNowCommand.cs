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

    public BackupNowCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IBackupService backupService)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _backupService = backupService;
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

            var result = await _backupService.CreateBackupAsync(request.DestinationFolder, cancellationToken);
            return Result<BackupResultDto>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            // IBackupService surfaces every EXPECTED failure (unwritable / disk full / pg_dump missing /
            // dump failed) as InvalidOperationException with a clear operator-facing reason — return it
            // verbatim. Anything else (a genuine bug, or OperationCanceledException on cancellation) is
            // left to propagate to the global exception middleware rather than masked as a benign failure
            // (Finding 7).
            return Result<BackupResultDto>.Failure(ex.Message);
        }
    }
}
