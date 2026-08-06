using System.Net;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Shared HTTP plumbing for the push senders, on <see cref="HttpReminderChannelSender"/>'s pattern: serialize,
/// POST with a bearer token and a 15 s bound, map the response. Subclasses supply the platform, the URL, the
/// payload and — the one thing that is genuinely per-platform — how to recognise a dead token.
/// </summary>
public abstract class HttpPushSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);
    private const int MaxBodyLogLength = 200;

    private readonly IHttpClientFactory _httpClientFactory;

    protected HttpPushSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Does this refusal mean « this device is gone » rather than « try again later »? FCM answers
    /// <c>UNREGISTERED</c>, APNs <c>410 Gone</c> / <c>BadDeviceToken</c> — different enough that guessing one
    /// rule for both would silently classify half of them wrong.
    /// </summary>
    protected abstract bool IsTokenInvalid(HttpStatusCode status, string body);

    /// <summary>
    /// POSTs the payload. Extra headers exist for APNs, which carries its topic and priority as headers rather
    /// than in the body.
    /// </summary>
    protected async Task<PushSendResult> PostJsonAsync(
        string url,
        object payload,
        string bearerToken,
        string platformLabel,
        IReadOnlyDictionary<string, string>? extraHeaders,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(SendTimeout);

            var client = _httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

            if (extraHeaders != null)
            {
                foreach (var (name, value) in extraHeaders)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                return PushSendResult.Sent;
            }

            var body = await ReadTruncatedBodyAsync(response, timeoutCts.Token);
            var reason = $"{platformLabel} a répondu {(int)response.StatusCode} : {body}";

            return IsTokenInvalid(response.StatusCode, body)
                ? PushSendResult.TokenInvalid(reason)
                : PushSendResult.Transient(reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PushSendResult.Transient($"Délai dépassé lors de l'envoi {platformLabel}");
        }
        catch (Exception ex)
        {
            return PushSendResult.Transient($"Échec de l'envoi {platformLabel} : {ex.Message}");
        }
    }

    private static async Task<string> ReadTruncatedBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length > MaxBodyLogLength ? body[..MaxBodyLogLength] : body;
        }
        catch
        {
            return "(pas de corps de réponse)";
        }
    }
}
