using System.Net;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Android delivery through Firebase Cloud Messaging's HTTP v1 API.
///
/// <para>The payload is FCM's <c>notification</c> title plus a <c>data</c> block carrying the category and the
/// routing id — nothing else (AC-47). The <c>data</c> block is what the shell reads on a tap to open the right
/// screen (AC-48); the <c>notification</c> block is what the OS draws while the app is not running.</para>
/// </summary>
public sealed class FcmPushSender : HttpPushSender, IPushSender
{
    public FcmPushSender(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public DevicePlatform Platform => DevicePlatform.Android;

    public Task<PushSendResult> SendAsync(
        PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (!credentials.IsConfigured)
        {
            return Task.FromResult(PushSendResult.NotConfigured);
        }

        // The endpoint is templated on the project id so an operator configures one URL for every clinic.
        var url = credentials.Endpoint!.Replace("{projectId}", credentials.Identity, StringComparison.Ordinal);

        var payload = new
        {
            message = new
            {
                token = message.Token,
                notification = new { title = message.Label },
                data = RoutingData(message)
            }
        };

        return PostJsonAsync(url, payload, credentials.Secret!, "FCM", extraHeaders: null, cancellationToken);
    }

    /// <summary>
    /// FCM requires every <c>data</c> value to be a string, so the id is formatted rather than left a GUID — and
    /// an absent id is omitted rather than sent as <c>""</c>, which the shell would have to special-case.
    /// </summary>
    private static Dictionary<string, string> RoutingData(PushMessage message)
    {
        var data = new Dictionary<string, string> { ["category"] = message.Category.ToString() };

        if (message.AppointmentId is Guid appointmentId)
        {
            data["appointmentId"] = appointmentId.ToString();
        }

        return data;
    }

    /// <summary>
    /// FCM reports a dead token as <c>UNREGISTERED</c> (404) or, for a malformed one, <c>INVALID_ARGUMENT</c>
    /// (400). Matched on the error <b>code in the body</b> and not on the status alone: a bare 400 also covers a
    /// payload this sender got wrong, which is a bug to fix rather than a device to deactivate.
    /// </summary>
    protected override bool IsTokenInvalid(HttpStatusCode status, string body) =>
        body.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase)
        || body.Contains("registration-token-not-registered", StringComparison.OrdinalIgnoreCase);
}
