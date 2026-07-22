using System.Diagnostics.CodeAnalysis;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// The effective reminder settings for one clinic: per-clinic overrides where set, else the per-install
/// <c>Reminders</c> config. Produced by <see cref="Interfaces.IReminderSettingsProvider"/> and consumed by the
/// channel senders — secrets here are already <b>decrypted</b> (in-process only; never serialized/logged).
/// A null secret means "not configured" for that channel (either genuinely unset, or a decryption failure).
/// Provider endpoint URLs stay per-install.
/// </summary>
public sealed record ResolvedReminderSettings
{
    public required IReadOnlyList<NotificationType> EnabledChannels { get; init; }

    public string? SmsApiUrl { get; init; }
    public string? SmsSenderId { get; init; }
    public string? SmsApiKey { get; init; }

    public string? WhatsAppApiUrl { get; init; }
    public string? WhatsAppPhoneNumberId { get; init; }
    public string? WhatsAppTemplateName { get; init; }
    public string? WhatsAppTemplateLanguage { get; init; }
    public string? WhatsAppAccessToken { get; init; }

    /// <summary>
    /// Whether the WhatsApp template carries a single body variable (<c>{{1}}</c>) that receives the rendered
    /// reminder text. True (default) for a proper reminder template; false for a parameter-less template
    /// (e.g. a canned <c>hello_world</c>) where the sender must omit the body component or Meta rejects it.
    /// </summary>
    public bool WhatsAppTemplateHasBodyParam { get; init; } = true;

    /// <summary>Effective lead-time tiers (hours before the appointment) — per-clinic override else per-install.</summary>
    public IReadOnlyList<int> LeadTimeHours { get; init; } = Array.Empty<int>();

    /// <summary>Custom reminder wording (per-clinic). Null ⇒ the built-in French default is used.</summary>
    public string? MessageTemplateBody { get; init; }

    /// <summary>
    /// Whether the SMS channel has everything it needs to actually send (gateway URL + sender id + API key).
    /// This is the single source of truth for "SMS is sendable" — the SMS sender and the admin effective-status
    /// surface both read it, so they never drift. The <see cref="MemberNotNullWhenAttribute"/> lets the sender
    /// dereference those fields without a null warning once this returns true.
    /// </summary>
    [MemberNotNullWhen(true, nameof(SmsApiUrl), nameof(SmsSenderId), nameof(SmsApiKey))]
    public bool SmsConfigured =>
        !string.IsNullOrWhiteSpace(SmsApiUrl) &&
        !string.IsNullOrWhiteSpace(SmsSenderId) &&
        !string.IsNullOrWhiteSpace(SmsApiKey);

    /// <summary>
    /// Whether the WhatsApp channel has everything it needs to actually send (Graph URL + phone-number id +
    /// template name + access token). Single source of truth for "WhatsApp is sendable".
    /// </summary>
    [MemberNotNullWhen(true, nameof(WhatsAppApiUrl), nameof(WhatsAppPhoneNumberId), nameof(WhatsAppTemplateName), nameof(WhatsAppAccessToken))]
    public bool WhatsAppConfigured =>
        !string.IsNullOrWhiteSpace(WhatsAppApiUrl) &&
        !string.IsNullOrWhiteSpace(WhatsAppPhoneNumberId) &&
        !string.IsNullOrWhiteSpace(WhatsAppTemplateName) &&
        !string.IsNullOrWhiteSpace(WhatsAppAccessToken);
}
