using System.Net.Http.Headers;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Production implementation of <see cref="ITtnClient"/> (FR-3): OAuth2 client-credentials against TTN, then
/// a signed-TEIF POST to the « El Fatoora » submission endpoint. Selected when the clinic's environment is
/// "Production".
/// <para>
/// The exact TTN transport (REST vs SOAP), endpoints, auth and status vocabulary are a spec Open Question
/// (#1) not resolvable in-repo. This is a best-effort REST client driven entirely by config (<c>Ttn:*</c>);
/// when it is not configured it returns a <see cref="TtnSubmissionOutcome.TransientFailure"/> (so the outbox
/// keeps the invoice Queued rather than losing it), and network/5xx errors are treated as transient too.
/// Verify against the official TTN integration docs before enabling Production.
/// </para>
/// </summary>
public class HttpTtnClient : ITtnClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HttpTtnClient> _logger;

    public HttpTtnClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<HttpTtnClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public string Environment => Clinic.TtnEnvironmentProduction;

    public async Task<TtnSubmissionResult> SubmitAsync(string signedTeifXml, string invoiceNumber, CancellationToken cancellationToken = default)
    {
        var baseUrl = TtnConfig.BaseUrl(_configuration, Environment);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("TTN Production base URL not configured; leaving invoice {InvoiceNumber} queued.", invoiceNumber);
            return TtnSubmissionResult.Transient("Endpoint TTN production non configuré.");
        }

        try
        {
            var token = await AcquireTokenAsync(cancellationToken);
            if (token == null)
            {
                return TtnSubmissionResult.Transient("Authentification TTN indisponible.");
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/invoices");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(signedTeifXml, Encoding.UTF8, "application/xml");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var ttnId = ExtractIdentifier(body) ?? invoiceNumber;
                return TtnSubmissionResult.Validated(ttnId, body);
            }

            // 4xx (except 429) = the invoice was refused on its own merits → permanent rejection.
            if ((int)response.StatusCode is >= 400 and < 500 && (int)response.StatusCode != 429)
            {
                return TtnSubmissionResult.Rejected($"TTN a rejeté la facture ({(int)response.StatusCode}): {Truncate(body)}", body);
            }

            // 5xx / 429 = transient; the outbox retries.
            return TtnSubmissionResult.Transient($"TTN indisponible ({(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transient error submitting invoice {InvoiceNumber} to TTN Production", invoiceNumber);
            return TtnSubmissionResult.Transient("Erreur réseau lors de l'envoi à TTN.");
        }
    }

    private async Task<string?> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        var tokenUrl = TtnConfig.TokenUrl(_configuration, Environment);
        var username = TtnConfig.Username(_configuration);
        var secret = TtnConfig.ApiSecret(_configuration);

        if (string.IsNullOrWhiteSpace(tokenUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("TTN Production credentials not fully configured.");
            return null;
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = username,
                ["client_secret"] = secret
            })
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
    }

    private static string? ExtractIdentifier(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        // Best-effort: accept either a JSON { "identifier": "..." } or an XML <UniqueIdentifier>...</UniqueIdentifier>.
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            foreach (var name in new[] { "identifier", "uniqueIdentifier", "id" })
            {
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            const string open = "<UniqueIdentifier>";
            const string close = "</UniqueIdentifier>";
            var start = body.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            var end = body.IndexOf(close, StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
            {
                return body[(start + open.Length)..end].Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 300 ? value : value[..300];
}
