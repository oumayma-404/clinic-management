using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// Support actions the vendor performs on a cabinet's <b>accounts</b> rather than on its contract — the two ways
/// back into an account whose owner cannot get in: clearing a second factor whose authenticator is lost
/// (<c>hosted-security-hardening</c> FR-1.4), and replacing a forgotten password.
///
/// <para>⚠️ <b>They are deliberately two calls, each with its own journal row.</b> Somebody who talks support into
/// one still cannot sign in — a password without the six-digit code is useless, and a cleared factor without the
/// password equally so. Offering « réinitialiser les deux » would collapse two independent proofs into a single
/// phone call, which is precisely the attack the split defends against.</para>
///
/// <para>⚠️ <b>Its own controller, and not a route on <c>PlatformSubscriptionsController</c>.</b> That file's
/// heading is « the vendor records a payment … stops a cabinet for abuse », and Part 6's own note records what
/// happens when an action is filed next to the wrong neighbours: a « Suspendre » button beside the payment history
/// reads as a billing lever, and a vendor who reads it that way reaches for a cancellation instead. A second-factor
/// reset is neither money nor discipline — it is somebody ringing to say they dropped their phone in a sink — and
/// filing it under « Abonnement et paiements » would invite exactly the wrong mental model on the one action whose
/// misuse hands an account to whoever asked for it.</para>
///
/// <para>⚠️ <b><c>[AllowsWithoutSubscription]</c> is load-bearing here, not defensive.</b> The person who cannot
/// sign in is very often the sole administrator of a cabinet whose cover lapsed <i>because</i> nobody could sign in
/// to pay. Refusing the reset on that ground would make the lockout self-sustaining, which is the deadlock the
/// subscription gate's own exemption list exists to prevent.</para>
///
/// <para>⚠️ Reachable only on the console's own Kestrel listener: <c>ConsolePortGate</c> 404s
/// <c>/api/platform/*</c> on the public port and 404s every console path when <c>Console:Port</c> is 0.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformClinicSecurityController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformClinicSecurityController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Clears one clinic account's second factor so its owner can enrol a new one at their next sign-in.
    ///
    /// <para>⚠️ <b>A POST on a sub-resource, never a <c>DELETE</c> on the factor.</b> What happens is not only a
    /// removal: a journal row is written, the account's sessions end and the person is notified — and the account is
    /// expected to hold a factor again within minutes. <c>DELETE</c> would advertise a tidier operation than this
    /// is.</para>
    ///
    /// <para>⚠️ <b>The cabinet is in the URL and the person in the body.</b> The console has no roster endpoint —
    /// deliberately, so this feature adds nothing to what the vendor can <i>read</i> about a practice's staff — so
    /// the address comes from the telephone call. Putting the cabinet in the path is what stops a mis-keyed address
    /// reaching an account at a practice the vendor never opened.</para>
    ///
    /// <para>⚠️ The refusals a client acts on differently carry <b>codes</b>: an unknown cabinet or account is a
    /// 404, and « pas de second facteur enrôlé » is a state of the world (409) rather than a rejected request.
    /// None is recovered by matching the French sentence.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/second-factor/reset")]
    [AllowsWithoutSubscription(
        "The account that cannot sign in is frequently the sole administrator of a cabinet whose cover lapsed "
        + "because nobody could sign in to pay for it. Gating this on the entitlement would make that lockout "
        + "permanent by construction.")]
    public async Task<ActionResult<PlatformSecondFactorResetDto>> ResetSecondFactor(
        Guid clinicId,
        [FromBody] ResetClinicUserSecondFactorRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ResetClinicUserSecondFactorFromConsoleCommand
            {
                ClinicId = clinicId,
                Email = request.Email,
                Reason = request.Reason,
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Code switch
        {
            ResetClinicUserSecondFactorFromConsoleCommandHandler.UnknownClinicCode
                => NotFound(new { error = result.Error, code = result.Code }),
            ResetClinicUserSecondFactorFromConsoleCommandHandler.UnknownAccountCode
                => NotFound(new { error = result.Error, code = result.Code }),
            ResetClinicUserSecondFactorFromConsoleCommandHandler.NotEnrolledCode
                => Conflict(new { error = result.Error, code = result.Code }),
            _ => BadRequest(new { error = result.Error }),
        };
    }

    /// <summary>
    /// Replaces one clinic account's password with a fresh temporary one, returned once for the vendor to read back
    /// over the telephone.
    ///
    /// <para>⚠️ <b>Filed here rather than on <c>PlatformSubscriptionsController</c></b> for this file's own stated
    /// reason: this is somebody ringing to say they cannot get in, not money and not discipline, and a password
    /// reset sitting under « Abonnement et paiements » would invite the billing mental model on the second of the
    /// two actions whose misuse hands an account to whoever asked for it.</para>
    ///
    /// <para>⚠️ <b>A POST on a sub-resource, never a <c>PUT</c> on the password.</b> What happens is not only a
    /// replacement: a journal row is written, the account's sessions end, the person is notified, and a credential
    /// the vendor must relay comes back exactly once. <c>PUT</c> would advertise something idempotent, and a
    /// retried <c>PUT</c> here silently invalidates the password the vendor just read out.</para>
    ///
    /// <para>⚠️ The refusals a client acts on differently carry <b>codes</b>: an unknown cabinet or account is a
    /// 404, and « ce compte n'a pas de mot de passe local » is a state of the world (409) rather than a rejected
    /// request. None is recovered by matching the French sentence.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/password/reset")]
    [AllowsWithoutSubscription(
        "The account that cannot sign in is frequently the sole administrator of a cabinet whose cover lapsed "
        + "because nobody could sign in to pay for it. Gating this on the entitlement would make that lockout "
        + "permanent by construction — the same reasoning as the second-factor reset above, and it applies at "
        + "least as strongly to the credential somebody is more likely to forget.")]
    public async Task<ActionResult<PlatformPasswordResetDto>> ResetPassword(
        Guid clinicId,
        [FromBody] ResetClinicUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ResetClinicUserPasswordFromConsoleCommand
            {
                ClinicId = clinicId,
                Email = request.Email,
                Reason = request.Reason,
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Code switch
        {
            ResetClinicUserPasswordFromConsoleCommandHandler.UnknownClinicCode
                => NotFound(new { error = result.Error, code = result.Code }),
            ResetClinicUserPasswordFromConsoleCommandHandler.UnknownAccountCode
                => NotFound(new { error = result.Error, code = result.Code }),
            ResetClinicUserPasswordFromConsoleCommandHandler.NotLocalAccountCode
                => Conflict(new { error = result.Error, code = result.Code }),
            _ => BadRequest(new { error = result.Error }),
        };
    }
}
