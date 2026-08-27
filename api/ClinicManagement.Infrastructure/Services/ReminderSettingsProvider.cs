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
/// <see cref="RemindersConfig"/>. Channel toggles are <c>bool?</c> (null = inherit the install default); secrets
/// are decrypted in-process.
///
/// <para>
/// ⚠️ <b>Resolution is per CHANNEL, not per field.</b> A clinic that supplies any of a channel's endpoint,
/// identity or secret owns that whole channel and inherits nothing further for it. Per-field coalescing let a
/// tenant pair its own endpoint with the install's credential, which the dispatcher then handed to that endpoint
/// — see <c>ClaimsItsOwnSms</c> for the full reasoning. Only wording and transport details (template name and
/// language, SMTP port, TLS flag, display name) still inherit: they carry no credential and address no host.
/// </para>
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

        // ⚠️ Each channel resolves as an ATOMIC UNIT — see ClaimsItsOwn* below. Resolving the endpoint and the
        // secret independently is how the install's credential ended up being offered to a tenant-chosen host.
        var clinicOwnsSms = ClaimsItsOwnSms(clinic);
        var clinicOwnsWhatsApp = ClaimsItsOwnWhatsApp(clinic);
        var clinicOwnsSmtp = ClaimsItsOwnSmtp(clinic);

        var resolved = new ResolvedReminderSettings
        {
            EnabledChannels = ResolveEnabledChannels(clinic),

            // Endpoint URLs are a per-clinic override (else the per-install value) so an admin can turn a
            // channel fully on without a server-config edit (reliability-and-polish AC-1) — but a clinic that
            // names its own endpoint gets NO install fall-back for that channel's identity or secret.
            SmsApiUrl = clinicOwnsSms ? clinic?.SmsApiUrl : RemindersConfig.SmsApiUrl(_configuration),
            SmsSenderId = clinicOwnsSms ? clinic?.SmsSenderId : RemindersConfig.SmsSenderId(_configuration),
            SmsApiKey = clinicOwnsSms
                ? DecryptOwn(clinic?.SmsApiKeyEncrypted, NotificationType.SMS)
                : RemindersConfig.SmsApiKey(_configuration),

            WhatsAppApiUrl = clinicOwnsWhatsApp ? clinic?.WhatsAppApiUrl : RemindersConfig.WhatsAppApiUrl(_configuration),
            WhatsAppPhoneNumberId = clinicOwnsWhatsApp
                ? clinic?.WhatsAppPhoneNumberId
                : RemindersConfig.WhatsAppPhoneNumberId(_configuration),
            WhatsAppAccessToken = clinicOwnsWhatsApp
                ? DecryptOwn(clinic?.WhatsAppAccessTokenEncrypted, NotificationType.WhatsApp)
                : RemindersConfig.WhatsAppAccessToken(_configuration),
            // Template name/language and the body-param flag are wording, not identity: they carry no credential
            // and address no host, so they keep inheriting.
            WhatsAppTemplateName = clinic?.WhatsAppTemplateName ?? RemindersConfig.WhatsAppTemplateName(_configuration),
            WhatsAppTemplateLanguage = clinic?.WhatsAppTemplateLanguage ?? RemindersConfig.WhatsAppTemplateLanguage(_configuration),
            WhatsAppTemplateHasBodyParam = RemindersConfig.WhatsAppTemplateHasBodyParam(_configuration),

            LeadTimeHours = ResolveLeadTimeHours(clinic),
            MessageTemplateBody = clinic?.MessageTemplateBody,

            // Outbound email (SMTP) — same atomic rule. A clinic naming its own SmtpHost must supply its own
            // credentials: offering the install's SMTP username and password to a tenant-chosen host is exactly
            // the leak this shape closes.
            SmtpHost = clinicOwnsSmtp ? clinic?.SmtpHost : SmtpConfig.Host(_configuration),
            SmtpUsername = clinicOwnsSmtp ? clinic?.SmtpUsername : SmtpConfig.Username(_configuration),
            SmtpPassword = clinicOwnsSmtp
                ? DecryptOwn(clinic?.SmtpPasswordEncrypted, NotificationType.Email)
                : SmtpConfig.Password(_configuration),
            SmtpFromAddress = clinicOwnsSmtp ? clinic?.SmtpFromAddress : SmtpConfig.FromAddress(_configuration),
            // Transport details and the display name carry no credential and name no host, so they inherit.
            SmtpPort = clinic?.SmtpPort ?? SmtpConfig.Port(_configuration),
            SmtpUseTls = clinic?.SmtpUseTls ?? SmtpConfig.UseTls(_configuration),
            SmtpFromName = clinic?.SmtpFromName ?? SmtpConfig.FromName(_configuration),
        };

        _resolveCache[cacheKey] = resolved;
        return resolved;
    }

    // Per-clinic lead-time tiers where the clinic set them, else the per-install Reminders:LeadTimesHours.
    private IReadOnlyList<int> ResolveLeadTimeHours(ClinicReminderSettings? clinic)
    {
        var perClinic = ClinicReminderSettings.ParseLeadTimeHours(clinic?.LeadTimeHours);
        return perClinic.Count > 0 ? perClinic : RemindersConfig.LeadTimesHours(_configuration);
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

    /// <summary>
    /// Whether the clinic claims a channel as <b>its own</b> — i.e. it supplied any of that channel's endpoint,
    /// identity or secret.
    ///
    /// <para>
    /// ⚠️ <b>This is the security boundary, not a convenience.</b> Resolution used to coalesce per field, so a
    /// clinic could supply only the URL and inherit the <i>install's</i> credential — which the dispatcher then
    /// sent to the clinic's host as a bearer token or SMTP AUTH. On a multi-tenant backend, where any signer-up
    /// is an admin of their own clinic, that is remote theft of an install-wide secret. Claiming any part of a
    /// channel therefore means owning all of it: no install fall-back for its endpoint, identity or secret.
    /// </para>
    ///
    /// <para>
    /// A clinic that claims a channel but leaves its secret empty (or whose ciphertext no longer decrypts) gets a
    /// <c>null</c> secret, which makes the channel <i>not configured</i> and parks the row. That is the correct
    /// direction: refusing to send is recoverable, leaking the operator's credential is not.
    /// </para>
    /// </summary>
    private static bool ClaimsItsOwnSms(ClinicReminderSettings? clinic) =>
        Provided(clinic?.SmsApiUrl) || Provided(clinic?.SmsSenderId) || Provided(clinic?.SmsApiKeyEncrypted);

    /// <inheritdoc cref="ClaimsItsOwnSms"/>
    private static bool ClaimsItsOwnWhatsApp(ClinicReminderSettings? clinic) =>
        Provided(clinic?.WhatsAppApiUrl)
        || Provided(clinic?.WhatsAppPhoneNumberId)
        || Provided(clinic?.WhatsAppAccessTokenEncrypted);

    /// <inheritdoc cref="ClaimsItsOwnSms"/>
    private static bool ClaimsItsOwnSmtp(ClinicReminderSettings? clinic) =>
        Provided(clinic?.SmtpHost)
        || Provided(clinic?.SmtpUsername)
        || Provided(clinic?.SmtpPasswordEncrypted)
        || Provided(clinic?.SmtpFromAddress);

    private static bool Provided(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Decrypts a secret the clinic owns. Never falls back to the install value — the caller has already
    /// established that this channel belongs to the clinic, so an install secret here would be the very leak
    /// <see cref="ClaimsItsOwnSms"/> exists to prevent. An undecryptable ciphertext (rotated/unavailable key)
    /// resolves to null and parks the channel; logged once per scope, never thrown.
    /// </summary>
    private string? DecryptOwn(string? clinicCiphertext, NotificationType channel)
    {
        if (string.IsNullOrEmpty(clinicCiphertext))
        {
            return null;
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
