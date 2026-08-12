using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The Graph API side of FR-7: <c>POST /{wabaId}/message_templates</c> to submit the French utility reminder
/// template, and <c>GET</c> to read one back for the reconciling poll. Endpoint and version come from
/// <see cref="MetaConfig"/>, the token from the cabinet's own stored connection.
///
/// <para>⚠️ <b>Every failure is logged and returns null</b> — see <see cref="IWhatsAppTemplateService"/> for why
/// this seam cannot throw. The one shape that is <i>not</i> a failure is « a template with that name already
/// exists », which is what a cabinet reconnecting produces; that falls through to the read.</para>
/// </summary>
public class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppTemplateService> _logger;

    public WhatsAppTemplateService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WhatsAppTemplateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<WhatsAppTemplateState?> SubmitReminderTemplateAsync(
        string wabaId, string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{MetaConfig.GraphBaseUrl(_configuration)}/{Uri.EscapeDataString(wabaId)}/message_templates";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                name = WhatsAppReminderTemplate.Name,
                language = WhatsAppReminderTemplate.Language,
                category = WhatsAppReminderTemplate.Category,
                components = new object[]
                {
                    new
                    {
                        type = "BODY",
                        text = WhatsAppReminderTemplate.Body,
                        example = new { body_text = new[] { new[] { WhatsAppReminderTemplate.BodyExample } } },
                    },
                },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var body = await SendAsync(request, cancellationToken);
        if (body is null)
        {
            // The commonest cause on a reconnection is « name already exists ». Reading it back is both the
            // recovery and the correct answer, since that existing template is the one this cabinet will send with.
            return await ReadReminderTemplateAsync(wabaId, accessToken, templateId: null, cancellationToken);
        }

        var submitted = ReadSubmissionResponse(body);
        if (submitted is not null)
        {
            return submitted;
        }

        // Accepted, but the response did not carry a status we could read. The template exists, so ask for it.
        return await ReadReminderTemplateAsync(wabaId, accessToken, templateId: null, cancellationToken);
    }

    public async Task<WhatsAppTemplateState?> ReadReminderTemplateAsync(
        string wabaId, string accessToken, string? templateId, CancellationToken cancellationToken = default)
    {
        // By id where we have one — a name is unique per WABA but a rename is representable and an id is not.
        var url = string.IsNullOrWhiteSpace(templateId)
            ? $"{MetaConfig.GraphBaseUrl(_configuration)}/{Uri.EscapeDataString(wabaId)}/message_templates"
              + $"?name={Uri.EscapeDataString(WhatsAppReminderTemplate.Name)}&fields=id,name,status,category"
            : $"{MetaConfig.GraphBaseUrl(_configuration)}/{Uri.EscapeDataString(templateId)}"
              + "?fields=id,name,status,category";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var body = await SendAsync(request, cancellationToken);
        return body is null ? null : ReadTemplateResponse(body);
    }

    /// <summary>The response body on success, or null on any non-2xx, timeout or transport failure.</summary>
    private async Task<string?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            // The body goes to the log and nowhere else — WhatsAppSender's own rule (D-8): nothing Meta returns is
            // ever surfaced to a tenant.
            _logger.LogWarning(
                "WhatsApp template call to {Uri} failed ({Status}). Response body: {Body}",
                request.RequestUri, (int)response.StatusCode, body);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WhatsApp template call to {Uri} timed out.", request.RequestUri);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp template call to {Uri} failed.", request.RequestUri);
            return null;
        }
    }

    /// <summary>A submission answers with the created template's own id, status and granted category, flat.</summary>
    private static WhatsAppTemplateState? ReadSubmissionResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadState(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A read answers either with the template itself or with a <c>data</c> array holding it.</summary>
    private static WhatsAppTemplateState? ReadTemplateResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in data.EnumerateArray())
                {
                    // A WABA can hold the same name in several languages; ours is the French one.
                    if (Text(element, "name") == WhatsAppReminderTemplate.Name
                        && ReadState(element) is { } state)
                    {
                        return state;
                    }
                }

                return null;
            }

            return ReadState(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WhatsAppTemplateState? ReadState(JsonElement element) =>
        WhatsAppTemplateStatuses.Parse(Text(element, "status")) is { } status
            ? new WhatsAppTemplateState(status, Text(element, "category"), Text(element, "id"))
            : null;

    private static string? Text(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
