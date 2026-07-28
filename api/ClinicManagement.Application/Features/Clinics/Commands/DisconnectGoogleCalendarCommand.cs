using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Admin-only: disconnect the caller's clinic from Google Calendar (AC-P2.33) — clears the stored refresh token
/// and target calendar id, so App→Google pushes stop and « status » reports « non connecté ».
/// <para>
/// <see cref="Domain.Entities.Clinic.ClearGoogleCalendarConnection"/> has existed since Google tokens moved
/// per-clinic into the DB and had **zero callers**: a clinic that authorised the wrong Google account could
/// only overwrite it by re-running the whole OAuth flow, and could never simply stop syncing.
/// </para>
/// <para>
/// Deliberately does **not** touch <c>Appointment.GoogleCalendarEventId</c> (AC-P2.35). Those ids point at real
/// events in the clinic's own Google account; clearing them would orphan every existing event and make a
/// reconnect duplicate the entire calendar, and deleting the events themselves would destroy data in an
/// account we are being told to stop touching.
/// </para>
/// </summary>
public class DisconnectGoogleCalendarCommand : IRequest<Result<bool>>
{
}

public class DisconnectGoogleCalendarCommandHandler : IRequestHandler<DisconnectGoogleCalendarCommand, Result<bool>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DisconnectGoogleCalendarCommandHandler> _logger;

    public DisconnectGoogleCalendarCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<DisconnectGoogleCalendarCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DisconnectGoogleCalendarCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<bool>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (user == null)
            {
                return Result<bool>.Failure("Utilisateur introuvable.");
            }

            // The endpoint is AdminOnly; the DB role is re-checked here as everywhere else, because the
            // authoritative clinic and role come from the user record, not the token claim.
            if (!user.IsAdmin())
            {
                return Result<bool>.Failure("Seuls les administrateurs peuvent déconnecter Google Calendar.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<bool>.Failure("Cabinet introuvable.");
            }

            // Idempotent: nothing connected is a successful no-op, not an error. Reconnecting and disconnecting
            // twice in a row is a normal thing for an admin to do while fixing a wrong account.
            if (string.IsNullOrEmpty(clinic.GoogleRefreshToken) && string.IsNullOrEmpty(clinic.GoogleCalendarId))
            {
                return Result<bool>.Success(true);
            }

            clinic.ClearGoogleCalendarConnection();
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Google Calendar disconnected for clinic {ClinicId} by {UserId}", clinic.Id, user.Id);
            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure disconnecting Google Calendar");
            return Result<bool>.Failure("Erreur lors de la déconnexion de Google Calendar. Veuillez réessayer.");
        }
    }
}
