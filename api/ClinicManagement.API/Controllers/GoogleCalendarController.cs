using System.Security.Cryptography;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.API.Controllers;

// Class-level [Authorize]: the AJAX endpoints (connect/status/sync) require an authenticated user; the
// single OAuth browser-redirect endpoint (callback) carries an explicit [AllowAnonymous] (it is reached
// by a top-level navigation from Google and cannot present a bearer token). Per-clinic connection binding
// (feature cloud-security-and-tenant-isolation, #4) replaces the former single global token/calendar.
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/[controller]")]
public class GoogleCalendarController : ApiControllerBase
{
    // Short-lived server-side store mapping an issued OAuth `state` to the CLINIC that started the flow,
    // so the anonymous callback can bind the resulting token to the right tenant (CSRF + clinic binding).
    private const string OAuthStateCachePrefix = "google_oauth_state:";
    // Companion HttpOnly cookie holding the same state, so the callback can prove the flow was started by
    // THIS browser (double-submit) — not merely that the server issued some state recently (login-CSRF).
    private const string OAuthStateCookieName = "google_oauth_state";
    private const string OAuthCookiePath = "/api/googlecalendar";
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(10);

    private readonly IGoogleCalendarSyncService _syncService;
    private readonly IConfiguration _configuration;
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly IMediator _mediator;
    private readonly IGoogleTokenProtector _googleTokenProtector;
    private readonly ILogger<GoogleCalendarController> _logger;

    public GoogleCalendarController(
        IGoogleCalendarSyncService syncService,
        IConfiguration configuration,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        IMemoryCache cache,
        IMediator mediator,
        IGoogleTokenProtector googleTokenProtector,
        ILogger<GoogleCalendarController> logger)
    {
        _googleTokenProtector = googleTokenProtector;
        _syncService = syncService;
        _configuration = configuration;
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Disconnect the caller's clinic from Google Calendar (AC-P2.33/2.34). `AdminOnly`, matching every other
    /// mutating action here.
    /// <para>
    /// Unlike the rest of this controller — whose OAuth plumbing legitimately works the repositories directly —
    /// this is an ordinary clinic mutation with an admin guard, so it goes through MediatR like every other one:
    /// that is what makes it unit-testable (the test project references Application, not this controller's
    /// internals) and what gets the realtime broadcast for free.
    /// </para>
    /// </summary>
    [HttpPost("disconnect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DisconnectGoogleCalendarCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(new { disconnected = true });
    }

    /// <summary>
    /// Manually trigger sync from Google Calendar to Clinic appointments (admin only). The sync service
    /// resolves the caller's clinic internally and scopes everything to it.
    /// </summary>
    [HttpPost("sync-from-google")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> SyncFromGoogleCalendar()
    {
        try
        {
            _logger.LogInformation("Manual sync from Google Calendar triggered");
            await _syncService.SyncGoogleCalendarToAppointmentsAsync();
            return Ok(new
            {
                message = "Sync from Google Calendar completed successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return BadRequest(new { error = "Google Calendar n'est pas connecté pour ce cabinet. Connectez-le depuis les paramètres." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual sync from Google Calendar");
            return StatusCode(500, new { error = "Erreur lors de la synchronisation depuis Google Calendar." });
        }
    }

    /// <summary>
    /// Get the redirect URI that should be configured in Google Cloud Console.
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
            redirectUri,
            configuredUri = configuredRedirectUri,
            requestScheme = Request.Scheme,
            requestHost = Request.Host.ToString()
        });
    }

    /// <summary>
    /// Get sync status for the caller's clinic (is Google Calendar connected + is its token valid).
    /// </summary>
    [HttpGet("status")]
    // The one action here that is not integration *administration*. Every role's agenda reads it on mount to
    // decide whether to render the « non synchronisé » badge, so gating it with its admin-only siblings would
    // put a 403 on every reception page load and silently switch the badge off for the people who watch it.
    // It returns whether the secrets are *present*, never their values.
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
    public async Task<IActionResult> GetSyncStatus(CancellationToken cancellationToken)
    {
        var clientId = _configuration["GoogleCalendar:ClientId"];
        var clientSecret = _configuration["GoogleCalendar:ClientSecret"];
        var hasClientId = !string.IsNullOrEmpty(clientId);
        var hasClientSecret = !string.IsNullOrEmpty(clientSecret);

        var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return BadRequest(new { error = clinicResult.Error ?? "Impossible de résoudre le cabinet." });
        }

        var clinic = await _clinicRepository.GetByIdAsync(clinicResult.Value, cancellationToken);
        // ⚠️ An unreadable token reports « non configuré » on this status read alone, deliberately: it IS the
        // screen that offers « Reconnecter », so refusing it would leave the clinic looking at an error with no
        // control to act on. Every path that would actually SYNC refuses instead (FR-3.3).
        var refreshToken = !string.IsNullOrEmpty(clinic?.GoogleRefreshTokenProtected)
                           && _googleTokenProtector.TryUnprotect(clinic.GoogleRefreshTokenProtected, out var decrypted)
            ? decrypted
            : null;
        var calendarId = clinic?.GoogleCalendarId ?? "primary";
        var hasRefreshToken = !string.IsNullOrEmpty(refreshToken);

        var isConfigured = hasClientId && hasClientSecret && hasRefreshToken;

        // Validate the refresh token by attempting a small read against THIS clinic's calendar.
        var tokenValid = false;
        if (isConfigured)
        {
            try
            {
                var calendarService = HttpContext.RequestServices.GetRequiredService<IGoogleCalendarService>();
                await calendarService.GetEventsAsync(
                    new GoogleCalendarConnection(refreshToken!, clinic!.GoogleCalendarId),
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(1),
                    cancellationToken);
                tokenValid = true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("refresh token") || ex.Message.Contains("not configured"))
            {
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
            hasClientId,
            hasClientSecret,
            hasRefreshToken,
            tokenValid,
            calendarId,
            message = !hasClientId || !hasClientSecret
                ? "Le ClientId et le ClientSecret Google doivent être configurés côté serveur."
                : !hasRefreshToken
                    ? "Google Calendar n'est pas connecté pour ce cabinet. Cliquez sur « Connecter » pour autoriser l'accès."
                    : !tokenValid
                        ? "Le jeton Google Calendar est invalide ou expiré. Reconnectez-vous."
                        : "Google Calendar est connecté et prêt."
        });
    }

    /// <summary>
    /// Manually trigger sync of a specific appointment to Google Calendar (admin only).
    /// </summary>
    [HttpPost("sync-appointment/{appointmentId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
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
            return BadRequest(new { error = "Google Calendar n'est pas connecté pour ce cabinet. Connectez-le depuis les paramètres." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing appointment {AppointmentId} to Google Calendar", appointmentId);
            return StatusCode(500, new { error = "Erreur lors de la synchronisation du rendez-vous." });
        }
    }

    /// <summary>
    /// Begin the Google Calendar OAuth flow for the caller's clinic (admin only). Returns the Google
    /// authorization URL; the frontend navigates the browser to it. A high-entropy `state` is minted and
    /// bound to THIS clinic (server-side cache + HttpOnly companion cookie) so the anonymous callback can
    /// prove the flow (CSRF) and save the token to the correct tenant.
    /// </summary>
    [HttpPost("connect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken = default)
    {
        var clientId = _configuration["GoogleCalendar:ClientId"];
        var clientSecret = _configuration["GoogleCalendar:ClientSecret"];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return BadRequest(new { error = "Le ClientId et le ClientSecret Google doivent être configurés côté serveur." });
        }

        var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return BadRequest(new { error = clinicResult.Error ?? "Impossible de résoudre le cabinet." });
        }

        var configuredRedirectUri = _configuration["GoogleCalendar:RedirectUri"];
        var redirectUri = !string.IsNullOrEmpty(configuredRedirectUri)
            ? configuredRedirectUri
            : $"{Request.Scheme}://{Request.Host}/api/googlecalendar/callback";

        const string scopes = "https://www.googleapis.com/auth/calendar";

        // CSRF + clinic binding: mint a high-entropy state, cache state → clinicId (short-lived), and drop
        // the state into an HttpOnly companion cookie. The callback requires the query state to match BOTH
        // the cache entry and the cookie before it will save a token — and it saves to the cached clinicId.
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _cache.Set(OAuthStateCachePrefix + state, clinicResult.Value, OAuthStateLifetime);
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

        return Ok(new { authUrl });
    }

    /// <summary>
    /// Handle the OAuth callback: validate the state, exchange the code, and save the refresh token to the
    /// clinic that started the flow. Anonymous — Google redirects the user's browser here with ?code=...
    /// and it cannot carry a bearer token; the clinic is resolved from the state-bound cache entry.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, [FromQuery] string? state, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Google OAuth error: {Error}", error);
            return BadRequest(new { error = $"Authorization failed: {error}" });
        }

        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("Authorization code not provided in callback");
            return BadRequest(new { error = "Code d'autorisation manquant." });
        }

        // CSRF + clinic binding: reject a callback whose state we didn't issue, that is missing, or that does
        // not match the companion cookie set on `connect`. Consume the cache entry (and the resolved clinic
        // id) and clear the cookie so a state cannot be replayed.
        var cookieState = Request.Cookies[OAuthStateCookieName];
        Response.Cookies.Delete(OAuthStateCookieName, new CookieOptions { Path = OAuthCookiePath });
        if (string.IsNullOrEmpty(state)
            || string.IsNullOrEmpty(cookieState)
            || !string.Equals(state, cookieState, StringComparison.Ordinal)
            || !_cache.TryGetValue(OAuthStateCachePrefix + state, out Guid clinicId))
        {
            _logger.LogWarning("Google OAuth callback rejected: missing, unrecognized, or unbound state parameter");
            return BadRequest(new { error = "Demande d'autorisation invalide ou expirée. Veuillez relancer la connexion à Google." });
        }
        _cache.Remove(OAuthStateCachePrefix + state);

        try
        {
            var clientId = _configuration["GoogleCalendar:ClientId"];
            var clientSecret = _configuration["GoogleCalendar:ClientSecret"];

            var configuredRedirectUri = _configuration["GoogleCalendar:RedirectUri"];
            var redirectUri = !string.IsNullOrEmpty(configuredRedirectUri)
                ? configuredRedirectUri
                : $"{Request.Scheme}://{Request.Host}/api/googlecalendar/callback";

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                return BadRequest(new { error = "Identifiants Google Agenda non configurés." });
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
                new FormUrlEncodedContent(tokenRequest),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to exchange authorization code. Status: {Status}, Response: {Response}",
                    response.StatusCode, errorContent);
                // Do not leak the raw Google error body to the client (canonical { error } only).
                return StatusCode(500, new { error = "Échec de l'échange du code d'autorisation Google." });
            }

            var tokenResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(tokenResponse);

            if (tokenData == null)
            {
                _logger.LogError("Failed to parse token response from Google");
                return StatusCode(500, new { error = "Échec de l'analyse de la réponse de Google." });
            }

            if (tokenData.ContainsKey("error"))
            {
                var oauthError = tokenData["error"].GetString();
                _logger.LogError("Google OAuth error: {Error}", oauthError);
                return StatusCode(500, new { error = "Erreur OAuth Google." });
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                _logger.LogWarning("Clinic {ClinicId} bound to the OAuth state no longer exists", clinicId);
                return BadRequest(new { error = "Cabinet introuvable pour cette autorisation." });
            }

            string? refreshToken = null;
            if (tokenData.ContainsKey("refresh_token"))
            {
                refreshToken = tokenData["refresh_token"].GetString();
            }
            else
            {
                // No refresh token in the response (user already granted). Reuse the clinic's existing one —
                // decrypting it (FR-3.4), and treating an unreadable one as absent, which is honest here: the
                // user is standing in front of a re-connect flow, and the fix is to revoke and grant again.
                refreshToken = !string.IsNullOrEmpty(clinic.GoogleRefreshTokenProtected)
                               && _googleTokenProtector.TryUnprotect(clinic.GoogleRefreshTokenProtected, out var stored)
                    ? stored
                    : null;
                if (string.IsNullOrEmpty(refreshToken))
                {
                    _logger.LogWarning("No refresh token returned and none stored for clinic {ClinicId}", clinicId);
                    return StatusCode(500, new
                    {
                        error = "Aucun jeton de rafraîchissement reçu. Révoquez l'accès dans votre compte Google puis reconnectez."
                    });
                }
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                return StatusCode(500, new { error = "Le jeton de rafraîchissement est vide." });
            }

            // Persist the refresh token onto THIS clinic (per-clinic isolation, #4) — encrypted at rest (FR-3.4)
            // and preserving any target calendar id already chosen (null → the account's primary calendar).
            clinic.SetGoogleCalendarConnection(_googleTokenProtector.Protect(refreshToken), clinic.GoogleCalendarId);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Google Calendar connected for clinic {ClinicId}.", clinicId);

            var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:3000";
            return Redirect($"{frontendUrl}/appointments?googleCalendarAuthorized=true");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OAuth callback");
            return StatusCode(500, new { error = "Erreur lors du traitement de l'autorisation." });
        }
    }
}
