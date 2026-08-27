using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Per-install SMTP settings for outbound document emails, read from the <c>Notification:Smtp</c> section —
/// the keys that had been sitting in <c>appsettings.json</c> unused since the original email
/// <c>NotificationService</c> was removed as dead code. A clinic's own <c>ClinicReminderSettings</c> overrides
/// each of these; these are the fallback (and the only source for a single-clinic install that never opens the
/// settings screen).
/// <para>
/// Deliberately its own accessor class rather than more members on <see cref="RemindersConfig"/>: that one owns
/// the <c>Reminders</c> section, and a class that silently reads two config sections is how a key ends up looked
/// up under the wrong prefix.
/// </para>
/// ⚠️ <c>Notification:Smtp:Password</c> is a secret — supply it via env (<c>Notification__Smtp__Password</c>) or
/// user-secrets, never committed config.
/// </summary>
public static class SmtpConfig
{
    private const int DefaultPort = 587;
    private const bool DefaultUseTls = true;

    public static string? Host(IConfiguration configuration) => Trimmed(configuration["Notification:Smtp:Server"]);

    /// <summary>The submission port. A non-positive or missing value falls back to 587 (STARTTLS submission).</summary>
    public static int Port(IConfiguration configuration)
    {
        var configured = configuration.GetValue<int?>("Notification:Smtp:Port");
        return configured is > 0 ? configured.Value : DefaultPort;
    }

    public static bool UseTls(IConfiguration configuration) =>
        configuration.GetValue<bool?>("Notification:Smtp:UseTls") ?? DefaultUseTls;

    public static string? Username(IConfiguration configuration) => Trimmed(configuration["Notification:Smtp:Username"]);

    public static string? Password(IConfiguration configuration) => Trimmed(configuration["Notification:Smtp:Password"]);

    /// <summary>
    /// The envelope sender. Falls back to the username when it is itself an address — a mailbox provider's
    /// username usually <i>is</i> the address, and requiring both would leave the channel unconfigured for the
    /// commonest setup.
    /// </summary>
    public static string? FromAddress(IConfiguration configuration)
    {
        var configured = Trimmed(configuration["Notification:Smtp:FromAddress"]);
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var username = Username(configuration);
        return username != null && username.Contains('@') ? username : null;
    }

    public static string? FromName(IConfiguration configuration) => Trimmed(configuration["Notification:Smtp:FromName"]);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
