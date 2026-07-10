using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.API.Controllers;

// Class-level [Authorize]: closes the Cloud-mode gap where sync-from-google / status / sync-appointment
// were reachable unauthenticated (Cloud has a null fallback policy). The two browser-redirect OAuth
// endpoints below carry an explicit [AllowAnonymous] (they cannot present a bearer token).
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GoogleCalendarController : ControllerBase
{
    // Short-lived server-side store of issued OAuth `state` values for CSRF protection on the callback.
    private const string OAuthStateCachePrefix = "google_oauth_state:";
    // Companion HttpOnly cookie holding the same state, so the callback can prove the flow was started by
    // THIS browser (double-submit) — not merely that the server issued some state recently (login-CSRF).
    private const string OAuthStateCookieName = "google_oauth_state";
    private const string OAuthCookiePath = "/api/googlecalendar";
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(10);

    private readonly IGoogleCalendarSyncService _syncService;
    private readonly IConfiguration _configuration;
    private readonly IGoogleTokenStore _tokenStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GoogleCalendarController> _logger;

    public GoogleCalendarController(
        IGoogleCalendarSyncService syncService,
        IConfiguration configuration,
        IGoogleTokenStore tokenStore,
        IMemoryCache cache,
        ILogger<GoogleCalendarController> logger)
    {
        _syncService = syncService;
        _configuration = configuration;
        _tokenStore = tokenStore;
        _cache = cache;
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
    /// Get the redirect URI that should be configured in Google Cloud Console
    /// </summary>
    [HttpGet("redirect-uri")]
    public IActionResult GetRedirectUri()
    {
        var configuredRedirectUri = _configuration["GoogleCalendar:RedirectUri"];
        var redirectUri = !string.IsNullOrEmpty(configuredRedirectUri)
            ? configuredRedirectUri
            : $"{Request.Scheme}://{Request.Host}/api/googlecalendar/callback";
        
        return Ok(new
        {
            redirectUri = redirectUri,
            configuredUri = configuredRedirectUri,
            requestScheme = Request.Scheme,
            requestHost = Request.Host.ToString(),
            instructions = new
            {
                step1 = "Go to https://console.cloud.google.com/apis/credentials",
                step2 = "Select your OAuth 2.0 Client ID",
                step3 = "Under 'Authorized redirect URIs', click 'ADD URI'",
                step4 = $"Add this exact URI: {redirectUri}",
                step5 = "Click 'SAVE'",
                note = "The URI must match EXACTLY (including http/https, port number, and path)"
            }
        });
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
        var refreshToken = _tokenStore.GetRefreshToken();
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
    // Browser-redirect endpoint: reached by a top-level navigation that cannot carry a bearer token,
    // so it is exempted from the Local-mode fail-closed fallback policy (FR-E3). The AJAX endpoints
    // above deliberately carry NO carve-out — the fallback now requires auth on them in Local mode.
    [AllowAnonymous]
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

        // CSRF protection: mint a high-entropy state (CSPRNG, not Guid), persist it server-side
        // (short-lived), AND drop it into an HttpOnly companion cookie. The callback requires the query
        // state to match BOTH the cache entry and the cookie — binding the flow to this browser so an
        // attacker cannot lure an admin to a callback carrying a state the attacker minted (login-CSRF).
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _cache.Set(OAuthStateCachePrefix + state, true, OAuthStateLifetime);
        Response.Cookies.Append(OAuthStateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax, // sent on the top-level GET redirect Google makes back to the callback
            MaxAge = OAuthStateLifetime,
            Path = OAuthCookiePath
        });

        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
            $"client_id={Uri.EscapeDataString(clientId)}&" +
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(scopes)}&" +
            $"access_type=offline&" +
            $"prompt=consent&" +
            $"state={Uri.EscapeDataString(state)}";
        
        _logger.LogWarning("=== GOOGLE OAUTH REDIRECT URI DEBUG ===");
        _logger.LogWarning("Configured RedirectUri from appsettings.json: {ConfiguredUri}", configuredRedirectUri ?? "(not set)");
        _logger.LogWarning("Request Scheme: {Scheme}", Request.Scheme);
        _logger.LogWarning("Request Host: {Host}", Request.Host);
        _logger.LogWarning("Final Redirect URI being used: {RedirectUri}", redirectUri);
        _logger.LogWarning("=== IMPORTANT: Add this EXACT URI to Google Cloud Console ===");
        _logger.LogWarning("Go to: https://console.cloud.google.com/apis/credentials");
        _logger.LogWarning("Select your OAuth 2.0 Client ID");
        _logger.LogWarning("Under 'Authorized redirect URIs', add: {RedirectUri}", redirectUri);
        _logger.LogWarning("=========================================");

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle OAuth callback and exchange authorization code for refresh token
    /// </summary>
    // Browser-redirect endpoint (Google redirects the user's browser here with ?code=...); it cannot
    // carry a bearer token, so it is exempted from the Local-mode fail-closed fallback policy (FR-E3).
    [AllowAnonymous]
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

        // CSRF protection: reject a callback whose state we didn't issue, that is missing, or that does not
        // match the companion cookie set on `authorize` (proves this browser started the flow). Consume the
        // cache entry and clear the cookie so a state cannot be replayed.
        var cookieState = Request.Cookies[OAuthStateCookieName];
        Response.Cookies.Delete(OAuthStateCookieName, new CookieOptions { Path = OAuthCookiePath });
        if (string.IsNullOrEmpty(state)
            || string.IsNullOrEmpty(cookieState)
            || !string.Equals(state, cookieState, StringComparison.Ordinal)
            || !_cache.TryGetValue(OAuthStateCachePrefix + state, out _))
        {
            _logger.LogWarning("Google OAuth callback rejected: missing, unrecognized, or unbound state parameter");
            return BadRequest(new { error = "Invalid or expired authorization state. Please restart the Google authorization." });
        }
        _cache.Remove(OAuthStateCachePrefix + state);

        try
        {
            var clientId = _configuration["GoogleCalendar:ClientId"];
            var clientSecret = _configuration["GoogleCalendar:ClientSecret"];
            
            // Use the same redirect URI as in the authorization request
            var configuredRedirectUri = _configuration["GoogleCalendar:RedirectUri"];
            var redirectUri = !string.IsNullOrEmpty(configuredRedirectUri)
                ? configuredRedirectUri
                : $"{Request.Scheme}://{Request.Host}/api/googlecalendar/callback";
            
            _logger.LogWarning("Using redirect URI for token exchange: {RedirectUri}", redirectUri);
            _logger.LogWarning("If you get redirect_uri_mismatch error, ensure this URI is in Google Cloud Console");

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
                // We should use the existing refresh token from the token store (file, config fallback)
                refreshToken = _tokenStore.GetRefreshToken();
                if (string.IsNullOrEmpty(refreshToken))
                {
                    _logger.LogWarning("No refresh token in response and no existing refresh token stored. " +
                        "This might happen if the user already authorized. You may need to revoke access and re-authorize.");
                    return StatusCode(500, new {
                        error = "Refresh token not received. If you've already authorized this app, you may need to revoke access in Google Account settings and try again."
                    });
                }
                _logger.LogInformation("Using existing refresh token from the token store");
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                return StatusCode(500, new { error = "Refresh token is empty" });
            }

            // Persist the refresh token to the gitignored per-install token store instead of rewriting the
            // committed appsettings.json (US-3 / FR-E3). The store is a Singleton with an in-memory cache,
            // so the new token is picked up immediately by GoogleCalendarService without a restart.
            await _tokenStore.SaveRefreshTokenAsync(refreshToken);

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

