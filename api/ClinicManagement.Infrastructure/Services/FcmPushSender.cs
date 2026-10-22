using System.Net;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Android delivery through Firebase Cloud Messaging's HTTP v1 API.
///
/// <para>The payload is FCM's <c>notification</c> title plus a <c>data</c> block carrying the category and the
/// routing id — nothing else (AC-47). The <c>data</c> block is what the shell reads on a tap to open the right
/// screen (AC-48); the <c>notification</c> block is what the OS draws while the app is not running.</para>
///
/// <para>The <c>android</c> block picks the shell's channel (« Messages » or « Agenda », so a dentist can silence
/// one without the other) and a <c>tag</c>, so a second message in the same thread replaces the first banner
/// instead of stacking under it.</para>
/// </summary>
public sealed class FcmPushSender : HttpPushSender, IPushSender
{
    /// <summary>The shell's two notification channels — <c>mobile/android</c>'s <c>PushChannels</c> declares the same ids.</summary>
    public const string MessagesChannel = "messages";
    public const string AgendaChannel = "agenda";

    public FcmPushSender(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public DevicePlatform Platform => DevicePlatform.Android;

    public async Task<PushSendResult> SendAsync(
        PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (!credentials.IsConfigured)
        {
            return PushSendResult.NotConfigured;
        }

        FcmAccessToken.Outcome bearer;
        try
        {
            bearer = await FcmAccessToken.ResolveAsync(
                HttpClientFactory.CreateClient(), credentials.Secret!, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return PushSendResult.Transient($"Échec du jeton Firebase : {ex.Message}");
        }

        if (bearer.Bearer is null)
        {
            // A key Google refuses is the operator's to fix, so the row is parked rather than retried to death.
            return bearer.Misconfigured ? PushSendResult.NotConfigured : PushSendResult.Transient(bearer.Error ?? "Jeton Firebase indisponible");
        }

        // The endpoint is templated on the project id so an operator configures one URL for every clinic.
        var url = credentials.Endpoint!.Replace("{projectId}", credentials.Identity, StringComparison.Ordinal);
        var payload = new
        {
            message = new
            {
                token = message.Token,
                notification = new { title = message.Label },
                data = RoutingData(message),
                android = new
                {
                    priority = "HIGH",
                    notification = new { channel_id = ChannelFor(message.Category), tag = TagFor(message) }
                }
            }
        };

        return await PostJsonAsync(url, payload, bearer.Bearer, "FCM", extraHeaders: null, cancellationToken);
    }

    public static string ChannelFor(NotificationCategory category) =>
        category == NotificationCategory.StaffChatMessage ? MessagesChannel : AgendaChannel;

    private static string TagFor(PushMessage message) =>
        message.Conversation ?? message.AppointmentId?.ToString() ?? message.Category.ToString();

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

        if (!string.IsNullOrEmpty(message.Conversation))
        {
            data["conversation"] = message.Conversation;
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
