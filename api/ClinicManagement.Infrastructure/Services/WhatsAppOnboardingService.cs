using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// WhatsApp Business (Graph API) onboarding for Meta Embedded Signup (Cloud). Uses the platform app id/secret
/// (<see cref="MetaConfig"/>) for the code→token exchange and the returned business token for the app-subscribe
/// and phone-register steps. Every failure is thrown as a categorized <see cref="WhatsAppOnboardingException"/>
/// so the command handler can keep the connect flow atomic and surface a specific message.
/// </summary>
public class WhatsAppOnboardingService : IWhatsAppOnboardingService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppOnboardingService> _logger;

    public WhatsAppOnboardingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WhatsAppOnboardingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
    {
        var appId = MetaConfig.AppId(_configuration);
        var appSecret = MetaConfig.AppSecret(_configuration);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            _logger.LogWarning("Meta app credentials are not configured; cannot exchange the Embedded-Signup code.");
            throw new WhatsAppOnboardingException(
                WhatsAppOnboardingError.CodeExchangeFailed, "Meta app credentials are not configured.");
        }

        var url = $"{MetaConfig.GraphBaseUrl(_configuration)}/oauth/access_token" +
                  $"?client_id={Uri.EscapeDataString(appId)}" +
                  $"&client_secret={Uri.EscapeDataString(appSecret)}" +
                  $"&code={Uri.EscapeDataString(code)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var body = await SendAsync(request, WhatsAppOnboardingError.CodeExchangeFailed, cancellationToken);

        var token = ReadStringProperty(body, "access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Meta code exchange returned no access token.");
            throw new WhatsAppOnboardingException(
                WhatsAppOnboardingError.CodeExchangeFailed, "Meta code exchange returned no access token.");
        }

        return token;
    }

    public async Task SubscribeAppAsync(string wabaId, string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{MetaConfig.GraphBaseUrl(_configuration)}/{Uri.EscapeDataString(wabaId)}/subscribed_apps";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await SendAsync(request, WhatsAppOnboardingError.WabaNotEligible, cancellationToken);
    }

    public async Task RegisterPhoneAsync(string phoneNumberId, string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{MetaConfig.GraphBaseUrl(_configuration)}/{Uri.EscapeDataString(phoneNumberId)}/register";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { messaging_product = "whatsapp", pin = GenerateRegistrationPin() })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await SendAsync(request, WhatsAppOnboardingError.NumberAlreadyRegistered, cancellationToken);
    }

    public async Task UnsubscribeAppAsync(string wabaId, string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{MetaConfig.GraphBaseUrl(_configuration)}/{Uri.EscapeDataString(wabaId)}/subscribed_apps";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await SendAsync(request, WhatsAppOnboardingError.Unknown, cancellationToken);
    }

    /// <summary>
    /// Sends the request with a bounded timeout and returns the response body on success. On a non-success
    /// status the Graph error body is classified (falling back to <paramref name="defaultError"/> for the
    /// step) and thrown as a <see cref="WhatsAppOnboardingException"/>; network/timeout failures throw with
    /// the same default category.
    /// </summary>
    private async Task<string> SendAsync(
        HttpRequestMessage request, WhatsAppOnboardingError defaultError, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var category = ClassifyGraphError(body, defaultError);
                _logger.LogWarning(
                    "WhatsApp onboarding call to {Uri} failed ({Status}); classified as {Category}.",
                    request.RequestUri, (int)response.StatusCode, category);
                throw new WhatsAppOnboardingException(category, $"Meta returned {(int)response.StatusCode}.");
            }

            return body;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WhatsApp onboarding call to {Uri} timed out.", request.RequestUri);
            throw new WhatsAppOnboardingException(defaultError, "The Meta request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "WhatsApp onboarding call to {Uri} failed to reach Meta.", request.RequestUri);
            throw new WhatsAppOnboardingException(defaultError, "Could not reach Meta.");
        }
    }

    // Classifies a Graph API error body. Meta's error subcodes are not stable enough to hard-code safely, so
    // we inspect the (external) error message text for the two well-known conditions and otherwise fall back
    // to the step's default category. This is parsing a third-party response, not an internal magic string.
    private static WhatsAppOnboardingError ClassifyGraphError(string body, WhatsAppOnboardingError defaultError)
    {
        var message = ReadNestedErrorMessage(body);
        if (string.IsNullOrWhiteSpace(message))
        {
            return defaultError;
        }

        var lower = message.ToLowerInvariant();
        if (lower.Contains("already") && (lower.Contains("register") || lower.Contains("migrat")))
        {
            return WhatsAppOnboardingError.NumberAlreadyRegistered;
        }

        if (lower.Contains("eligib") || lower.Contains("verif"))
        {
            return WhatsAppOnboardingError.WabaNotEligible;
        }

        return defaultError;
    }

    private static string? ReadStringProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadNestedErrorMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error", out var error)
                   && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GenerateRegistrationPin() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
