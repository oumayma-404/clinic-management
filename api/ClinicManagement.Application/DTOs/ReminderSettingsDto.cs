namespace ClinicManagement.Application.DTOs;

/// <summary>Per-channel effective-status values returned by the reminder-settings GET (reliability-and-polish
/// AC-2). "configured" = the channel would actually send; "not_configured" = a warning (missing URL/secret/…)
/// even if a WhatsApp OAuth "connection" exists.</summary>
public static class ReminderEffectiveStatus
{
    public const string Configured = "configured";
    public const string NotConfigured = "not_configured";
}

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

    // Per-clinic overrides of previously per-install-only values (non-secret; reliability-and-polish AC-1).
    public string? SmsApiUrl { get; init; }
    public string? WhatsAppApiUrl { get; init; }
    public IReadOnlyList<int>? LeadTimeHours { get; init; }
    public string? MessageTemplateBody { get; init; }

    // Outbound email (SMTP) — the channel that delivers generated documents. Same shape as the two message
    // channels: non-secret values returned, the password only as a configured flag.
    public string? SmtpHost { get; init; }
    public int? SmtpPort { get; init; }
    public bool? SmtpUseTls { get; init; }
    public string? SmtpUsername { get; init; }
    public bool SmtpPasswordConfigured { get; init; }
    public string? SmtpFromAddress { get; init; }
    public string? SmtpFromName { get; init; }

    // Per-channel effective status (AC-2): whether the resolved settings + credentials make the channel
    // actually sendable. Values are ReminderEffectiveStatus.Configured / NotConfigured.
    public string SmsEffectiveStatus { get; init; } = ReminderEffectiveStatus.NotConfigured;
    public string WhatsAppEffectiveStatus { get; init; } = ReminderEffectiveStatus.NotConfigured;
    public string EmailEffectiveStatus { get; init; } = ReminderEffectiveStatus.NotConfigured;

    // WhatsApp Embedded-Signup connection metadata (read-only; token is never returned). Status is the
    // enum name ("NotConnected" | "Connected" | "Error") so the frontend badge reads a stable string.
    public string? WhatsAppBusinessAccountId { get; init; }
    public string WhatsAppConnectionStatus { get; init; } = nameof(Domain.Enums.WhatsAppConnectionStatus.NotConnected);
    public string? WhatsAppLastError { get; init; }
    public DateTime? WhatsAppConnectedAt { get; init; }

    /// <summary>
    /// AC-1.7 — <b>are this cabinet's WhatsApp credentials ours to provision rather than theirs to type?</b> True on a
    /// deployment that sells vendor messaging, where the three manual fields (endpoint, phone-number id, access token)
    /// are absent from the screen and refused by the handler.
    ///
    /// <para>⚠️ It travels on <i>this</i> DTO rather than being probed elsewhere, because the screen that hides the
    /// fields and the handler that refuses them are two halves of one rule — and this is the read that handler
    /// answers. A separate capability probe is a second source that can disagree, which would mean a form offering a
    /// field the save is about to reject.</para>
    /// </summary>
    public bool WhatsAppVendorManaged { get; init; }

    /// <summary>
    /// Round-tripped by the settings sheet so two tabs cannot silently overwrite each other's channel configuration.
    /// <c>0</c> means « not supplied » and skips the check — which is what it was doing for every save.
    /// </summary>
    public uint Version { get; init; }
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

    // Per-clinic overrides of previously per-install-only values (non-secret; reliability-and-polish AC-1).
    // Blank/empty ⇒ cleared (inherit the per-install value).
    public string? SmsApiUrl { get; init; }
    public string? WhatsAppApiUrl { get; init; }
    public IReadOnlyList<int>? LeadTimeHours { get; init; }
    public string? MessageTemplateBody { get; init; }

    // Outbound email (SMTP). Non-secret fields replace/clear as above; SmtpPassword is write-only like the two
    // other secrets — omitted/blank leaves the stored one untouched.
    public string? SmtpHost { get; init; }
    public int? SmtpPort { get; init; }
    public bool? SmtpUseTls { get; init; }
    public string? SmtpUsername { get; init; }
    public string? SmtpPassword { get; init; }
    public string? SmtpFromAddress { get; init; }
    public string? SmtpFromName { get; init; }

    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the admin was
    /// editing; <c>0</c> means « not supplied » and skips the check.
    /// </summary>
    public uint Version { get; init; }
}

/// <summary>
/// Payload the frontend posts after a successful Meta Embedded-Signup run. Carries the one-time OAuth
/// <see cref="Code"/> the backend exchanges for a business token, plus the WABA and phone-number ids the
/// SDK returned. Cloud-only onboarding — the Local install uses the manual credential path instead.
/// </summary>
public sealed record ConnectWhatsAppRequest
{
    public required string Code { get; init; }
    public required string WabaId { get; init; }
    public required string PhoneNumberId { get; init; }
}
