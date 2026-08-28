using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Meta.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// What a client must be to use this server (AC-28). Anonymous by necessity: a shell below the floor has to be
/// able to ask <i>before</i> signing in, and the whole point of the answer is to reach a client the rest of the
/// API is refusing.
///
/// <para>That is also why <c>ClientVersionMiddleware</c> exempts this one route (AC-29) — without the exemption
/// the single route that says where to update would be the single route a stale client cannot read.</para>
/// </summary>
[ApiController]
[Route(RoutePrefix)]
// Only `client-requirements` is [AllowAnonymous]; the class policy covers everything else added here.
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class MetaController : ApiControllerBase
{
    private const string RoutePrefix = "api/meta";
    private const string ClientRequirementsRoute = "client-requirements";
    private const string ClientDownloadRoute = "client-download";

    /// <summary>
    /// The absolute path <c>ClientVersionMiddleware</c> exempts from the floor. Stated once, here, because the
    /// exemption and the route have to be the same string — a rename that moved only one of them would leave the
    /// update instructions unreachable by exactly the clients they exist for.
    /// </summary>
    public const string ClientRequirementsPath = "/" + RoutePrefix + "/" + ClientRequirementsRoute;

    /// <summary>
    /// The download's absolute path, exempted from the floor for the same reason as the route above and stated
    /// here for the same reason: a shell already refused with 426 must be able to fetch the very file that stops
    /// it being refused. Announcing an update a client cannot then download would be the whole feature undone.
    /// </summary>
    public const string ClientDownloadPath = "/" + RoutePrefix + "/" + ClientDownloadRoute;

    private readonly IConfiguration _configuration;
    private readonly IMediator _mediator;

    public MetaController(IConfiguration configuration, IMediator mediator)
    {
        _configuration = configuration;
        _mediator = mediator;
    }

    [AllowAnonymous]
    [HttpGet(ClientRequirementsRoute)]
    // Both actions here are GETs, so the subscription gate never inspects them — the attribute is documentation,
    // stating the exempt set as *what* rather than leaving a reader to re-derive « is a GET refused? ». It is
    // therefore NOT load-bearing, and SubscriptionExemptionCoverageTests (non-GET only) cannot fail on its removal.
    [AllowsWithoutSubscription("Not clinic work — the shells' version floor, which a refused client must be able to read.")]
    public IActionResult ClientRequirements()
    {
        var requirements = Models.ClientRequirements.Read(_configuration);
        var package = ClientUpdatePackage.Resolve(_configuration, AppContext.BaseDirectory);

        if (package is null)
        {
            return Ok(requirements);
        }

        // ⚠️ **The configured URL WINS.** An operator who has deliberately pointed clients at their own mirror
        // must not be silently overridden by a file that happens to sit beside the server — and on an upgrade
        // path where both exist, the explicit setting is the one somebody chose.
        var url = string.IsNullOrWhiteSpace(requirements.StoreUrls.Windows)
            ? BuildDownloadUrl()
            : requirements.StoreUrls.Windows;

        // The hash is only ours to publish when the bytes are ours to serve: pairing a locally-computed hash with
        // an operator's own URL would refuse every download from that mirror.
        var sha = string.IsNullOrWhiteSpace(requirements.StoreUrls.Windows)
            ? package.Sha256
            : requirements.WindowsSetupSha256;

        // `CurrentShellVersion` likewise defers to the operator, but where nothing is set the shipped package IS
        // the current release — which is what lets « upgrade the server » be the only act.
        var current = string.IsNullOrWhiteSpace(requirements.CurrentShellVersion)
            ? package.Version
            : requirements.CurrentShellVersion;

        return Ok(requirements with
        {
            CurrentShellVersion = current,
            StoreUrls = requirements.StoreUrls with { Windows = url },
            WindowsSetupSha256 = sha,
        });
    }

    /// <summary>
    /// The Windows client installer this server shipped with (see <see cref="ClientUpdatePackage"/>). Anonymous
    /// and floor-exempt, exactly like the requirements route that points at it.
    /// </summary>
    [AllowAnonymous]
    [HttpGet(ClientDownloadRoute)]
    [AllowsWithoutSubscription("Not clinic work — the installer a refused client needs in order to stop being refused.")]
    public IActionResult ClientDownload()
    {
        var package = ClientUpdatePackage.Resolve(_configuration, AppContext.BaseDirectory);
        if (package is null)
        {
            // 404 rather than an empty 200: the shell reports the status, and « this deployment ships no
            // installer » must not read as « the installer is zero bytes ».
            return NotFound(new { error = "Aucun programme d'installation client n'est disponible sur ce serveur." });
        }

        // ⚠️ `enableRangeProcessing` so an interrupted 50 MB download resumes rather than restarting, on a LAN
        // where the transfer competes with everything else the practice is doing.
        return PhysicalFile(package.FullPath, "application/octet-stream", package.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// An absolute URL for this server, built from the request the shell actually reached us on — never from a
    /// configured host. The shell may have arrived on a LAN IP, a hostname, or through the front-door proxy, and
    /// the address it used is the only one it is known to be able to reach.
    /// </summary>
    private string BuildDownloadUrl() =>
        $"{Request.Scheme}://{Request.Host}{ClientDownloadPath}";

    /// <summary>
    /// What the patient-file door accepts, projected from the catalog (AC-5.1). Deliberately <b>not</b> exempt
    /// from the client-version floor: only <c>client-requirements</c> earns that, being the answer a refused
    /// client needs in order to stop being refused.
    /// </summary>
    [HttpGet("upload-policy")]
    [AllowsWithoutSubscription("Not clinic work — what the file picker may offer, read on a screen an expired cabinet still opens.")]
    public async Task<ActionResult<Application.DTOs.UploadPolicyDto>> UploadPolicy(
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetUploadPolicyQuery(), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
