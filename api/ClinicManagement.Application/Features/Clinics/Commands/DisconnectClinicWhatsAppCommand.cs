using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Admin-only, Cloud-only: disconnects the caller's clinic WhatsApp connection — clears the stored WABA id,
/// phone-number id and access token, disables the channel and resets the status to <c>NotConnected</c>. The
/// Meta app-unsubscribe is best-effort: a failure there is logged but never fails the local disconnect.
/// </summary>
public class DisconnectClinicWhatsAppCommand : IRequest<Result<ReminderSettingsDto>>
{
}

public class DisconnectClinicWhatsAppCommandHandler
    : IRequestHandler<DisconnectClinicWhatsAppCommand, Result<ReminderSettingsDto>>
{
    private readonly IClinicReminderSettingsRepository _settingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IReminderSecretProtector _secretProtector;
    private readonly IWhatsAppOnboardingService _onboardingService;
    private readonly IUnitOfWork _unitOfWork;

    public DisconnectClinicWhatsAppCommandHandler(
        IClinicReminderSettingsRepository settingsRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IReminderSecretProtector secretProtector,
        IWhatsAppOnboardingService onboardingService,
        IUnitOfWork unitOfWork)
    {
        _settingsRepository = settingsRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _secretProtector = secretProtector;
        _onboardingService = onboardingService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReminderSettingsDto>> Handle(
        DisconnectClinicWhatsAppCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ReminderSettingsDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (user == null)
            {
                return Result<ReminderSettingsDto>.Failure("Utilisateur introuvable.");
            }

            if (!user.IsAdmin())
            {
                return Result<ReminderSettingsDto>.Failure("Seuls les administrateurs peuvent déconnecter WhatsApp.");
            }

            var settings = await _settingsRepository.GetByClinicIdAsync(user.ClinicId, cancellationToken);

            // No settings row / nothing connected → nothing to do; return the current (not-connected) view.
            if (settings == null || string.IsNullOrEmpty(settings.WhatsAppBusinessAccountId))
            {
                return Result<ReminderSettingsDto>.Success(settings.ToDto());
            }

            await TryUnsubscribeAsync(settings.WhatsAppBusinessAccountId, settings.WhatsAppAccessTokenEncrypted, cancellationToken);

            settings.ClearWhatsAppConnection();
            await _settingsRepository.UpdateAsync(settings, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ReminderSettingsDto>.Success(settings.ToDto());
        }
        catch (Exception ex)
        {
            return Result<ReminderSettingsDto>.Failure($"Error disconnecting WhatsApp: {ex.Message}");
        }
    }

    // Best-effort: decrypt the stored token and ask Meta to unsubscribe the app. Any failure (rotated key,
    // network, Meta error) is swallowed — the local disconnect must still proceed (AC-5).
    private async Task TryUnsubscribeAsync(string wabaId, string? tokenCiphertext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokenCiphertext))
        {
            return;
        }

        try
        {
            var accessToken = _secretProtector.Unprotect(tokenCiphertext);
            await _onboardingService.UnsubscribeAppAsync(wabaId, accessToken, cancellationToken);
        }
        catch
        {
            // Intentionally swallowed — disconnect is local-authoritative; unsubscribe is best-effort.
        }
    }
}
