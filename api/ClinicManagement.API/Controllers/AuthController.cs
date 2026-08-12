using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Auth.Queries;
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
// Signing in works on an expired cabinet, and so does changing a password — including one an administrator forced
// (AC-4.7, EC-2). Class-level rather than seven copies because the reason is one reason: none of this is recording
// clinical work. The bootstrap actions arrive with an Unset tenant scope and so pass the gate anyway; only
// change-password is authenticated, clinic-scoped and non-GET, i.e. genuinely refused without this.
[AllowsWithoutSubscription("AC-4.7, EC-2 — a cabinet locked out of its own account cannot even read its records.")]
public class AuthController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    private readonly DeploymentProfile _deployment;
    private readonly ISubscriptionPolicy _subscriptionPolicy;

    public AuthController(
        IMediator mediator,
        IConfiguration configuration,
        DeploymentProfile deployment,
        ISubscriptionPolicy subscriptionPolicy)
    {
        _mediator = mediator;
        _configuration = configuration;
        _deployment = deployment;
        _subscriptionPolicy = subscriptionPolicy;
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
        return Ok(new
        {
            mode,
            selfRegistrationEnabled = deployment.AllowsSelfRegistration,
            // clinic-self-signup. A third field rather than a reuse of the second, because they answer opposite
            // questions: `selfRegistrationEnabled` is « may a stranger join an EXISTING clinic with its shared
            // code? » (✗ here) and this is « may a visitor create their OWN clinic behind an emailed token? »
            // (✓ here). The `/signup` page reads it so it never offers a form the endpoint would 404.
            publicSignupEnabled = deployment.AllowsPublicClinicSignup,
            // clinic-subscription Part C. The client mounts the « Abonnement » entry and (Part D) the banner from
            // this flag, never from probing the endpoint: a network failure and a genuine 404 are indistinguishable
            // to a probe, and EC-13 requires a failed read to be retryable rather than read as « aucun abonnement ».
            // The 404 on SubscriptionController stays as the server-side guarantee.
            requiresSubscription = deployment.RequiresSubscription,
            // clinic-subscription Part D, AC-1.3. The signup form has to state the trial before the visitor submits
            // anything, and `Subscription:TrialDays` is the one authority on how long it is — a literal « 30 jours »
            // in the wizard would be a second one, and this product's own landing copy already says « 2 semaines ».
            // Null where nothing expires, so no screen can quote a trial that deployment does not grant.
            trialDays = deployment.RequiresSubscription ? _subscriptionPolicy.TrialDays : (int?)null,
            // hosted-security-hardening FR-1.9. The floor, served rather than restated: every screen that
            // collects a NEW password used to carry its own `8`, so raising the constant would have left four
            // client-side rules disagreeing with the server that refuses them — and the user reading a French
            // sentence quoting the old number. `PasswordFloorSingleSourceTests` fails on a re-introduced literal.
            passwordMinLength = PasswordPolicy.MinLength,
            // hosted-security-hardening FR-1.1. Whether an ADMINISTRATOR is refused a session without a second
            // factor. Served so the login screen can say so before the first refusal rather than after it; the
            // server enforces it regardless, and no client reads this to decide whether to send the code.
            requiresSecondFactor = deployment.RequiresAdminSecondFactor,
        });
    }

    /// <summary>
    /// Public clinic self-signup: writes a pending signup and emails a verification link. Creates no clinic, no
    /// account and no catalogue — that happens at <see cref="VerifySignUp"/>.
    ///
    /// <para>⚠️ Gated on <c>AllowsPublicClinicSignup</c>, which is <b>not</b> <c>AllowsSelfRegistration</c>: that
    /// one is about joining an existing clinic with its six-character code and stays closed here. This door hands
    /// out no shared secret at all — the gate is a single-use 32-byte token sent to an address the caller must
    /// control.</para>
    ///
    /// <para>The 404 is returned <b>before the mediator is reached</b> (AC-1), so on a profile without the
    /// capability the handler, its repository and its mail sender are never resolved.</para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] ClinicSignUpRequest request)
    {
        if (!Deployment.AllowsPublicClinicSignup)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new SignUpClinicCommand
        {
            ClinicName = request.ClinicName,
            FullName = request.FullName,
            Email = request.Email,
            Password = request.Password,
            Phone = request.Phone,
            Address = request.Address,
            City = request.City,
            DoctorInfo = request.DoctorInfo,
            WorkingHoursJson = request.WorkingHoursJson
        });

        if (!result.IsSuccess)
        {
            // 503, not 400, when the deployment itself cannot complete a signup: a 400 tells every client and
            // proxy the call is malformed and not worth retrying, the opposite of what the message says.
            return result.Code == SignUpClinicCommandHandler.UnavailableCode
                ? HandleFailure(result, StatusCodes.Status503ServiceUnavailable)
                : HandleFailure(result);
        }

        // 202: the clinic does not exist yet and may never — the visitor still has to open their email.
        return Accepted(result.Value);
    }

    /// <summary>
    /// Consumes a verification token and provisions the clinic + its first admin.
    ///
    /// <para><b>Returns no session</b> — no access token, no cookie (AC-12). Receiving the email is not the same
    /// as knowing the password, and the password is the credential the visitor already chose; they sign in at
    /// <c>/login</c> with it.</para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("signup/verify")]
    public async Task<IActionResult> VerifySignUp([FromBody] ClinicSignUpVerifyRequest request)
    {
        if (!Deployment.AllowsPublicClinicSignup)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new VerifyClinicSignUpCommand { Token = request.Token });

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
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
            Password = request.Password,
            TotpCode = request.TotpCode
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            // ⚠️ The status now comes from the refusal's CODE, not a flat 401
            // (`hosted-security-hardening` FR-1.2). « Ce compte doit d'abord enrôler » is a **403**: a 401
            // reads to every client as « wrong password », and the one thing this refusal has to convey is
            // that the password was right and something else is owed.
            return RefuseAuth(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Enrols a second factor from the login screen itself (FR-1.3). Two calls: the first returns a secret to
    /// scan, the second confirms it with a code and returns the recovery codes <b>once</b>.
    ///
    /// <para><b>Anonymous by necessity</b> — an account told « enrol first » has no session, that being the
    /// point. It falls under <c>/api/auth</c>, so <c>RateLimiting.IsAnonymousAuthPath</c>'s prefix already gives
    /// it the tight per-account window and the <c>AuthAttemptAccount</c> capture; no list needed.</para>
    ///
    /// <para>It issues <b>no session</b>: enrolling is not signing in.</para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("totp/enrol")]
    public async Task<IActionResult> EnrolTotp([FromBody] EnrolTotpRequest request)
    {
        if (!Deployment.UsesLocalAccounts)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new EnrolTotpCommand
        {
            Email = request.Email,
            Password = request.Password,
            TotpCode = request.TotpCode
        });

        return result.IsSuccess ? Ok(result) : RefuseAuth(result);
    }

    /// <summary>
    /// Signs in with a single-use recovery code (FR-1.4) — the way back the user can take without anybody else.
    ///
    /// <para>Anonymous for the same reason as <see cref="EnrolTotp"/>, and inside the same rate-limit window.</para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("recovery")]
    public async Task<IActionResult> RedeemRecoveryCode([FromBody] RedeemRecoveryCodeRequest request)
    {
        if (!Deployment.UsesLocalAccounts)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new RedeemRecoveryCodeCommand
        {
            Email = request.Email,
            Password = request.Password,
            RecoveryCode = request.RecoveryCode
        });

        return result.IsSuccess ? Ok(result) : RefuseAuth(result);
    }

    /// <summary>
    /// « Sécurité » — this account's own second factor (FR-1.5). Reachable by <b>every</b> role: a doctor or a
    /// secretary may enrol voluntarily on any deployment, and the screen is where they do it.
    /// </summary>
    [HttpGet("totp")]
    public async Task<IActionResult> GetTotpState()
    {
        var result = await _mediator.Send(new GetTotpStateQuery());
        return result.IsSuccess ? Ok(result) : HandleFailure(result);
    }

    /// <summary>Replaces every recovery code with a fresh set. Requires a current code, not just the session.</summary>
    [HttpPost("totp/recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] TotpCodeRequest request)
    {
        var result = await _mediator.Send(new RegenerateRecoveryCodesCommand { TotpCode = request.TotpCode });
        return result.IsSuccess ? Ok(result) : HandleFailure(result);
    }

    /// <summary>
    /// Removes the second factor. Refused for an administrator <b>where the deployment requires one</b> — never
    /// on the role alone, which would strand a voluntarily-enrolled admin on the other two profiles.
    /// </summary>
    // A POST and not a DELETE: it carries a body (the current code, which is what authorises it), and DELETE
    // with a body is unevenly supported end to end — `apiDelete` in the web client sends none at all.
    [HttpPost("totp/disable")]
    public async Task<IActionResult> DisableTotp([FromBody] TotpCodeRequest request)
    {
        var result = await _mediator.Send(new DisableTotpCommand { TotpCode = request.TotpCode });
        return result.IsSuccess ? Ok(new { }) : HandleFailure(result);
    }

    /// <summary>
    /// Re-authenticates the signed-in user for one sensitive action (FR-1.8).
    ///
    /// <para>⚠️ It spends its <b>own</b> failure counter, never the login lockout: three wrong attempts refuse
    /// this action with the session untouched, because the user is already signed in and doing ordinary work.</para>
    /// </summary>
    [HttpPost("step-up")]
    public async Task<IActionResult> StepUp([FromBody] StepUpRequest request)
    {
        var result = await _mediator.Send(new StepUpCommand
        {
            Action = request.Action,
            Password = request.Password,
            TotpCode = request.TotpCode
        });

        return result.IsSuccess ? Ok(result) : HandleFailure(result);
    }

    /// <summary>Renders a refusal with the status its code carries.</summary>
    private IActionResult RefuseAuth(Result result) => HandleFailure(result, StatusForRefusal(result.Code));

    /// <summary>
    /// The HTTP status a clinic-auth refusal code carries.
    ///
    /// <para>⚠️ <b>The status is decided here and never derived inside the handler</b> — <c>Result.Code</c> says
    /// what happened, and how that maps onto HTTP is a presentation decision.
    /// <c>PlatformAuthController.StatusFor</c> is the same split for the other population.</para>
    ///
    /// <para>⚠️ An <b>unmapped</b> code falls to 401 rather than throwing: this is the sign-in endpoint, and a
    /// refusal nobody mapped must still be readable rather than a 500. <c>ClinicTotpAuthTests</c> asserts every
    /// code <c>ClinicAuthRefusals</c> declares is mapped here explicitly, so the fallback stays unreachable.</para>
    /// </summary>
    private static int StatusForRefusal(string? code) => code switch
    {
        ClinicAuthRefusals.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ClinicAuthRefusals.TotpRequired => StatusCodes.Status401Unauthorized,
        ClinicAuthRefusals.AccountDisabled => StatusCodes.Status401Unauthorized,
        // 403 and not 401: the password was correct. A 401 here reads as « wrong password » and would leave the
        // user retyping a credential that is already right, with no way to reach the enrolment step.
        ClinicAuthRefusals.TotpEnrolmentRequired => StatusCodes.Status403Forbidden,
        ClinicAuthRefusals.TotpInvalid => StatusCodes.Status400BadRequest,
        ClinicAuthRefusals.TotpNotEnrolled => StatusCodes.Status400BadRequest,
        ClinicAuthRefusals.TotpAlreadyEnrolled => StatusCodes.Status409Conflict,
        ClinicAuthRefusals.TooManyAttempts => StatusCodes.Status429TooManyRequests,
        ClinicAuthRefusals.PasswordPolicy => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status401Unauthorized
    };

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
