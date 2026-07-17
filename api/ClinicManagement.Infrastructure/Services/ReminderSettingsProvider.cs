using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Resolves the effective reminder settings for a clinic: per-clinic overrides where set, else the per-install
/// <see cref="RemindersConfig"/>. Channel toggles are <c>bool?</c> (null = inherit the install default);
/// identity fields fall back to the install value; secrets are decrypted in-process. A clinic that set its own
/// secret whose ciphertext can't be decrypted (rotated/unavailable key) is treated as <b>not configured</b>
/// for that channel — logged once per scope, never thrown (edge case). Endpoint URLs stay per-install.
/// </summary>
public class ReminderSettingsProvider : IReminderSettingsProvider
{
    private readonly IClinicReminderSettingsRepository _repository;
    private readonly IReminderSecretProtector _secretProtector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReminderSettingsProvider> _logger;

    // Dedupe decryption-failure Error logs within this (scoped) instance so one dispatch tick doesn't
    // re-log the same broken key for every pending row.
    private readonly HashSet<NotificationType> _decryptFailuresLogged = new();

    // Memoize full resolution per clinic for this (scoped) instance so a dispatch tick with many rows for the
    // same clinic (or many null-ClinicId legacy rows) resolves each clinic's settings once, not once per row.
    // Keyed by clinic id with Guid.Empty as the sentinel for a null ClinicId (Dictionary rejects null keys;
    // real clinic ids are never Guid.Empty).
    private readonly Dictionary<Guid, ResolvedReminderSettings> _resolveCache = new();

    public ReminderSettingsProvider(
        IClinicReminderSettingsRepository repository,
        IReminderSecretProtector secretProtector,
        IConfiguration configuration,
        ILogger<ReminderSettingsProvider> logger)
    {
        _repository = repository;
        _secretProtector = secretProtector;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NotificationType>> ResolveEnabledChannelsAsync(
        Guid? clinicId, CancellationToken cancellationToken = default)
    {
        var clinic = await LoadAsync(clinicId, cancellationToken);
        return ResolveEnabledChannels(clinic);
    }

    public async Task<ResolvedReminderSettings> ResolveAsync(
        Guid? clinicId, CancellationToken cancellationToken = default)
    {
        var cacheKey = clinicId ?? Guid.Empty;
        if (_resolveCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var clinic = await LoadAsync(clinicId, cancellationToken);

        var resolved = new ResolvedReminderSettings
        {
            EnabledChannels = ResolveEnabledChannels(clinic),

            // Endpoint URLs stay per-install (out of scope for per-clinic override).
            SmsApiUrl = RemindersConfig.SmsApiUrl(_configuration),
            SmsSenderId = clinic?.SmsSenderId ?? RemindersConfig.SmsSenderId(_configuration),
            SmsApiKey = ResolveSecret(clinic?.SmsApiKeyEncrypted, RemindersConfig.SmsApiKey(_configuration), NotificationType.SMS),

            WhatsAppApiUrl = RemindersConfig.WhatsAppApiUrl(_configuration),
            WhatsAppPhoneNumberId = clinic?.WhatsAppPhoneNumberId ?? RemindersConfig.WhatsAppPhoneNumberId(_configuration),
            WhatsAppTemplateName = clinic?.WhatsAppTemplateName ?? RemindersConfig.WhatsAppTemplateName(_configuration),
            WhatsAppTemplateLanguage = clinic?.WhatsAppTemplateLanguage ?? RemindersConfig.WhatsAppTemplateLanguage(_configuration),
            WhatsAppAccessToken = ResolveSecret(clinic?.WhatsAppAccessTokenEncrypted, RemindersConfig.WhatsAppAccessToken(_configuration), NotificationType.WhatsApp),
        };

        _resolveCache[cacheKey] = resolved;
        return resolved;
    }

    private Task<ClinicReminderSettings?> LoadAsync(Guid? clinicId, CancellationToken cancellationToken) =>
        clinicId.HasValue
            ? _repository.GetByClinicIdAsync(clinicId.Value, cancellationToken)
            : Task.FromResult<ClinicReminderSettings?>(null);

    // Per channel: the clinic's explicit toggle if set, otherwise whether the install enables the channel.
    private IReadOnlyList<NotificationType> ResolveEnabledChannels(ClinicReminderSettings? clinic)
    {
        var installChannels = RemindersConfig.Channels(_configuration);
        var smsEnabled = clinic?.SmsEnabled ?? installChannels.Contains(NotificationType.SMS);
        var whatsAppEnabled = clinic?.WhatsAppEnabled ?? installChannels.Contains(NotificationType.WhatsApp);

        var channels = new List<NotificationType>();
        if (smsEnabled)
        {
            channels.Add(NotificationType.SMS);
        }

        if (whatsAppEnabled)
        {
            channels.Add(NotificationType.WhatsApp);
        }

        return channels;
    }

    // A clinic that set its own secret uses it (decrypted); if that can't be decrypted the channel is treated
    // as not configured (null) — never falling back to the install secret (the clinic chose its own identity).
    // A clinic that did not set a secret inherits the per-install value.
    private string? ResolveSecret(string? clinicCiphertext, string? installSecret, NotificationType channel)
    {
        if (string.IsNullOrEmpty(clinicCiphertext))
        {
            return installSecret;
        }

        try
        {
            return _secretProtector.Unprotect(clinicCiphertext);
        }
        catch (Exception ex)
        {
            if (_decryptFailuresLogged.Add(channel))
            {
                _logger.LogError(
                    ex,
                    "Could not decrypt the per-clinic {Channel} reminder secret (key rotated/unavailable); treating the channel as not configured.",
                    channel);
            }

            return null;
        }
    }
}
