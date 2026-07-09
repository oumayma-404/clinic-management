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
                return Result<BackupResultDto>.Failure("User ID not found in token");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<BackupResultDto>.Failure("User not found");
            }

            // AC-8.1: only an admin may trigger a backup.
            if (!admin.IsAdmin())
            {
                return Result<BackupResultDto>.Failure("Only admins can run a backup");
            }

            var result = await _backupService.CreateBackupAsync(request.DestinationFolder, cancellationToken);
            return Result<BackupResultDto>.Success(result);
        }
        catch (Exception ex)
        {
            // The service throws with a clear operator-facing reason (unwritable / disk full /
            // pg_dump missing / dump failed); surface it verbatim rather than a silent failure.
            return Result<BackupResultDto>.Failure(ex.Message);
        }
    }
}
