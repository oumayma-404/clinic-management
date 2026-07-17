namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A clinic's reminder settings as returned to the admin (secret-masked). Channel toggles are nullable
/// (null = inherit the per-install default). Secret values are never returned — only a per-secret
/// configured/not-configured flag.
/// </summary>
public sealed record ReminderSettingsDto
{
    public bool? SmsEnabled { get; init; }
    public bool? WhatsAppEnabled { get; init; }
    public string? SmsSenderId { get; init; }
    public string? WhatsAppPhoneNumberId { get; init; }
    public string? WhatsAppTemplateName { get; init; }
    public string? WhatsAppTemplateLanguage { get; init; }
    public bool SmsApiKeyConfigured { get; init; }
    public bool WhatsAppAccessTokenConfigured { get; init; }
}

/// <summary>
/// Admin PUT payload for a clinic's reminder settings. Non-secret fields replace the stored values
/// (blank ⇒ cleared/inherit). Secrets (<see cref="SmsApiKey"/>, <see cref="WhatsAppAccessToken"/>) are
/// <b>write-only</b>: omitted/blank ⇒ the stored secret is left unchanged; a value ⇒ re-encrypted &amp; replaced.
/// </summary>
public sealed record UpdateReminderSettingsRequest
{
    public bool? SmsEnabled { get; init; }
    public bool? WhatsAppEnabled { get; init; }
    public string? SmsSenderId { get; init; }
    public string? WhatsAppPhoneNumberId { get; init; }
    public string? WhatsAppTemplateName { get; init; }
    public string? WhatsAppTemplateLanguage { get; init; }
    public string? SmsApiKey { get; init; }
    public string? WhatsAppAccessToken { get; init; }
}
