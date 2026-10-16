using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Web Push delivery (staff-chat Part 8): a browser, or an iPhone home-screen app, through its vendor's push service.
///
/// <para>The payload is what <see cref="FcmPushSender"/> sends and nothing more — the fixed French label, the category
/// and the routing ids (AC-47) — encrypted to the browser, so even the push service cannot read the routing.</para>
///
/// <para>⚠️ <b>The endpoint is re-checked here</b>, not only at registration: the dispatcher is the code that actually
/// opens the connection, and a row written before the allow-list existed must not become a way around it.</para>
/// </summary>
public sealed class WebPushSender : HttpPushSender, IPushSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long a push service keeps an undelivered message — a day, past which « Nouveau message » is stale.</summary>
    private const int TimeToLiveSeconds = 86400;

    public WebPushSender(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public DevicePlatform Platform => DevicePlatform.WebPush;

    public async Task<PushSendResult> SendAsync(
        PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (!credentials.IsConfigured)
        {
            return PushSendResult.NotConfigured;
        }

        var browserKey = WebPushSubscription.FromBase64Url(message.WebPushP256dh);
        var authSecret = WebPushSubscription.FromBase64Url(message.WebPushAuth);
        if (WebPushSubscription.Refusal(message.Token, message.WebPushP256dh, message.WebPushAuth) is { } refusal
            || browserKey is null || authSecret is null)
        {
            // Nothing about this subscription can ever be delivered to: terminal for the device, like an expired token.
            return PushSendResult.TokenInvalid("Abonnement aux notifications inutilisable");
        }

        string authorization;
        byte[] body;
        try
        {
            var endpoint = new Uri(message.Token);
            authorization = WebPushCrypto.VapidAuthorization(
                endpoint.GetLeftPart(UriPartial.Authority), credentials.Endpoint!, credentials.Identity!, credentials.Secret!,
                DateTimeOffset.UtcNow);
            body = WebPushCrypto.Encrypt(Payload(message), browserKey, authSecret);
        }
        catch (CryptographicException)
        {
            // The VAPID keys are the operator's to fix: park the row rather than retry it to death.
            return PushSendResult.NotConfigured;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, message.Token) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("TTL", TimeToLiveSeconds.ToString());
            request.Headers.TryAddWithoutValidation("Urgency", "high");
            // One pending message per thread at the push service: a phone offline for an hour wakes to one banner, not ten.
            request.Headers.TryAddWithoutValidation("Topic", Topic(message));

            using var response = await HttpClientFactory.CreateClient().SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return PushSendResult.Sent;
            }

            var reason = $"Web Push a répondu {(int)response.StatusCode}";
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Gone => PushSendResult.TokenInvalid(reason),
                // A VAPID key the service rejects: nothing a retry changes.
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PushSendResult.NotConfigured,
                _ => PushSendResult.Transient(reason)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PushSendResult.Transient("Délai dépassé lors de l'envoi Web Push");
        }
        catch (HttpRequestException ex)
        {
            return PushSendResult.Transient($"Échec de l'envoi Web Push : {ex.Message}");
        }
    }

    /// <summary>The routing a tap needs and the phrase the lock screen shows — the page's service worker reads it.</summary>
    public static byte[] Payload(PushMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string?>
        {
            ["title"] = message.Label,
            ["category"] = message.Category.ToString(),
            ["tag"] = Tag(message),
            ["conversation"] = message.Conversation,
            ["appointmentId"] = message.AppointmentId?.ToString()
        }.Where(entry => entry.Value != null).ToDictionary(entry => entry.Key, entry => entry.Value));

    public static string Tag(PushMessage message) =>
        message.Conversation ?? message.AppointmentId?.ToString() ?? message.Category.ToString();

    // RFC 8030 § 5.4: at most 32 characters of the base64url alphabet.
    private static string Topic(PushMessage message) =>
        WebPushSubscription.ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(Tag(message))))[..32];

    protected override bool IsTokenInvalid(HttpStatusCode status, string body) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Gone;
}
