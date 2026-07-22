using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>
/// Admin-only (AC-1): returns the caller's clinic reminder settings, secret-masked (channel toggles +
/// non-secret identity + per-secret configured flags — never the secret values). A clinic with no settings
/// row yet returns an all-inherit DTO.
/// </summary>
public class GetClinicReminderSettingsQuery : IRequest<Result<ReminderSettingsDto>>
{
}

public class GetClinicReminderSettingsQueryHandler
    : IRequestHandler<GetClinicReminderSettingsQuery, Result<ReminderSettingsDto>>
{
    private readonly IClinicReminderSettingsRepository _settingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IReminderSettingsProvider _settingsProvider;

    public GetClinicReminderSettingsQueryHandler(
        IClinicReminderSettingsRepository settingsRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IReminderSettingsProvider settingsProvider)
    {
        _settingsRepository = settingsRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<ReminderSettingsDto>> Handle(
        GetClinicReminderSettingsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ReminderSettingsDto>.Failure("User ID not found in token");
            }

            var user = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (user == null)
            {
                return Result<ReminderSettingsDto>.Failure("User not found");
            }

            if (!user.IsAdmin())
            {
                return Result<ReminderSettingsDto>.Failure("Only admins can view reminder settings");
            }

            var settings = await _settingsRepository.GetByClinicIdAsync(user.ClinicId, cancellationToken);

            // effectiveStatus (AC-2) reflects the *resolved* settings (per-clinic override else per-install,
            // secrets decrypted) — so a channel toggled on but missing a URL/secret reads not_configured even
            // when a WhatsApp OAuth "connection" exists.
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
        catch (Exception ex)
        {
            return Result<ReminderSettingsDto>.Failure($"Error retrieving reminder settings: {ex.Message}");
        }
    }
}
