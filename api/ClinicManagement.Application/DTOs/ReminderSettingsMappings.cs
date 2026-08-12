using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>Maps <see cref="ClinicReminderSettings"/> to its secret-masked DTO (secrets → configured flags only).</summary>
public static class ReminderSettingsMappings
{
    /// <summary>
    /// A clinic with no settings row (<paramref name="settings"/> null) maps to an all-inherit DTO
    /// (null toggles, no identity, both secrets not configured) — GET works even before anything is saved.
    /// </summary>
    /// <param name="vendorManagedWhatsApp">
    /// AC-1.7's flag. <b>Required rather than defaulted</b>, deliberately: four handlers produce this DTO — the read,
    /// the settings save, the connect and the disconnect — and a screen that hid the manual fields on load and got
    /// them back after connecting would be worse than never hiding them. A default would let one caller forget in
    /// silence; a parameter makes the compiler list them.
    /// </param>
    public static ReminderSettingsDto ToDto(this ClinicReminderSettings? settings, bool vendorManagedWhatsApp)
    {
        var leadTimes = ClinicReminderSettings.ParseLeadTimeHours(settings?.LeadTimeHours);
        return new()
        {
            SmsEnabled = settings?.SmsEnabled,
            WhatsAppEnabled = settings?.WhatsAppEnabled,
            SmsSenderId = settings?.SmsSenderId,
            WhatsAppPhoneNumberId = settings?.WhatsAppPhoneNumberId,
            WhatsAppTemplateName = settings?.WhatsAppTemplateName,
            WhatsAppTemplateLanguage = settings?.WhatsAppTemplateLanguage,
            SmsApiKeyConfigured = !string.IsNullOrEmpty(settings?.SmsApiKeyEncrypted),
            WhatsAppAccessTokenConfigured = !string.IsNullOrEmpty(settings?.WhatsAppAccessTokenEncrypted),
            SmsApiUrl = settings?.SmsApiUrl,
            WhatsAppApiUrl = settings?.WhatsAppApiUrl,
            LeadTimeHours = leadTimes.Count > 0 ? leadTimes : null,
            MessageTemplateBody = settings?.MessageTemplateBody,
            WhatsAppBusinessAccountId = settings?.WhatsAppBusinessAccountId,
            WhatsAppConnectionStatus =
                (settings?.WhatsAppConnectionStatus ?? Domain.Enums.WhatsAppConnectionStatus.NotConnected).ToString(),
            WhatsAppLastError = settings?.WhatsAppLastError,
            WhatsAppConnectedAt = settings?.WhatsAppConnectedAt,
            WhatsAppVendorManaged = vendorManagedWhatsApp,
            SmtpHost = settings?.SmtpHost,
            SmtpPort = settings?.SmtpPort,
            SmtpUseTls = settings?.SmtpUseTls,
            SmtpUsername = settings?.SmtpUsername,
            SmtpPasswordConfigured = !string.IsNullOrEmpty(settings?.SmtpPasswordEncrypted),
            SmtpFromAddress = settings?.SmtpFromAddress,
            SmtpFromName = settings?.SmtpFromName,
        };
    }
}
