using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Backup.Commands;

/// <summary>
/// The clinic's unattended-backup schedule (L4a) — on/off, the clinic-local hour, how many copies to keep, and
/// after how long without a success the admins are told.
///
/// <para><b>This command is the point of the four columns.</b> The spec names <c>Clinic.SetStockExpiryLeadDays</c>
/// explicitly as the failure not to repeat: it shipped with <b>zero</b> production callers, so the expiry window
/// has been permanently 30 days ever since — a setting nobody can set. So the schedule ships with its writer, its
/// reader (<c>GetBackupHistoryQuery</c>) and its UI in the same change.</para>
///
/// <para>One command for the four fields rather than four, mirroring <c>Clinic.SetBackupSettings</c>: they are one
/// decision, and a per-field endpoint invites a settings screen that saves half of it.</para>
/// </summary>
public class SetBackupScheduleCommand : IRequest<Result<BackupScheduleDto>>
{
    public bool Enabled { get; set; }
    public int HourLocal { get; set; }
    public int RetentionCount { get; set; }
    public int StaleAfterHours { get; set; }
}

/// <summary>The schedule as stored, echoed back so the screen renders what the server accepted.</summary>
public record BackupScheduleDto(bool Enabled, int HourLocal, int RetentionCount, int StaleAfterHours);

public class SetBackupScheduleCommandHandler
    : IRequestHandler<SetBackupScheduleCommand, Result<BackupScheduleDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetBackupScheduleCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BackupScheduleDto>> Handle(
        SetBackupScheduleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<BackupScheduleDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var caller = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (caller == null)
            {
                return Result<BackupScheduleDto>.Failure("Utilisateur introuvable.");
            }

            // Defence in depth behind the controller's AdminOnly policy — the shape every command on this
            // controller uses. Turning the backup off is exactly the sort of change whose effect cannot be read
            // off any screen afterwards, which is what AdminOnly is for.
            if (!caller.IsAdmin())
            {
                return Result<BackupScheduleDto>.Failure(
                    "Seuls les administrateurs peuvent modifier la planification des sauvegardes.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<BackupScheduleDto>.Failure("Cabinet introuvable.");
            }

            clinic.SetBackupSettings(
                request.Enabled, request.HourLocal, request.RetentionCount, request.StaleAfterHours);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<BackupScheduleDto>.Success(new BackupScheduleDto(
                clinic.BackupEnabled, clinic.BackupHourLocal, clinic.BackupRetentionCount,
                clinic.BackupStaleAfterHours));
        }
        catch (ArgumentException ex)
        {
            // The aggregate's own French range messages, verbatim — the same arrangement
            // SetRecallSettingsCommand uses, so the ranges are stated in exactly one place (the entity).
            return Result<BackupScheduleDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<BackupScheduleDto>.Failure(
                "Erreur lors de l'enregistrement de la planification des sauvegardes.");
        }
    }
}
