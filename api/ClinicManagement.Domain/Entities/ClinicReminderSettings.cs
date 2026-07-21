using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A clinic's own SMS/WhatsApp reminder channel toggles + sender identity + (encrypted) credentials,
/// overriding the per-install <c>Reminders</c> config for that clinic. 1:1 with <see cref="Clinic"/> — the
/// entity <see cref="Common.Entity{TId}.Id"/> <b>is</b> the owning clinic id (shared primary key).
///
/// Channel toggles are <c>bool?</c>: <c>null</c> means "inherit the per-install default", <c>true</c>/<c>false</c>
/// an explicit override. Secret credentials are stored as Data-Protection ciphertext (<c>*Encrypted</c>) —
/// they are set write-only (only replaced when a new value is supplied) and never exposed in plaintext here.
/// </summary>
public class ClinicReminderSettings : Entity<Guid>
{
    public bool? SmsEnabled { get; private set; }
    public bool? WhatsAppEnabled { get; private set; }
    public string? SmsSenderId { get; private set; }
    public string? WhatsAppPhoneNumberId { get; private set; }
    public string? WhatsAppTemplateName { get; private set; }
    public string? WhatsAppTemplateLanguage { get; private set; }
    public string? SmsApiKeyEncrypted { get; private set; }
    public string? WhatsAppAccessTokenEncrypted { get; private set; }

    // WhatsApp Embedded-Signup connection metadata (Cloud onboarding). Populated by ApplyWhatsAppConnection
    // on a successful connect and reset by ClearWhatsAppConnection; the manual path leaves them at defaults.
    public string? WhatsAppBusinessAccountId { get; private set; }
    public Enums.WhatsAppConnectionStatus WhatsAppConnectionStatus { get; private set; }
    public string? WhatsAppLastError { get; private set; }
    public DateTime? WhatsAppConnectedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ClinicReminderSettings() { } // For EF Core

    public ClinicReminderSettings(Guid clinicId)
    {
        Id = clinicId;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces the non-secret settings (channel toggles + sender identity). Blank strings are normalized to
    /// <c>null</c> (= inherit the per-install value). Secrets are set separately, write-only.
    /// </summary>
    public void ApplyNonSecretSettings(
        bool? smsEnabled,
        bool? whatsAppEnabled,
        string? smsSenderId,
        string? whatsAppPhoneNumberId,
        string? whatsAppTemplateName,
        string? whatsAppTemplateLanguage)
    {
        SmsEnabled = smsEnabled;
        WhatsAppEnabled = whatsAppEnabled;
        SmsSenderId = Normalize(smsSenderId);
        WhatsAppPhoneNumberId = Normalize(whatsAppPhoneNumberId);
        WhatsAppTemplateName = Normalize(whatsAppTemplateName);
        WhatsAppTemplateLanguage = Normalize(whatsAppTemplateLanguage);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Stores a new (already-encrypted) SMS API key. Only call when the admin supplied a new value.</summary>
    public void SetSmsApiKeyEncrypted(string ciphertext)
    {
        SmsApiKeyEncrypted = ciphertext ?? throw new ArgumentNullException(nameof(ciphertext));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Stores a new (already-encrypted) WhatsApp access token. Only call when the admin supplied a new value.</summary>
    public void SetWhatsAppAccessTokenEncrypted(string ciphertext)
    {
        WhatsAppAccessTokenEncrypted = ciphertext ?? throw new ArgumentNullException(nameof(ciphertext));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a successful WhatsApp Embedded-Signup connection: stores the WABA id + phone-number id,
    /// enables the channel, marks the connection <see cref="Enums.WhatsAppConnectionStatus.Connected"/>,
    /// stamps the connect time and clears any prior error. The access token is stored separately (write-only)
    /// via <see cref="SetWhatsAppAccessTokenEncrypted"/>.
    /// </summary>
    public void ApplyWhatsAppConnection(string businessAccountId, string phoneNumberId)
    {
        WhatsAppBusinessAccountId = Normalize(businessAccountId);
        WhatsAppPhoneNumberId = Normalize(phoneNumberId);
        WhatsAppEnabled = true;
        WhatsAppConnectionStatus = Enums.WhatsAppConnectionStatus.Connected;
        WhatsAppLastError = null;
        WhatsAppConnectedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the WhatsApp connection: removes the stored WABA id, phone-number id and access token, disables
    /// the channel and resets the status to <see cref="Enums.WhatsAppConnectionStatus.NotConnected"/>.
    /// </summary>
    public void ClearWhatsAppConnection()
    {
        WhatsAppBusinessAccountId = null;
        WhatsAppPhoneNumberId = null;
        WhatsAppAccessTokenEncrypted = null;
        WhatsAppEnabled = false;
        WhatsAppConnectionStatus = Enums.WhatsAppConnectionStatus.NotConnected;
        WhatsAppConnectedAt = null;
        WhatsAppLastError = null;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
