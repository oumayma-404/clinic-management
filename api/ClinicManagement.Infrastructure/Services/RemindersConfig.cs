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

    /// <summary>
    /// How many due rows one dispatch tick may take (AC-P4.31), mirroring <c>TtnConfig.DispatchBatchSize</c>.
    /// The scan was unbounded: one large backlog could make a single tick run for minutes while holding the
    /// job's <c>[DisableConcurrentExecution]</c> lock, starving every later tick.
    /// </summary>
    private const int DefaultDispatchBatchSize = 50;

    /// <summary>
    /// Retention window in days for **terminal** outbox rows (AC-P4.32/4.33). Stated here rather than buried in
    /// the job so the default is discoverable. 90 days: long enough that the delivery-status card and any "did
    /// the patient get their reminder?" question can still be answered for a full quarter, short enough that the
    /// table stops growing forever — nothing has ever purged it.
    /// </summary>
    private const int DefaultRetentionDays = 90;

    /// <summary>
    /// How many due rows a <b>single clinic</b> may contribute to one dispatch tick (L3a). The scan had no
    /// clinic dimension, so on a shared install the practice with the oldest backlog owned every tick and
    /// nobody else's reminders ever went out. 20 against a batch of 50 means no clinic can take more than
    /// 40 % of a tick, while a single-clinic install is unaffected (that path is a flat batch).
    /// </summary>
    private const int DefaultPerClinicDispatchBound = 20;

    /// <summary>
    /// Clinic-local quiet hours: no reminder is sent from <see cref="DefaultQuietHoursStartLocal"/>:00 until
    /// <see cref="DefaultQuietHoursEndLocal"/>:00. Without this floor the tiered send-time calculation happily
    /// resolved to 02:00 for an 08:00 appointment booked ~22 h ahead — a message that wakes the patient is worse
    /// than no message, and it is the fastest way to have a channel blocked at the handset.
    /// </summary>
    private const int DefaultQuietHoursStartLocal = 21;
    private const int DefaultQuietHoursEndLocal = 8;

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

    /// <summary>Bounded dispatch batch (AC-P4.31). A non-positive override falls back to the default.</summary>
    public static int DispatchBatchSize(IConfiguration configuration) =>
        Positive(configuration.GetValue<int?>("Reminders:DispatchBatchSize"), DefaultDispatchBatchSize);

    /// <summary>Retention window for terminal rows (AC-P4.33). A non-positive override falls back.</summary>
    public static int RetentionDays(IConfiguration configuration) =>
        Positive(configuration.GetValue<int?>("Reminders:RetentionDays"), DefaultRetentionDays);

    /// <summary>Per-clinic share of one dispatch tick (L3a). A non-positive override falls back.</summary>
    public static int PerClinicDispatchBound(IConfiguration configuration) =>
        Positive(configuration.GetValue<int?>("Reminders:PerClinicDispatchBound"), DefaultPerClinicDispatchBound);

    /// <summary>
    /// The clinic-local quiet window as <c>(startHour, endHour)</c> — no sends at or after <c>start</c>, none
    /// before <c>end</c>. Equal values disable the floor entirely (« pas d'heures calmes »), which is the only
    /// way to turn it off; out-of-range values fall back rather than being clamped into a different window than
    /// the operator asked for.
    /// </summary>
    public static (int StartHour, int EndHour) QuietHoursLocal(IConfiguration configuration)
    {
        var start = Hour(configuration.GetValue<int?>("Reminders:QuietHoursStartLocal"), DefaultQuietHoursStartLocal);
        var end = Hour(configuration.GetValue<int?>("Reminders:QuietHoursEndLocal"), DefaultQuietHoursEndLocal);
        return (start, end);
    }

    private static int Hour(int? configured, int fallback) =>
        configured is >= 0 and <= 23 ? configured.Value : fallback;

    /// <summary>
    /// A zero or negative override would silently disable the feature (a zero batch dispatches nothing; a zero
    /// retention would purge everything), which is worse than ignoring a bad value — so fall back instead.
    /// </summary>
    private static int Positive(int? configured, int fallback) =>
        configured.HasValue && configured.Value > 0 ? configured.Value : fallback;

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

    /// <summary>Whether the WhatsApp template has a single body variable {{1}} (default true). Set false to
    /// use a parameter-less template (e.g. hello_world) — the sender then omits the body component.</summary>
    public static bool WhatsAppTemplateHasBodyParam(IConfiguration configuration) =>
        configuration.GetValue<bool?>("Reminders:WhatsApp:TemplateHasBodyParam") ?? true;
}
