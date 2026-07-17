using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>Maps <see cref="ClinicReminderSettings"/> to its secret-masked DTO (secrets → configured flags only).</summary>
public static class ReminderSettingsMappings
{
    /// <summary>
    /// A clinic with no settings row (<paramref name="settings"/> null) maps to an all-inherit DTO
    /// (null toggles, no identity, both secrets not configured) — GET works even before anything is saved.
    /// </summary>
    public static ReminderSettingsDto ToDto(this ClinicReminderSettings? settings) => new()
    {
        SmsEnabled = settings?.SmsEnabled,
        WhatsAppEnabled = settings?.WhatsAppEnabled,
        SmsSenderId = settings?.SmsSenderId,
        WhatsAppPhoneNumberId = settings?.WhatsAppPhoneNumberId,
        WhatsAppTemplateName = settings?.WhatsAppTemplateName,
        WhatsAppTemplateLanguage = settings?.WhatsAppTemplateLanguage,
        SmsApiKeyConfigured = !string.IsNullOrEmpty(settings?.SmsApiKeyEncrypted),
        WhatsAppAccessTokenConfigured = !string.IsNullOrEmpty(settings?.WhatsAppAccessTokenEncrypted),
    };
}
