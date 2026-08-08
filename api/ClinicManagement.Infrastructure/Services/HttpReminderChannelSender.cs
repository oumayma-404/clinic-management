using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Shared HTTP plumbing for reminder senders: serializes a JSON payload, POSTs it with a bearer token and
/// a bounded timeout, and maps the response to a <see cref="ReminderSendResult"/> (2xx → Sent, anything
/// else / exception → transient failure). Subclasses supply the channel, config check, endpoint and payload.
///
/// <para>
/// ⚠️ <b>The gateway's response body never reaches the returned result.</b> It used to, truncated to 200 bytes,
/// and that string is persisted on the outbox row and served back to the clinic by
/// <c>GET /api/clinics/reminder-status</c> and <c>reminder-log</c> — the latter readable by <i>any</i> clinic
/// role. Since the endpoint URL is itself tenant-supplied, that turned a settings field into a read primitive:
/// point it at an internal address and read back whatever answered. The body now goes to the log, where the
/// operator needs it, and only the status code is reported to the tenant.
/// </para>
/// </summary>
public abstract class HttpReminderChannelSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);
    private const int MaxBodyLogLength = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected HttpReminderChannelSender(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected async Task<ReminderSendResult> PostJsonAsync(
        string url, object payload, string bearerToken, string channelLabel, CancellationToken cancellationToken)
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

            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                return ReminderSendResult.Sent;
            }

            // Body to the log, status code to the tenant. See the type note: the URL is tenant-controlled, so
            // anything echoed back here is whatever the tenant chose to point this at.
            var body = await ReadTruncatedBodyAsync(response, timeoutCts.Token);
            _logger.LogWarning(
                "{Channel} gateway returned {StatusCode}. Response body: {Body}",
                channelLabel, (int)response.StatusCode, body);

            return ReminderSendResult.Transient($"{channelLabel} gateway returned {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReminderSendResult.Transient($"{channelLabel} send timed out");
        }
        catch (Exception ex)
        {
            // The exception message can carry the resolved host and socket detail of a tenant-chosen target;
            // same reasoning as the response body above.
            _logger.LogWarning(ex, "{Channel} send failed.", channelLabel);
            return ReminderSendResult.Transient($"{channelLabel} send failed");
        }
    }

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length > MaxBodyLogLength ? body[..MaxBodyLogLength] : body;
        }
        catch
        {
            return "(no response body)";
        }
    }
}
