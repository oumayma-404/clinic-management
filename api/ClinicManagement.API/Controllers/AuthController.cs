using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Local (offline) authentication endpoints. Active in Local mode; the login endpoint has no
/// effect in Cloud mode (Auth0 owns login there).
/// </summary>
[ApiController]
[Route("api/auth")]
// Authenticated-but-role-less: every bootstrap action below is explicitly [AllowAnonymous] (and on the
// coverage guard's reviewed allow-list); the class policy is what makes a *future* action here fail closed
// instead of inheriting the anonymity of its neighbours.
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public class AuthController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    private readonly DeploymentProfile _deployment;

    public AuthController(IMediator mediator, IConfiguration configuration, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _configuration = configuration;
        _deployment = deployment;
    }

    // Injected, not re-resolved per request: AddInfrastructure already registers the resolved profile as a
    // singleton, so calling Resolve here re-read the key, re-parsed the enum and allocated a new profile on every
    // request — two answers to « which profile is this? », which is the shape the type was created to remove.
    private DeploymentProfile Deployment => _deployment;

    /// <summary>
    /// Public: reports whether this deployment owns its accounts, so the frontend renders the right login UI.
    ///
    /// <para>⚠️ The only read here that is a <b>value</b> and not a guard: it must keep answering in every
    /// profile, or the frontend's mode probe has nothing to branch on. The <c>mode</c> wire value is unchanged.</para>
    ///
    /// <para><b>US-3 added <c>selfRegistrationEnabled</c>, and it cannot be derived from <c>mode</c>.</b> The
    /// browser learns the mode from the Next server's own <c>AUTH_MODE</c>, which reads <c>local</c> in
    /// <i>both</i> account-owning profiles — so <c>/join</c> had no way to tell a LAN install (where the clinic
    /// code is a real gate) from a hosted one (where it is a password everybody has). Answering it here keeps the
    /// server the single authority: the page cannot offer a form the <c>register</c> endpoint below will 404.</para>
    /// </summary>
    [AllowAnonymous]
    [HttpGet("mode")]
    public IActionResult GetMode()
    {
        var deployment = Deployment;
        var mode = deployment.UsesLocalAccounts
            ? LocalAuthConfig.LocalMode
            : LocalAuthConfig.CloudMode;
        return Ok(new { mode, selfRegistrationEnabled = deployment.AllowsSelfRegistration });
    }

    /// <summary>
    /// Local-mode login: email + password → signed JWT. Returns 401 on any failure.
    /// </summary>
    [AllowAnonymous]
    // The brute-force surface (US-4 / AC-4.1): a tight window per submitted account, plus a looser per-address
    // ceiling — a whole practice arrives through one NAT address, so the address alone cannot be the brake.
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Local-mode only — Cloud login is owned by Auth0 (mirrors Setup/Register).
        if (!Deployment.UsesLocalAccounts)
        {
            return NotFound();
        }

        var command = new LoginCommand
        {
            Email = request.Email,
            Password = request.Password
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Exchanges the BFF's HttpOnly session cookie for a fresh short-lived access token (US-5).
    ///
    /// Anonymous by necessity — the caller has no access token yet, that is the point. It is not
    /// unauthenticated in effect: the refresh token itself is the credential, and it is signed, audience-bound,
    /// lifetime-bound and re-checked against live account state. Rate-limited like the other anonymous auth
    /// endpoints.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (!Deployment.UsesLocalAccounts)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new RefreshTokenCommand { RefreshToken = request.RefreshToken });

        return result.IsFailure
            ? Failure(result.Error, StatusCodes.Status401Unauthorized)
            : Ok(result);
    }

    /// <summary>
    /// Local-mode first-run setup: creates the clinic + first admin (email+password).
    /// Reachable only from the server machine (localhost) and only until the first admin
    /// exists — AC-1.2a. Does not exist in Cloud mode.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        if (!Deployment.UsesLocalAccounts)
        {
            return NotFound();
        }

        if (!ClinicManagement.Infrastructure.LocalRequest.IsLoopback(HttpContext))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, new { error = "First-run setup is only available on the server machine." });
        }

        var command = new CreateClinicCommand
        {
            Name = request.ClinicName,
            Email = request.Email,
            Password = request.Password,
            FullName = request.FullName,
            Phone = request.Phone,
            Address = request.Address,
            City = request.City,
            Role = "admin",
            DoctorInfo = request.DoctorInfo, // when set, the admin is also the practitioner (Doctor is created + linked)
            GenerateCode = true,
            WorkingHoursJson = request.WorkingHoursJson
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Staff self-registration: join a clinic by code with email+password. Reachable from any LAN client (the
    /// clinic code is the gate — not localhost). Admin is never self-assignable (enforced in the handler), and
    /// since I5 the account is created <b>pending an admin's activation</b>.
    ///
    /// <para>⚠️ Gated on <c>AllowsSelfRegistration</c> since US-3, <b>not</b> on <c>UsesLocalAccounts</c> — which
    /// is true in the hosted profile too, so the old guard would have exposed a six-character clinic code as the
    /// only barrier between the internet and an account that reads every patient record. Unchanged in both shipped
    /// profiles: <c>SelfHostedLan</c> still allows it, <c>CloudBrowser</c> still 404s.</para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!Deployment.AllowsSelfRegistration)
        {
            return NotFound();
        }

        var command = new JoinClinicCommand
        {
            Code = request.Code,
            Role = request.Role,
            DoctorInfo = request.DoctorInfo,
            Email = request.Email,
            Password = request.Password,
            FullName = request.FullName,
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Authenticated: the current user sets a new password (verifying the current one). Used
    /// for the forced change after an admin reset and for voluntary changes (AC-5.2).
    /// </summary>
    // No method policy: the class's `Authenticated` is exactly right, and deliberately so. A user forced to
    // change their password after an admin reset may not have a role in the JWT yet (Cloud writes it to
    // app_metadata only once the clinic is joined), so requiring one here would lock them out of the very
    // screen that unblocks them. A bare `[Authorize]` said the same thing while looking like an omission.
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var command = new ChangePasswordCommand
        {
            CurrentPassword = request.CurrentPassword,
            NewPassword = request.NewPassword
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}
