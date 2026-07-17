using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Config accessors for the SMS/WhatsApp appointment-reminder feature (the <c>Reminders</c> section).
/// Mirrors the <see cref="ConnectivityConfig"/> idiom: static accessors over <see cref="IConfiguration"/>
/// with baked-in <c>const</c> defaults, so the feature works with no <c>Reminders</c> section present.
///
/// Secrets (<c>Sms:ApiKey</c>, <c>WhatsApp:AccessToken</c>) are read the same way but are expected to come
/// from the environment (e.g. <c>Reminders__Sms__ApiKey</c>) / user-secrets, never committed appsettings.
/// </summary>
public static class RemindersConfig
{
    private static readonly int[] DefaultLeadTimesHours = { 24, 6 };
    private const int DefaultMinLeadHours = 1;
    private const int DefaultMaxRetries = 3;
    private const string DefaultWhatsAppTemplateLanguage = "fr";

    private const string SmsChannel = "Sms";
    private const string WhatsAppChannel = "WhatsApp";

    /// <summary>Enabled reminder channels, parsed to <see cref="NotificationType"/>. Empty = reminders off.</summary>
    public static IReadOnlyList<NotificationType> Channels(IConfiguration configuration)
    {
        var raw = configuration.GetSection("Reminders:Channels").Get<string[]>() ?? Array.Empty<string>();
        var channels = new List<NotificationType>();
        foreach (var value in raw)
        {
            if (string.Equals(value, SmsChannel, StringComparison.OrdinalIgnoreCase))
            {
                if (!channels.Contains(NotificationType.SMS))
                {
                    channels.Add(NotificationType.SMS);
                }
            }
            else if (string.Equals(value, WhatsAppChannel, StringComparison.OrdinalIgnoreCase))
            {
                if (!channels.Contains(NotificationType.WhatsApp))
                {
                    channels.Add(NotificationType.WhatsApp);
                }
            }
            // Unknown/unsupported channel values (e.g. "Email") are ignored — reminders are SMS/WhatsApp only.
        }

        return channels;
    }

    /// <summary>Preferred lead-time tiers (hours before the appointment). Largest still-future tier wins.</summary>
    public static IReadOnlyList<int> LeadTimesHours(IConfiguration configuration)
    {
        var raw = configuration.GetSection("Reminders:LeadTimesHours").Get<int[]>();
        return raw is { Length: > 0 } ? raw : DefaultLeadTimesHours;
    }

    /// <summary>Below this many hours before the appointment, no reminder is scheduled.</summary>
    public static int MinLeadHours(IConfiguration configuration) =>
        configuration.GetValue<int?>("Reminders:MinLeadHours") ?? DefaultMinLeadHours;

    /// <summary>Max transient send attempts before a reminder is marked <c>Failed</c>.</summary>
    public static int MaxRetries(IConfiguration configuration) =>
        configuration.GetValue<int?>("Reminders:MaxRetries") ?? DefaultMaxRetries;

    // SMS gateway (generic HTTP).
    public static string? SmsApiUrl(IConfiguration configuration) => configuration["Reminders:Sms:ApiUrl"];
    public static string? SmsSenderId(IConfiguration configuration) => configuration["Reminders:Sms:SenderId"];
    public static string? SmsApiKey(IConfiguration configuration) => configuration["Reminders:Sms:ApiKey"];

    // WhatsApp Business (Graph API).
    public static string? WhatsAppApiUrl(IConfiguration configuration) => configuration["Reminders:WhatsApp:ApiUrl"];
    public static string? WhatsAppPhoneNumberId(IConfiguration configuration) => configuration["Reminders:WhatsApp:PhoneNumberId"];
    public static string? WhatsAppTemplateName(IConfiguration configuration) => configuration["Reminders:WhatsApp:TemplateName"];
    public static string? WhatsAppAccessToken(IConfiguration configuration) => configuration["Reminders:WhatsApp:AccessToken"];

    public static string WhatsAppTemplateLanguage(IConfiguration configuration) =>
        configuration["Reminders:WhatsApp:TemplateLanguage"] ?? DefaultWhatsAppTemplateLanguage;
}
