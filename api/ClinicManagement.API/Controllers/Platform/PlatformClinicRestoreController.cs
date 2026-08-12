using ClinicManagement.API.Filters;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// The vendor re-creates a cabinet from the archive its owner kept
/// (<c>clinic-data-archive-and-restore</c>) — the console's second write, and the only path that works when the
/// practice's own accounts are gone too.
///
/// <para>⚠️ <b>Its own controller, following <c>PlatformSubscriptionsController</c>'s precedent for the same
/// reason.</b> <c>PlatformPortfolioController</c> claims to be read-only *by construction*, and that claim stops
/// being checkable the moment one action on it writes. This one is not a subscription action either — no money
/// changed hands and no entitlement was extended — so folding it in there would make « the console's writes are
/// about entitlements » false as well.</para>
///
/// <para>⚠️ <b>It carries <c>[AllowsWithoutSubscription]</c></b>, on the same argument as the payment routes and
/// a stronger one: a cabinet being restored has usually just lapsed *because* it was gone, and a console account
/// is not a cabinet in any case.</para>
///
/// <para>⚠️ Reachable only on the console's own Kestrel listener: <c>ConsolePortGate</c> 404s
/// <c>/api/platform/*</c> on the public port and 404s every console path when <c>Console:Port</c> is 0.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformClinicRestoreController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public PlatformClinicRestoreController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    /// <summary>
    /// Re-creates the cabinet at the archive's <b>own</b> clinic id, restores its rows and blobs, and mints one
    /// administrator whose password is shown once.
    ///
    /// <para>⚠️ <b>A cabinet that is still live is a 409 <c>clinic_exists</c></b>, not a merge: the practice's own
    /// admin can restore it themselves from « Paramètres », with their own eyes on the result, and the vendor
    /// minting a second administrator into a working practice is the wrong move whatever the archive says.</para>
    ///
    /// <para>⚠️ The one-time password is in the response <b>once</b> and is stored nowhere readable —
    /// <c>platform-account create</c>'s shape. The account must change it on first use.</para>
    /// </summary>
    [HttpPost("clinics/restore")]
    [DisableRequestSizeLimit]
    [ArchiveUploadLimit]
    [AllowsWithoutSubscription(
        "Re-creating a cabinet that no longer exists cannot wait on that cabinet's entitlement — there is none "
        + "to read, and this is the action that gives it one.")]
    public async Task<ActionResult<PlatformClinicRestoredDto>> RestoreClinic(
        [FromForm] IFormFile archive,
        [FromForm] string adminEmail,
        [FromForm] string adminFullName,
        CancellationToken cancellationToken = default)
    {
        if (archive == null || archive.Length == 0)
        {
            return Failure("Aucun fichier n'a été envoyé.");
        }

        var maxMb = ArchiveUploadLimit.MaxSizeMb(_configuration);

        // ⚠️ The same ceiling the cabinet door applies. This one had none at all — only « is it empty? » — so the
        // sibling endpoint's cap did not reach it, and it is the door reached precisely when nobody at the
        // practice can act.
        if (archive.Length > ArchiveUploadLimit.MaxBytes(_configuration))
        {
            return Failure($"L'archive dépasse la taille maximale acceptée ({maxMb} Mo).");
        }

        await using var stream = archive.OpenReadStream();

        var result = await _mediator.Send(
            new RestoreClinicFromArchiveCommand
            {
                Archive = stream,
                AdminEmail = adminEmail,
                AdminFullName = adminFullName,
            },
            cancellationToken);

        if (result.IsFailure)
        {
            // 409 for « the cabinet is still there » and 400 for every archive fault: the console branches on the
            // code, but the status has to be right for the ordinary HTTP reader too — a live cabinet is a conflict
            // with the current state, not a malformed request.
            return HandleFailure(
                result,
                result.Code == ClinicArchiveFormat.ClinicExistsCode
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest);
        }

        return Ok(result.Value);
    }
}
