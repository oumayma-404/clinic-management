using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

public class Auth0ManagementService : IAuth0ManagementService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Auth0ManagementService> _logger;
    private readonly string _domain;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public Auth0ManagementService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<Auth0ManagementService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _domain = configuration["Auth0:Domain"] ?? throw new InvalidOperationException("Auth0:Domain is not configured");
        _clientId = configuration["Auth0:ManagementApi:ClientId"] ?? throw new InvalidOperationException("Auth0:ManagementApi:ClientId is not configured");
        _clientSecret = configuration["Auth0:ManagementApi:ClientSecret"] ?? throw new InvalidOperationException("Auth0:ManagementApi:ClientSecret is not configured");
    }

    public async Task UpdateUserMetadataAsync(string userId, Guid clinicId, string role, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if Management API is configured
            if (string.IsNullOrWhiteSpace(_clientId) || _clientId == "YOUR_MANAGEMENT_API_CLIENT_ID" ||
                string.IsNullOrWhiteSpace(_clientSecret) || _clientSecret == "YOUR_MANAGEMENT_API_CLIENT_SECRET")
            {
                _logger.LogWarning("Auth0 Management API credentials not configured. Skipping metadata update for user {UserId}", userId);
                return; // Silently skip if not configured
            }

            // Get Management API access token
            var accessToken = await GetManagementApiTokenAsync(cancellationToken);

            // Update user metadata
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var updateRequest = new
            {
                app_metadata = new
                {
                    clinic_id = clinicId.ToString(),
                    role = role.ToLowerInvariant()
                }
            };

            var json = JsonSerializer.Serialize(updateRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PatchAsync(
                $"https://{_domain}/api/v2/users/{Uri.EscapeDataString(userId)}",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to update Auth0 user metadata. Status: {Status}, Error: {Error}. This is non-critical and the operation will continue.", 
                    response.StatusCode, errorContent);
                // Don't throw - this is non-critical, user is already created in DB
                return;
            }

            _logger.LogInformation("Successfully updated Auth0 user metadata for user {UserId} with clinic {ClinicId} and role {Role}", 
                userId, clinicId, role);
        }
        catch (Exception ex)
        {
            // Log but don't throw - this is non-critical
            // The user is already created in the database, Auth0 metadata update is optional
            _logger.LogWarning(ex, "Error updating Auth0 user metadata for user {UserId}. This is non-critical and the operation will continue.", userId);
        }
    }

    private async Task<string> GetManagementApiTokenAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        
        var request = new Dictionary<string, string>
        {
            { "client_id", _clientId },
            { "client_secret", _clientSecret },
            { "audience", $"https://{_domain}/api/v2/" },
            { "grant_type", "client_credentials" }
        };

        var content = new FormUrlEncodedContent(request);
        var response = await client.PostAsync($"https://{_domain}/oauth/token", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to get Auth0 Management API token. Status: {Status}, Error: {Error}", 
                response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to get Auth0 Management API token: {response.StatusCode}");
        }

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(jsonResponse);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("Failed to extract access token from Auth0 response");
        }

        return accessToken;
    }
}

