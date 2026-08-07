using System.Net;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// iOS delivery through the Apple Push Notification service.
///
/// <para>APNs addresses the device in the <b>URL</b> and carries its topic and priority as <b>headers</b>, which
/// is why the shared base takes an extra-headers argument. The body is the <c>aps</c> alert — the fixed French
/// category phrase — plus the same routing keys the Android payload carries, so a tap resolves identically on
/// both platforms (AC-47, AC-48).</para>
/// </summary>
public sealed class ApnsPushSender : HttpPushSender, IPushSender
{
    public ApnsPushSender(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public DevicePlatform Platform => DevicePlatform.Ios;

    public Task<PushSendResult> SendAsync(
        PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (!credentials.IsConfigured)
        {
            return Task.FromResult(PushSendResult.NotConfigured);
        }

        var url = credentials.Endpoint!.Replace("{token}", message.Token, StringComparison.Ordinal);

        var payload = new
        {
            aps = new { alert = new { title = message.Label }, sound = "default" },
            category = message.Category.ToString(),
            appointmentId = message.AppointmentId?.ToString()
        };

        var headers = new Dictionary<string, string>
        {
            // The bundle id. APNs refuses a mismatch outright, which is why it is a configured value rather
            // than a constant here — Part 8 is what settles it, and it cannot be changed after first submission.
            ["apns-topic"] = credentials.Identity!,
            ["apns-push-type"] = "alert",
            // 10 = deliver immediately. A reminder about a visit in the next hour is not worth power-saving.
            ["apns-priority"] = "10"
        };

        return PostJsonAsync(url, payload, credentials.Secret!, "APNs", headers, cancellationToken);
    }

    /// <summary>
    /// APNs is unambiguous here: <c>410 Gone</c> means the token is no longer valid for this topic, and
    /// <c>BadDeviceToken</c> arrives as a 400 reason string. Both are per-device terminal.
    /// </summary>
    protected override bool IsTokenInvalid(HttpStatusCode status, string body) =>
        status == HttpStatusCode.Gone
        || body.Contains("BadDeviceToken", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Unregistered", StringComparison.OrdinalIgnoreCase);
}
