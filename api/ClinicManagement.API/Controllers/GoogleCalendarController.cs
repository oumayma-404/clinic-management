using Microsoft.AspNetCore.Mvc;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GoogleCalendarController : ControllerBase
{
    private readonly IGoogleCalendarSyncService _syncService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleCalendarController> _logger;

    public GoogleCalendarController(
        IGoogleCalendarSyncService syncService,
        IConfiguration configuration,
        ILogger<GoogleCalendarController> logger)
    {
        _syncService = syncService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger sync from Google Calendar to Clinic appointments
    /// </summary>
    [HttpPost("sync-from-google")]
    public async Task<IActionResult> SyncFromGoogleCalendar()
    {
        try
        {
            _logger.LogInformation("Manual sync from Google Calendar triggered");
            await _syncService.SyncGoogleCalendarToAppointmentsAsync();
            return Ok(new { 
                message = "Sync from Google Calendar completed successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return BadRequest(new { error = "Google Calendar is not configured. Please check your appsettings.json" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual sync from Google Calendar");
            return StatusCode(500, new { 
                error = $"Error syncing from Google Calendar: {ex.Message}",
                details = ex.ToString()
            });
        }
    }

    /// <summary>
    /// Get sync status and diagnostic information
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetSyncStatus()
    {
        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var clientId = config["GoogleCalendar:ClientId"];
        var clientSecret = config["GoogleCalendar:ClientSecret"];
        var refreshToken = config["GoogleCalendar:RefreshToken"];
        var calendarId = config["GoogleCalendar:CalendarId"] ?? "primary";

        var hasClientId = !string.IsNullOrEmpty(clientId);
        var hasClientSecret = !string.IsNullOrEmpty(clientSecret);
        var hasRefreshToken = !string.IsNullOrEmpty(refreshToken);
        
        var isConfigured = hasClientId && hasClientSecret && hasRefreshToken;
        
        // Try to validate the refresh token by attempting to get an access token
        var tokenValid = false;
        if (isConfigured)
        {
            try
            {
                var googleCalendarService = HttpContext.RequestServices.GetRequiredService<IGoogleCalendarService>();
                // Try to get events to validate the token
                await googleCalendarService.GetEventsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
                tokenValid = true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("refresh token") || ex.Message.Contains("not configured"))
            {
                // Token is invalid or expired
                tokenValid = false;
            }
            catch
            {
                // Other errors - assume token might be valid but something else is wrong
                tokenValid = true;
            }
        }

        return Ok(new
        {
            isConfigured = isConfigured && tokenValid,
            hasClientId = hasClientId,
            hasClientSecret = hasClientSecret,
            hasRefreshToken = hasRefreshToken,
            tokenValid = tokenValid,
            calendarId = calendarId,
            message = !hasClientId || !hasClientSecret
                ? "Google Calendar ClientId and ClientSecret must be configured in appsettings.json"
                : !hasRefreshToken
                    ? "Google Calendar refresh token is not configured. Please click 'Sync to Google Calendar' to authorize."
                    : !tokenValid
                        ? "Google Calendar refresh token is invalid or expired. Please re-authorize by clicking 'Sync to Google Calendar'."
                        : "Google Calendar is configured and ready"
        });
    }

    /// <summary>
    /// Manually trigger sync of a specific appointment to Google Calendar
    /// </summary>
    [HttpPost("sync-appointment/{appointmentId}")]
    public async Task<IActionResult> SyncAppointmentToGoogle(Guid appointmentId)
    {
        try
        {
            _logger.LogInformation("Manual sync of appointment {AppointmentId} to Google Calendar triggered", appointmentId);
            await _syncService.SyncAppointmentToGoogleCalendarAsync(appointmentId);
            return Ok(new { message = $"Appointment {appointmentId} synced to Google Calendar successfully" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return BadRequest(new { error = "Google Calendar is not configured. Please check your appsettings.json" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing appointment {AppointmentId} to Google Calendar", appointmentId);
            return StatusCode(500, new { error = $"Error syncing appointment: {ex.Message}" });
        }
    }

    /// <summary>
    /// Initiate Google Calendar OAuth authorization flow
    /// </summary>
    [HttpGet("authorize")]
    public IActionResult Authorize()
    {
        var clientId = _configuration["GoogleCalendar:ClientId"];
        var clientSecret = _configuration["GoogleCalendar:ClientSecret"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return BadRequest(new { error = "Google Calendar ClientId and ClientSecret must be configured in appsettings.json" });
        }

        // Build the authorization URL
        // Allow override from configuration, otherwise use request-based URI
        var configuredRedirectUri = _configuration["GoogleCalendar:RedirectUri"];
        var redirectUri = !string.IsNullOrEmpty(configuredRedirectUri)
            ? configuredRedirectUri
            : $"{Request.Scheme}://{Request.Host}/api/googlecalendar/callback";
        
        var scopes = "https://www.googleapis.com/auth/calendar";
        var state = Guid.NewGuid().ToString(); // Optional: store in session for CSRF protection

        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
            $"client_id={Uri.EscapeDataString(clientId)}&" +
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(scopes)}&" +
            $"access_type=offline&" +
            $"prompt=consent&" +
            $"state={Uri.EscapeDataString(state)}";
        
        _logger.LogInformation("Initiating Google Calendar OAuth flow. Redirect URI: {RedirectUri}. " +
            "Make sure this exact URI is added to Google Cloud Console > Credentials > Authorized redirect URIs", redirectUri);

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle OAuth callback and exchange authorization code for refresh token
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, [FromQuery] string? state)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Google OAuth error: {Error}", error);
            return BadRequest(new { error = $"Authorization failed: {error}" });
        }

        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("Authorization code not provided in callback");
            return BadRequest(new { error = "Authorization code not provided" });
        }

        try
        {
            var clientId = _configuration["GoogleCalendar:ClientId"];
            var clientSecret = _configuration["GoogleCalendar:ClientSecret"];
            
            // Use the same redirect URI as in the authorization request
            var configuredRedirectUri = _configuration["GoogleCalendar:RedirectUri"];
            var redirectUri = !string.IsNullOrEmpty(configuredRedirectUri)
                ? configuredRedirectUri
                : $"{Request.Scheme}://{Request.Host}/api/googlecalendar/callback";
            
            _logger.LogInformation("Using redirect URI for token exchange: {RedirectUri}", redirectUri);

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                return BadRequest(new { error = "Google Calendar credentials not configured" });
            }

            // Exchange authorization code for tokens
            using var httpClient = new HttpClient();
            var tokenRequest = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", redirectUri }
            };

            var response = await httpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(tokenRequest));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to exchange authorization code. Status: {Status}, Response: {Response}", 
                    response.StatusCode, errorContent);
                return StatusCode(500, new { error = "Failed to exchange authorization code", details = errorContent });
            }

            var tokenResponse = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Token response from Google: {Response}", tokenResponse);
            
            var tokenData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(tokenResponse);

            if (tokenData == null)
            {
                _logger.LogError("Failed to parse token response: {Response}", tokenResponse);
                return StatusCode(500, new { error = "Failed to parse token response from Google" });
            }

            // Check for errors first
            if (tokenData.ContainsKey("error"))
            {
                var oauthError = tokenData["error"].GetString();
                var errorDescription = tokenData.ContainsKey("error_description") 
                    ? tokenData["error_description"].GetString() 
                    : null;
                _logger.LogError("Google OAuth error: {Error}, Description: {Description}", oauthError, errorDescription);
                return StatusCode(500, new { error = $"Google OAuth error: {oauthError}", details = errorDescription });
            }

            // Get refresh token - it might be in the response or we might need to use the existing one
            string? refreshToken = null;
            if (tokenData.ContainsKey("refresh_token"))
            {
                refreshToken = tokenData["refresh_token"].GetString();
                _logger.LogInformation("New refresh token received from Google");
            }
            else
            {
                // If no refresh token in response, it means the user already authorized before
                // We should use the existing refresh token from configuration
                refreshToken = _configuration["GoogleCalendar:RefreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                {
                    _logger.LogWarning("No refresh token in response and no existing refresh token in configuration. " +
                        "This might happen if the user already authorized. You may need to revoke access and re-authorize.");
                    return StatusCode(500, new { 
                        error = "Refresh token not received. If you've already authorized this app, you may need to revoke access in Google Account settings and try again." 
                    });
                }
                _logger.LogInformation("Using existing refresh token from configuration");
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                return StatusCode(500, new { error = "Refresh token is empty" });
            }

            // Update appsettings.json with the refresh token
            // Note: In production, you should store this securely (e.g., in a database or secure vault)
            try
            {
                var appsettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                var appsettingsContent = await System.IO.File.ReadAllTextAsync(appsettingsPath);
                
                // Simple string replacement approach - more reliable than JSON parsing
                var oldRefreshTokenPattern = "\"RefreshToken\":\\s*\"[^\"]*\"";
                var newRefreshTokenLine = $"\"RefreshToken\": \"{refreshToken}\"";
                
                var updatedContent = Regex.Replace(
                    appsettingsContent,
                    oldRefreshTokenPattern,
                    newRefreshTokenLine,
                    RegexOptions.IgnoreCase);

                await System.IO.File.WriteAllTextAsync(appsettingsPath, updatedContent);
                _logger.LogInformation("Refresh token saved to appsettings.json");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save refresh token to appsettings.json. Token will only be available in memory until restart.");
            }

            // Also update the configuration in memory
            _configuration["GoogleCalendar:RefreshToken"] = refreshToken;

            _logger.LogInformation("Google Calendar authorization successful. Refresh token saved.");

            // Redirect to frontend with success message
            var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:3000";
            return Redirect($"{frontendUrl}/appointments?googleCalendarAuthorized=true");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OAuth callback");
            return StatusCode(500, new { error = $"Error processing authorization: {ex.Message}" });
        }
    }
}

