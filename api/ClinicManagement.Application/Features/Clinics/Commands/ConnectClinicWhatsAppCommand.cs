using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Admin-only, Cloud-only: completes a Meta Embedded-Signup connection for the caller's clinic. Runs the
/// Graph provisioning steps (exchange code → subscribe app → register phone) and only then persists the
/// encrypted token + WABA/phone ids on the clinic's <see cref="ClinicReminderSettings"/>. <b>Atomic</b> — if
/// any step fails, nothing is stored, the clinic stays <c>NotConnected</c>, and a specific French message is
/// returned.
/// </summary>
public class ConnectClinicWhatsAppCommand : IRequest<Result<ReminderSettingsDto>>
{
    public required ConnectWhatsAppRequest Request { get; init; }
}

public class ConnectClinicWhatsAppCommandHandler
    : IRequestHandler<ConnectClinicWhatsAppCommand, Result<ReminderSettingsDto>>
{
    private readonly IClinicReminderSettingsRepository _settingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IReminderSecretProtector _secretProtector;
    private readonly IWhatsAppOnboardingService _onboardingService;
    private readonly IUnitOfWork _unitOfWork;

    public ConnectClinicWhatsAppCommandHandler(
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
        ConnectClinicWhatsAppCommand request, CancellationToken cancellationToken)
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
                return Result<ReminderSettingsDto>.Failure("Seuls les administrateurs peuvent connecter WhatsApp.");
            }

            var input = request.Request;

            // Provision against Meta FIRST — persist nothing until all steps succeed (atomic connect).
            string accessToken;
            try
            {
                accessToken = await _onboardingService.ExchangeCodeForTokenAsync(input.Code, cancellationToken);
                await _onboardingService.SubscribeAppAsync(input.WabaId, accessToken, cancellationToken);
                await _onboardingService.RegisterPhoneAsync(input.PhoneNumberId, accessToken, cancellationToken);
            }
            catch (WhatsAppOnboardingException ex)
            {
                return Result<ReminderSettingsDto>.Failure(ToFrenchMessage(ex.Error));
            }

            var settings = await _settingsRepository.GetByClinicIdAsync(user.ClinicId, cancellationToken);
            var isNew = settings == null;
            settings ??= new ClinicReminderSettings(user.ClinicId);

            settings.ApplyWhatsAppConnection(input.WabaId, input.PhoneNumberId);
            settings.SetWhatsAppAccessTokenEncrypted(_secretProtector.Protect(accessToken));

            if (isNew)
            {
                await _settingsRepository.AddAsync(settings, cancellationToken);
            }
            else
            {
                await _settingsRepository.UpdateAsync(settings, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ReminderSettingsDto>.Success(settings.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ReminderSettingsDto>.Failure($"Error connecting WhatsApp: {ex.Message}");
        }
    }

    // Maps an onboarding failure to the specific French message the spec pins (AC-3). Backend messages are
    // French here by design because the spec's API contract enumerates these exact strings.
    private static string ToFrenchMessage(WhatsAppOnboardingError error) => error switch
    {
        WhatsAppOnboardingError.NumberAlreadyRegistered =>
            "Ce numéro WhatsApp est déjà enregistré ailleurs ou nécessite une migration.",
        WhatsAppOnboardingError.WabaNotEligible =>
            "Compte WhatsApp Business non éligible : la vérification de l'entreprise est requise.",
        WhatsAppOnboardingError.CodeExchangeFailed =>
            "Échec de la connexion à Meta, réessayez.",
        _ => "Échec de la connexion WhatsApp, réessayez.",
    };
}
