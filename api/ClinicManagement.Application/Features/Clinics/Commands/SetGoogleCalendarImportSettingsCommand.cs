using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Admin-only: record whether the connected Google calendar holds nothing but appointments
/// (<c>calendar-import-review</c>). With it set, the Google→App import stops demanding a keyword in the event
/// title and treats « Prénom Nom » as an appointment.
///
/// <para>⚠️ Refused when the clinic has <b>no</b> Google connection. The setting is a statement about a specific
/// calendar, and storing one for a connection that does not exist would leave a promise waiting to apply itself to
/// whichever account is authorised next — the same reason
/// <see cref="Domain.Entities.Clinic.ClearGoogleCalendarConnection"/> resets it.</para>
/// </summary>
public class SetGoogleCalendarImportSettingsCommand : IRequest<Result<bool>>
{
    public bool HoldsOnlyAppointments { get; set; }
}

public class SetGoogleCalendarImportSettingsCommandHandler
    : IRequestHandler<SetGoogleCalendarImportSettingsCommand, Result<bool>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetGoogleCalendarImportSettingsCommandHandler> _logger;

    public SetGoogleCalendarImportSettingsCommandHandler(
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<SetGoogleCalendarImportSettingsCommandHandler> logger)
    {
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(
        SetGoogleCalendarImportSettingsCommand request, CancellationToken cancellationToken)
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
                return Result<bool>.Failure("Seuls les administrateurs peuvent modifier l'import Google Agenda.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<bool>.Failure("Cabinet introuvable.");
            }

            // ⚠️ Both token columns, for the reason DisconnectGoogleCalendarCommand states: a clinic connected
            // before FR-3.4 holds only the legacy plaintext, one connected after holds only the ciphertext.
            if (request.HoldsOnlyAppointments
                && string.IsNullOrEmpty(clinic.GoogleRefreshToken)
                && string.IsNullOrEmpty(clinic.GoogleRefreshTokenProtected))
            {
                return Result<bool>.Failure(
                    "Connectez d'abord Google Agenda : ce réglage décrit le calendrier relié.");
            }

            clinic.SetGoogleCalendarHoldsOnlyAppointments(request.HoldsOnlyAppointments);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Google import gate set to holdsOnlyAppointments={Value} for clinic {ClinicId} by {UserId}",
                request.HoldsOnlyAppointments, clinic.Id, user.Id);

            return Result<bool>.Success(request.HoldsOnlyAppointments);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure setting the Google import gate");
            return Result<bool>.Failure("Erreur lors de l'enregistrement du réglage. Veuillez réessayer.");
        }
    }
}
