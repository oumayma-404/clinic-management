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

    // Per-clinic overrides of values that used to be per-install-only (reliability-and-polish). null/blank
    // means "inherit the per-install Reminders config". Provider endpoint URLs and the reminder wording can
    // now be set by an admin without editing server config.
    public string? SmsApiUrl { get; private set; }
    public string? WhatsAppApiUrl { get; private set; }

    // Lead-time tiers (hours before the appointment) stored as a canonical CSV, e.g. "24,6". null = inherit.
    public string? LeadTimeHours { get; private set; }

    // Custom reminder wording. Supports the {patient}, {date} and {clinic} placeholders; null = inherit the
    // built-in French default.
    public string? MessageTemplateBody { get; private set; }

    // WhatsApp Embedded-Signup connection metadata (Cloud onboarding). Populated by ApplyWhatsAppConnection
    // on a successful connect and reset by ClearWhatsAppConnection; the manual path leaves them at defaults.
    public string? WhatsAppBusinessAccountId { get; private set; }
    public Enums.WhatsAppConnectionStatus WhatsAppConnectionStatus { get; private set; }
    public string? WhatsAppLastError { get; private set; }
    public DateTime? WhatsAppConnectedAt { get; private set; }

    // Outbound email (SMTP) — the channel that sends generated documents to a patient or a confrère. It lives
    // on this row rather than in a parallel settings aggregate because everything it needs already exists here:
    // per-clinic-else-per-install resolution, write-only encrypted secrets, and one admin screen. A separate
    // aggregate + provider + protector for one channel's four fields would be duplication, not separation.
    // null/blank = inherit the per-install Notification:Smtp config. Each clinic sends from its own address:
    // a document carries a practitioner's name, so a shared sender would misattribute it.
    public string? SmtpHost { get; private set; }
    public int? SmtpPort { get; private set; }
    public bool? SmtpUseTls { get; private set; }
    public string? SmtpUsername { get; private set; }
    public string? SmtpPasswordEncrypted { get; private set; }
    public string? SmtpFromAddress { get; private set; }
    public string? SmtpFromName { get; private set; }

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
        string? whatsAppTemplateLanguage,
        string? smsApiUrl,
        string? whatsAppApiUrl,
        IReadOnlyList<int>? leadTimeHours,
        string? messageTemplateBody,
        bool allowPrivateNetwork = false)
    {
        SmsEnabled = smsEnabled;
        WhatsAppEnabled = whatsAppEnabled;
        SmsSenderId = Normalize(smsSenderId);
        WhatsAppPhoneNumberId = Normalize(whatsAppPhoneNumberId);
        WhatsAppTemplateName = Normalize(whatsAppTemplateName);
        WhatsAppTemplateLanguage = Normalize(whatsAppTemplateLanguage);
        // The two endpoints a tenant can point wherever it likes. Validated here rather than in the handler so
        // every caller is covered — see OutboundEndpoint for why this is a security boundary and not tidiness.
        SmsApiUrl = OutboundEndpoint.ValidateUrl(
            smsApiUrl, "L'URL de la passerelle SMS", allowPrivateNetwork);
        WhatsAppApiUrl = OutboundEndpoint.ValidateUrl(
            whatsAppApiUrl, "L'URL de l'API WhatsApp", allowPrivateNetwork);
        LeadTimeHours = FormatLeadTimeHours(leadTimeHours);
        MessageTemplateBody = Normalize(messageTemplateBody);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Parses a stored lead-time CSV (e.g. <c>"24,6"</c>) into positive, de-duplicated hour tiers, preserving
    /// order. Returns an empty list for null/blank/unparseable input (= inherit the per-install tiers).
    /// </summary>
    public static IReadOnlyList<int> ParseLeadTimeHours(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<int>();
        }

        var hours = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var value) && value > 0 && !hours.Contains(value))
            {
                hours.Add(value);
            }
        }

        return hours;
    }

    /// <summary>
    /// Canonicalizes lead-time tiers to a stored CSV: positive, de-duplicated, order-preserving. Returns
    /// <c>null</c> (= inherit) when the input is null/empty or has no valid tier.
    /// </summary>
    public static string? FormatLeadTimeHours(IReadOnlyList<int>? hours)
    {
        if (hours == null || hours.Count == 0)
        {
            return null;
        }

        var canonical = new List<int>();
        foreach (var value in hours)
        {
            if (value > 0 && !canonical.Contains(value))
            {
                canonical.Add(value);
            }
        }

        return canonical.Count == 0 ? null : string.Join(",", canonical);
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
    /// Replaces the non-secret SMTP settings. Blank strings and a non-positive port normalize to <c>null</c>
    /// (= inherit the per-install value); the password is set separately, write-only.
    /// </summary>
    public void ApplySmtpSettings(
        string? smtpHost,
        int? smtpPort,
        bool? smtpUseTls,
        string? smtpUsername,
        string? smtpFromAddress,
        string? smtpFromName,
        bool allowPrivateNetwork = false)
    {
        SmtpHost = OutboundEndpoint.ValidateHost(
            smtpHost, "Le serveur SMTP", allowPrivateNetwork);
        SmtpPort = smtpPort is > 0 ? smtpPort : null;
        SmtpUseTls = smtpUseTls;
        SmtpUsername = Normalize(smtpUsername);
        SmtpFromAddress = Normalize(smtpFromAddress);
        SmtpFromName = Normalize(smtpFromName);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Stores a new (already-encrypted) SMTP password. Only call when the admin supplied a new value.</summary>
    public void SetSmtpPasswordEncrypted(string ciphertext)
    {
        SmtpPasswordEncrypted = ciphertext ?? throw new ArgumentNullException(nameof(ciphertext));
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
