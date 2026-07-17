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
}
