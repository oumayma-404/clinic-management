using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Messaging;
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
    private readonly IOutboundEndpointPolicy _endpointPolicy;
    private readonly IVendorMessagingAvailability _vendorMessaging;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateClinicReminderSettingsCommandHandler(
        IClinicReminderSettingsRepository settingsRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IReminderSecretProtector secretProtector,
        IReminderSettingsProvider settingsProvider,
        IOutboundEndpointPolicy endpointPolicy,
        IVendorMessagingAvailability vendorMessaging,
        IUnitOfWork unitOfWork)
    {
        _settingsRepository = settingsRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _secretProtector = secretProtector;
        _settingsProvider = settingsProvider;
        _endpointPolicy = endpointPolicy;
        _vendorMessaging = vendorMessaging;
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

            // AC-1.7 — where the vendor provisions WhatsApp, its identity is not something a cabinet types. The
            // fields are absent from the screen; this is what makes their absence a rule rather than a layout choice.
            var vendorManagesWhatsApp = _vendorMessaging.SellsVendorMessaging;
            if (vendorManagesWhatsApp && ClaimsAWhatsAppCredential(input))
            {
                return Result<ReminderSettingsDto>.Failure(
                    MessagingRefusals.ManualWhatsApp, MessagingRefusals.ManualWhatsAppCode);
            }

            // ⚠️ And the four WhatsApp identity fields are then carried over from what is STORED, not from the
            // request. `ApplyNonSecretSettings` replaces every field it is given, so a screen that no longer renders
            // them posts nulls — which would erase the phone-number id and template name that « Connecter WhatsApp »
            // wrote, silently un-configuring the channel (ReminderSettingsProvider.ClaimsItsOwnWhatsApp reads exactly
            // those columns) on the next unrelated save of an SMS setting.
            var whatsAppApiUrl = vendorManagesWhatsApp ? settings.WhatsAppApiUrl : input.WhatsAppApiUrl;
            var whatsAppPhoneNumberId =
                vendorManagesWhatsApp ? settings.WhatsAppPhoneNumberId : input.WhatsAppPhoneNumberId;
            var whatsAppTemplateName =
                vendorManagesWhatsApp ? settings.WhatsAppTemplateName : input.WhatsAppTemplateName;
            var whatsAppTemplateLanguage =
                vendorManagesWhatsApp ? settings.WhatsAppTemplateLanguage : input.WhatsAppTemplateLanguage;

            // The three endpoint fields are the ones a tenant can aim anywhere, so the domain refuses a
            // non-public target. Caught here rather than left to the exception middleware: this is a typo in a
            // settings form, and it must come back as the French 400 the screen renders — not a generic 500.
            var allowPrivate = _endpointPolicy.AllowsPrivateNetworkEndpoints;
            try
            {
                settings.ApplyNonSecretSettings(
                    input.SmsEnabled,
                    input.WhatsAppEnabled,
                    input.SmsSenderId,
                    whatsAppPhoneNumberId,
                    whatsAppTemplateName,
                    whatsAppTemplateLanguage,
                    input.SmsApiUrl,
                    whatsAppApiUrl,
                    input.LeadTimeHours,
                    input.MessageTemplateBody,
                    allowPrivate);

                settings.ApplySmtpSettings(
                    input.SmtpHost,
                    input.SmtpPort,
                    input.SmtpUseTls,
                    input.SmtpUsername,
                    input.SmtpFromAddress,
                    input.SmtpFromName,
                    allowPrivate);
            }
            catch (ArgumentException ex)
            {
                return Result<ReminderSettingsDto>.Failure(ex.Message);
            }

            // Secrets are write-only: only re-encrypt & replace when a non-blank value is supplied.
            if (!string.IsNullOrWhiteSpace(input.SmsApiKey))
            {
                settings.SetSmsApiKeyEncrypted(_secretProtector.Protect(input.SmsApiKey.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(input.WhatsAppAccessToken))
            {
                settings.SetWhatsAppAccessTokenEncrypted(_secretProtector.Protect(input.WhatsAppAccessToken.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(input.SmtpPassword))
            {
                settings.SetSmtpPasswordEncrypted(_secretProtector.Protect(input.SmtpPassword.Trim()));
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
            var dto = settings.ToDto(vendorManagesWhatsApp) with
            {
                SmsEffectiveStatus = resolved.SmsConfigured
                    ? ReminderEffectiveStatus.Configured
                    : ReminderEffectiveStatus.NotConfigured,
                WhatsAppEffectiveStatus = resolved.WhatsAppConfigured
                    ? ReminderEffectiveStatus.Configured
                    : ReminderEffectiveStatus.NotConfigured,
                EmailEffectiveStatus = resolved.EmailConfigured
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

    /// <summary>
    /// Did this request try to supply a WhatsApp credential? The same three fields
    /// <c>ReminderSettingsProvider.ClaimsItsOwnWhatsApp</c> reads, deliberately: what AC-1.7 refuses is exactly what
    /// would make the cabinet own its own channel.
    /// </summary>
    private static bool ClaimsAWhatsAppCredential(UpdateReminderSettingsRequest input) =>
        !string.IsNullOrWhiteSpace(input.WhatsAppApiUrl)
        || !string.IsNullOrWhiteSpace(input.WhatsAppPhoneNumberId)
        || !string.IsNullOrWhiteSpace(input.WhatsAppAccessToken);
}
