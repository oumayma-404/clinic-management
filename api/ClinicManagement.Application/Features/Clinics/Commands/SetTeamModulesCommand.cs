using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Paramètres → « Équipe »: turns the secretary congés and the pointage on or off. Here rather than under <c>Team</c> so
/// it broadcasts <c>clinics</c>, which is what refreshes every open menu. Tri-state: an omitted switch is left as it is.
/// </summary>
public class SetTeamModulesCommand : IRequest<Result<TeamModulesDto>>
{
    public bool? LeaveRequests { get; set; }
    public bool? Attendance { get; set; }

    /// <summary>« Bloquer l'app tant que l'arrivée n'est pas pointée » (<c>pointage-obligatoire</c>); omitted = unchanged.</summary>
    public bool? AttendanceLock { get; set; }

    /// <summary>The clinic version the client read; <c>0</c> means « not supplied ».</summary>
    public uint Version { get; set; }
}

public class SetTeamModulesCommandHandler : IRequestHandler<SetTeamModulesCommand, Result<TeamModulesDto>>
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetTeamModulesCommandHandler(
        IClinicRepository clinics, IUserRepository users, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _clinics = clinics;
        _users = users;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TeamModulesDto>> Handle(SetTeamModulesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
                return Result<TeamModulesDto>.Failure("Session invalide, veuillez vous reconnecter.");

            var admin = await _users.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
                return Result<TeamModulesDto>.Failure("Utilisateur introuvable.");
            if (!admin.IsAdmin())
                return Result<TeamModulesDto>.Failure("Seuls les administrateurs peuvent modifier ces réglages.");

            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic == null)
                return Result<TeamModulesDto>.Failure("Clinique introuvable.");

            // The pointage and its lock becoming both on stamps when, so the lock starts at the next opening, never mid-day.
            var now = DateTime.UtcNow;
            var changed = false;
            if (request.LeaveRequests is { } leave && leave != clinic.LeaveRequestsEnabled)
            {
                clinic.SetLeaveRequestsEnabled(leave);
                changed = true;
            }
            if (request.Attendance is { } attendance && attendance != clinic.AttendanceEnabled)
            {
                clinic.SetAttendanceEnabled(attendance, now);
                changed = true;
            }
            if (request.AttendanceLock is { } attendanceLock && attendanceLock != clinic.AttendanceLockEnabled)
            {
                clinic.SetAttendanceLockEnabled(attendanceLock, now);
                changed = true;
            }

            if (changed)
            {
                _unitOfWork.SetExpectedVersion(clinic, request.Version);
                await _clinics.UpdateAsync(clinic, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<TeamModulesDto>.Success(new TeamModulesDto
            {
                LeaveRequestsEnabled = clinic.LeaveRequestsEnabled,
                AttendanceEnabled = clinic.AttendanceEnabled,
                AttendanceLockEnabled = clinic.AttendanceLockEnabled,
                Version = clinic.Version,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<TeamModulesDto>.Failure("Erreur lors de l'enregistrement des réglages de l'équipe.", ex);
        }
    }
}
