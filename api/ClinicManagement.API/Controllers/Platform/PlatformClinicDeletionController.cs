using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// Deleting a cabinet for good (<c>clinic-account-removal</c>) — the console's only irreversible action, and the
/// only one in this product that destroys a practice's records rather than adjusting what it may do.
///
/// <para><b>⚠️ Its own controller, on <c>PlatformClinicSecurityController</c>'s stated reasoning and more
/// sharply.</b> Filed under « Abonnement et paiements » a « Supprimer » route reads as a billing lever, which is
/// the mental model whose misuse here cannot be undone. It has no neighbours on purpose.</para>
///
/// <para><b>⚠️ Two calls, and the read is what makes the write safe.</b> The preview is what the panel states
/// before the vendor can type the name; both derive their figures from the deletion's own plan, so the sentence
/// somebody acts on cannot describe a smaller operation than the one that runs.</para>
///
/// <para>⚠️ Reachable only on the console's own Kestrel listener: <c>ConsolePortGate</c> 404s
/// <c>/api/platform/*</c> on the public port and 404s every console path when <c>Console:Port</c> is 0.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformClinicDeletionController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformClinicDeletionController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// What deleting this cabinet would remove: the named figures, the total, the files and their size, and the
    /// addresses that become free again.
    /// </summary>
    [HttpGet("clinics/{clinicId:guid}/deletion-preview")]
    public async Task<ActionResult<PlatformClinicDeletionPreviewDto>> Preview(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPlatformClinicDeletionPreviewQuery { ClinicId = clinicId }, cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Code == ClinicDeletionRefusals.UnknownClinicCode
            ? NotFound(new { error = result.Error, code = result.Code })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Deletes the cabinet. The body carries the cabinet's name as the vendor typed it and a mandatory motif.
    ///
    /// <para>⚠️ <b>A POST on a sub-resource, never <c>DELETE /clinics/{id}</c>.</b> A <c>DELETE</c> advertises
    /// something a mis-aimed client can perform with no body at all, and this call is refused without two of them —
    /// the typed name and the motif. It also does more than remove rows: it writes the journal row that is the only
    /// thing that will ever say the cabinet existed.</para>
    ///
    /// <para>⚠️ <b>The name is verified against the cabinet the id resolved to, server-side.</b> A client comparing
    /// two strings of its own would agree with itself; the failure being caught is a wrong row in the list.</para>
    ///
    /// <para>⚠️ The refusals a client acts on differently carry <b>codes</b>: an unknown cabinet is a 404, a
    /// mis-typed name and a missing motif are 400s with their own codes. None is recovered by matching the French
    /// sentence.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/delete")]
    [AllowsWithoutSubscription(
        "A cabinet whose cover lapsed is the likeliest one to be deleted — an abandoned trial is exactly what this "
        + "route exists to remove. Gating it on that cabinet's own entitlement would make the rows it left behind "
        + "permanent, and would push the deletion back to SSH where nothing is recorded at all.")]
    public async Task<ActionResult<PlatformClinicDeletedDto>> Delete(
        Guid clinicId,
        [FromBody] DeleteClinicRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new DeleteClinicFromConsoleCommand
            {
                ClinicId = clinicId,
                ConfirmationName = request.ConfirmationName,
                Reason = request.Reason,
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Code switch
        {
            ClinicDeletionRefusals.UnknownClinicCode
                => NotFound(new { error = result.Error, code = result.Code }),
            ClinicDeletionRefusals.NameMismatchCode
                => BadRequest(new { error = result.Error, code = result.Code }),
            ClinicDeletionRefusals.ReasonRequiredCode
                => BadRequest(new { error = result.Error, code = result.Code }),
            _ => BadRequest(new { error = result.Error }),
        };
    }
}
