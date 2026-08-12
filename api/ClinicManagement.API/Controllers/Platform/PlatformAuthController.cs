using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// The vendor console's four authentication actions (<c>platform-console</c> US-1, AC-8.6).
///
/// <para><b>Reachable only on the console's own listener</b> — <see cref="ConsolePortGate"/> 404s
/// <c>/api/platform/*</c> on the public port and 404s everything else on the console one — and only where
/// <c>DeploymentProfile.ServesPlatformConsole</c> and <c>Console:Port</c> both say so. With the console off these
/// routes are <b>absent</b> rather than present-and-refusing (AC-1.8).</para>
///
/// <para><b>⚠️ Three actions are <c>[AllowAnonymous]</c> by necessity, not by concession.</b> Signing in,
/// enrolling the second factor and redeeming a recovery code are all things done by a caller who has no session
/// — that being the point. They are on <c>ControllerAuthorizationCoverageTests</c>' reviewed allow-list, and they
/// are inside the <b>anonymous-auth rate limits</b> per account and per address (AC-1.5), which is what
/// <c>RateLimiting.IsAnonymousAuthPath</c> was widened for. <c>password</c> stays out of both: it requires a
/// console session, and that is AC-8.6's whole shape.</para>
///
/// <para><b>⚠️ The status code comes from the refusal's <c>code</c>, never from its French sentence.</b> The spec
/// gives these four distinct statuses; recovering one by matching prose would mean rewording a message silently
/// changed a status — the <c>Contains("déjà facturée")</c> defect this codebase deleted. See
/// <see cref="StatusFor"/>.</para>
/// </summary>
[ApiController]
[Route("api/platform/auth")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformAuthController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformAuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// The password floor, so the console states the server's number instead of its own
    /// (<c>hosted-security-hardening</c> FR-1.9).
    ///
    /// <para><b>Authenticated, not anonymous</b> — its only reader is « Changer le mot de passe », which is
    /// behind a session, and the sign-in screen has no length rule to state (it checks a password, it does not
    /// choose one). Leaving it on the class policy also keeps it off
    /// <c>ControllerAuthorizationCoverageTests</c>' reviewed anonymous list, where every entry should be there
    /// because it must be.</para>
    /// </summary>
    [HttpGet("meta")]
    public async Task<ActionResult> Meta(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPlatformAuthMetaQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Refuse(result);
    }

    /// <summary>E-mail + password + a one-time code (AC-1.2).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    public async Task<ActionResult> Login([FromBody] PlatformLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new PlatformLoginCommand(request.Email, request.Password, request.TotpCode), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Refuse(result);
    }

    /// <summary>
    /// Binds the secret the bootstrap verb issued, proving a code generated from it (AC-1.3a). The recovery codes
    /// in the response are shown <b>once</b> and cannot be retrieved again.
    /// </summary>
    [HttpPost("totp/enrol")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    public async Task<ActionResult> EnrolTotp(
        [FromBody] PlatformEnrolTotpRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new EnrolPlatformTotpCommand(request.Email, request.Password, request.TotpCode), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Refuse(result);
    }

    /// <summary>Signs in with a recovery code when the authenticator is gone (AC-1.3b, EC-3).</summary>
    [HttpPost("recovery")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    public async Task<ActionResult> Recovery(
        [FromBody] PlatformRecoveryRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RedeemPlatformRecoveryCodeCommand(request.Email, request.Password, request.RecoveryCode),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Refuse(result);
    }

    /// <summary>
    /// Changes the signed-in account's <b>own</b> password — the only account action on the web (AC-8.6).
    /// Deliberately carries no method-level policy: the class's <c>PlatformConsole</c> is exactly right.
    /// </summary>
    [HttpPost("password")]
    public async Task<ActionResult> ChangePassword(
        [FromBody] PlatformChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ChangePlatformPasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);

        return result.IsSuccess ? Ok(new { }) : Refuse(result);
    }

    private ActionResult Refuse(Result result) => HandleFailure(result, StatusFor(result.Code));

    /// <summary>
    /// The HTTP status a refusal code carries, as the spec's API section states it.
    ///
    /// <para>⚠️ An <b>unrecognised</b> code falls to 400 rather than throwing: a new refusal that nobody mapped is
    /// a bad request the caller can read, not a 500 on the sign-in endpoint. <c>PlatformAuthStatusTests</c>
    /// asserts every code <c>PlatformAuthRefusals</c> declares is mapped here explicitly, so the fallback is a
    /// safety net rather than the route new codes quietly take.</para>
    /// </summary>
    public static int StatusFor(string? code) => code switch
    {
        PlatformAuthRefusals.InvalidCredentials => StatusCodes.Status401Unauthorized,
        PlatformAuthRefusals.TotpRequired => StatusCodes.Status401Unauthorized,
        PlatformAuthRefusals.AccountDisabled => StatusCodes.Status401Unauthorized,
        PlatformAuthRefusals.NoSession => StatusCodes.Status401Unauthorized,
        PlatformAuthRefusals.TotpEnrolmentRequired => StatusCodes.Status403Forbidden,
        PlatformAuthRefusals.TotpInvalid => StatusCodes.Status400BadRequest,
        PlatformAuthRefusals.PasswordPolicy => StatusCodes.Status400BadRequest,
        PlatformAuthRefusals.TotpAlreadyEnrolled => StatusCodes.Status409Conflict,
        PlatformAuthRefusals.TooManyAttempts => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest
    };
}

/// <summary>Sign-in body. <c>totpCode</c> is nullable so « omitted » and « wrong » stay different refusals.</summary>
public record PlatformLoginRequest(string Email, string Password, string? TotpCode);

public record PlatformEnrolTotpRequest(string Email, string Password, string TotpCode);

public record PlatformRecoveryRequest(string Email, string Password, string RecoveryCode);

public record PlatformChangePasswordRequest(string CurrentPassword, string NewPassword);
