using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

/// <summary>
/// Admin-only (AC-2): creates or updates the caller's clinic reminder settings. Non-secret fields replace
/// the stored values; secrets are write-only — an omitted/blank secret leaves the stored value unchanged, a
/// provided one is encrypted (Data Protection) and replaces it. Returns the secret-masked settings.
/// </summary>
public class UpdateClinicReminderSettingsCommand : IRequest<Result<ReminderSettingsDto>>
{
    public required UpdateReminderSettingsRequest Settings { get; init; }
}

public class UpdateClinicReminderSettingsCommandHandler
    : IRequestHandler<UpdateClinicReminderSettingsCommand, Result<ReminderSettingsDto>>
{
    private readonly IClinicReminderSettingsRepository _settingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IReminderSecretProtector _secretProtector;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateClinicReminderSettingsCommandHandler(
        IClinicReminderSettingsRepository settingsRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IReminderSecretProtector secretProtector,
        IReminderSettingsProvider settingsProvider,
        IUnitOfWork unitOfWork)
    {
        _settingsRepository = settingsRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _secretProtector = secretProtector;
        _settingsProvider = settingsProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReminderSettingsDto>> Handle(
        UpdateClinicReminderSettingsCommand request, CancellationToken cancellationToken)
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
                return Result<ReminderSettingsDto>.Failure("Seuls les administrateurs peuvent modifier les paramètres de rappel.");
            }

            var input = request.Settings;

            var settings = await _settingsRepository.GetByClinicIdAsync(user.ClinicId, cancellationToken);
            var isNew = settings == null;
            settings ??= new ClinicReminderSettings(user.ClinicId);

            settings.ApplyNonSecretSettings(
                input.SmsEnabled,
                input.WhatsAppEnabled,
                input.SmsSenderId,
                input.WhatsAppPhoneNumberId,
                input.WhatsAppTemplateName,
                input.WhatsAppTemplateLanguage,
                input.SmsApiUrl,
                input.WhatsAppApiUrl,
                input.LeadTimeHours,
                input.MessageTemplateBody);

            // Secrets are write-only: only re-encrypt & replace when a non-blank value is supplied.
            if (!string.IsNullOrWhiteSpace(input.SmsApiKey))
            {
                settings.SetSmsApiKeyEncrypted(_secretProtector.Protect(input.SmsApiKey.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(input.WhatsAppAccessToken))
            {
                settings.SetWhatsAppAccessTokenEncrypted(_secretProtector.Protect(input.WhatsAppAccessToken.Trim()));
            }

            if (isNew)
            {
                await _settingsRepository.AddAsync(settings, cancellationToken);
            }
            else
            {
                await _settingsRepository.UpdateAsync(settings, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Return the freshly-resolved effective status too, so the UI can immediately show whether the
            // channel is now actually sendable (AC-2) without a second round-trip.
            var resolved = await _settingsProvider.ResolveAsync(user.ClinicId, cancellationToken);
            var dto = settings.ToDto() with
            {
                SmsEffectiveStatus = resolved.SmsConfigured
                    ? ReminderEffectiveStatus.Configured
                    : ReminderEffectiveStatus.NotConfigured,
                WhatsAppEffectiveStatus = resolved.WhatsAppConfigured
                    ? ReminderEffectiveStatus.Configured
                    : ReminderEffectiveStatus.NotConfigured,
            };

            return Result<ReminderSettingsDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ReminderSettingsDto>.Failure($"Error updating reminder settings: {ex.Message}");
        }
    }
}
