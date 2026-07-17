using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Shared HTTP plumbing for reminder senders: serializes a JSON payload, POSTs it with a bearer token and
/// a bounded timeout, and maps the response to a <see cref="ReminderSendResult"/> (2xx → Sent, anything
/// else / exception → transient failure). Subclasses supply the channel, config check, endpoint and payload.
/// </summary>
public abstract class HttpReminderChannelSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);
    private const int MaxBodyLogLength = 200;

    private readonly IHttpClientFactory _httpClientFactory;

    protected HttpReminderChannelSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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

            var body = await ReadTruncatedBodyAsync(response, timeoutCts.Token);
            return ReminderSendResult.Transient($"{channelLabel} gateway returned {(int)response.StatusCode}: {body}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReminderSendResult.Transient($"{channelLabel} send timed out");
        }
        catch (Exception ex)
        {
            return ReminderSendResult.Transient($"{channelLabel} send failed: {ex.Message}");
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
