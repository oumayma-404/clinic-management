using ClinicManagement.API.Authorization;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.API.Startup;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// What the PC de secours itself calls (<c>clinic-pc-copy</c>): pairing and its token (anonymous, rate-limited), then the
/// heartbeat and the copy under its own <c>clinic-relay</c> token. A person's token cannot reach any of it.
/// </summary>
[ApiController]
[Route("api/relay")]
[Authorize(Policy = AuthorizationPolicies.ClinicRelayPeer)]
[AllowedWhileCloudFenced("The PC's own channel: it is how a silent PC comes back, confirms its acks and stands down.")]
public class RelayPeerController : ApiControllerBase
{
    /// <summary>The PC's build, sent on every copy call so a mismatch is refused before any row moves (D10).</summary>
    public const string BuildHeader = "X-Relay-Build";

    /// <summary>The relay secret travels in a header, never in a URL (this application's URLs are logged).</summary>
    public const string SecretHeader = "X-Relay-Secret";

    /// <summary>The installer's SHA-256 (hex), beside its bytes: the Windows app runs nothing elevated without it.</summary>
    public const string InstallerSha256Header = "X-Content-SHA256";

    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;
    private readonly IConfiguration _configuration;
    private readonly IRelayBuildInfo _build;
    private readonly TrustedProxies _trustedProxies;

    public RelayPeerController(
        IMediator mediator, DeploymentProfile deployment, IConfiguration configuration, IRelayBuildInfo build,
        TrustedProxies trustedProxies)
    {
        _mediator = mediator;
        _deployment = deployment;
        _configuration = configuration;
        _build = build;
        _trustedProxies = trustedProxies;
    }

    /// <summary>
    /// The server installer of THIS build (D10): what the Windows app runs to make a PC the cabinet's PC de secours,
    /// and what a PC de secours runs to follow a cloud update. Anonymous like the pairing it precedes — the installer is
    /// the product every cabinet already gets, and the one-time code is what makes it a PC de secours.
    ///
    /// <para>⚠️ <b>404 until the installer of this exact build is published</b> — a PC set up one deploy behind could
    /// never copy. The Windows app reads that 404 as « pas encore », never as a connection problem.</para>
    /// </summary>
    [HttpGet("installer")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RelayTokenPolicy)]
    [AllowsWithoutSubscription("The installer of a PC that will hold the clinic's own records.")]
    public IActionResult Installer()
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var package = RelayInstallerPackage.Find(
            RelayInstallerPackage.ResolveFolder(_configuration, AppContext.BaseDirectory), _build.Current);
        if (package is null)
        {
            return Failure(RelayRefusals.InstallerUnavailable, StatusCodes.Status404NotFound);
        }

        Response.Headers[InstallerSha256Header] = package.Sha256;
        Response.Headers[BuildHeader] = package.Build;
        return PhysicalFile(package.FullPath, "application/octet-stream", package.FileName, enableRangeProcessing: true);
    }

    [HttpPost("pair")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RelayTokenPolicy)]
    [AllowsWithoutSubscription("A PC that will hold the clinic's own records may always be set up to copy them.")]
    public async Task<ActionResult<RelayPairingDto>> Pair([FromBody] PairRelayRequest request, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new PairRelayCommand(
            request.Code ?? string.Empty, request.Label ?? string.Empty, request.PublicKey ?? string.Empty,
            request.CertificateFingerprint, request.LanAddresses, request.Build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RelayTokenPolicy)]
    [AllowsWithoutSubscription("The copy of a cabinet's own records keeps running whatever its subscription says (FR-1).")]
    public async Task<ActionResult<RelayTokenDto>> Token(
        [FromBody] RelayTokenRequest request,
        [FromHeader(Name = SecretHeader)] string? secret,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new ExchangeRelayTokenCommand(request.RelayId, secret ?? string.Empty), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// A retired PC says « Effacer la copie » is done (AC-8.2). Its own secret, no token — the exchange refuses a retired
    /// PC, and only a retired PC can send this. It records a date and a journal row, nothing else.
    /// </summary>
    [HttpPost("erased")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RelayTokenPolicy)]
    [AllowsWithoutSubscription("Recording that a retired PC no longer holds the cabinet's records is never new work.")]
    public async Task<IActionResult> Erased(
        [FromBody] RelayTokenRequest request,
        [FromHeader(Name = SecretHeader)] string? secret,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new ReportRelayErasedCommand(request.RelayId, secret ?? string.Empty), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : NoContent();
    }

    /// <summary>
    /// The PC is being uninstalled (AC-8.3): it counts as retiring it, and the journal says so. Its own secret, no token —
    /// a PC retired earlier gets no token either, and is often the one being uninstalled.
    /// </summary>
    [HttpPost("uninstalled")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RelayTokenPolicy)]
    [AllowsWithoutSubscription("Recording that a PC no longer runs the copy is never new work.")]
    public async Task<IActionResult> Uninstalled(
        [FromBody] RelayTokenRequest request,
        [FromHeader(Name = SecretHeader)] string? secret,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new ReportRelayUninstalledCommand(request.RelayId, secret ?? string.Empty), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : NoContent();
    }

    [HttpPost("heartbeat")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    [AllowsWithoutSubscription("The PC's report about itself records no work of the cabinet's.")]
    public async Task<ActionResult<RelayHeartbeatAck>> Heartbeat(
        [FromBody] RelayHeartbeatRequest request, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(
            new RelayHeartbeatCommand(request, ClientIp.Resolve(HttpContext, _trustedProxies)), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// D16: the PC's long poll — the numbers it just kept, for the next ones a save is about to make final (≤ 20 s).
    /// A save waits ≤ 3 s for this round trip before committing, so the PC re-opens it at once.
    /// </summary>
    [HttpPost("promises")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    [AllowsWithoutSubscription("The PC keeping a number the cloud is about to issue records no work of its own.")]
    public async Task<ActionResult<IReadOnlyList<RelayNumberPromiseDto>>> Promises(
        [FromBody] RelayPromisesRequest request, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new ExchangeRelayPromisesCommand(request.Acks), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// D18 phase 1: the PC hands the cut's work back — rows, journal, sign-in traces — applied once in one transaction.
    /// The cloud stays fenced until the PC's next heartbeat says it stopped holding.
    /// </summary>
    [HttpPost("handback")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    [RequestSizeLimit(HandbackBodyLimit)]
    [AllowsWithoutSubscription("The cabinet's own work of a cut reaches the cloud whatever its subscription says (EC-15).")]
    public async Task<ActionResult<RelayHandbackResultDto>> HandBack(
        [FromBody] RelayHandbackRequest request,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new HandBackRelayCommand(request, build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// US-7 / AC-7.3: a PC whose cut « Reprendre la main » overruled sends what it recorded — listed « À reprendre »,
    /// never applied. Once per cut.
    /// </summary>
    [HttpPost("handback/overruled")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    [RequestSizeLimit(HandbackBodyLimit)]
    [AllowsWithoutSubscription("Listing what a cut recorded so it can be entered again records no new work.")]
    public async Task<ActionResult<RelayHandbackResultDto>> ListOverruledCut(
        [FromBody] RelayHandbackRequest request,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new ListOverruledCutCommand(request, build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>D18: which of the files the cut's rows name the cloud does not hold yet.</summary>
    [HttpPost("handback/files")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    [AllowsWithoutSubscription("Asking which files of a cut are missing records nothing.")]
    public async Task<ActionResult<IReadOnlyList<string>>> HandbackFiles(
        [FromBody] RelayHandbackFilesRequest request,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(
            new GetMissingHandbackFilesQuery(request.Keys ?? Array.Empty<string>(), build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>D18: one file made during the cut, stored under the key its row names — never over an existing one.</summary>
    [HttpPut("handback/file")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    [DisableRequestSizeLimit]
    [AllowsWithoutSubscription("A file the cabinet made during a cut reaches the cloud whatever its subscription says (EC-15).")]
    public async Task<IActionResult> HandbackFile(
        [FromQuery] string? key,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(
            new StoreHandbackFileCommand(key ?? string.Empty, Request.Body, build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : NoContent();
    }

    /// <summary>A cut's rows as JSON — a day of a busy cabinet is a few megabytes; this leaves a wide margin.</summary>
    public const long HandbackBodyLimit = 512L * 1024 * 1024;

    [HttpGet("changes")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    public async Task<ActionResult<RelayFeedBatch>> Changes(
        [FromQuery] long after,
        [FromQuery] string? fingerprint,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetRelayChangesQuery(after, fingerprint, build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    [HttpGet("snapshot")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    public async Task<IActionResult> Snapshot(
        [FromQuery] string? tables,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var only = string.IsNullOrWhiteSpace(tables)
            ? null
            : tables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = await _mediator.Send(new GetRelaySnapshotQuery(only, build), cancellationToken);
        return result.IsFailure
            ? HandleFailure(result, RelayController.StatusFor(result.Code))
            : File(result.Value!, "application/gzip", "snapshot.json.gz");
    }

    [HttpGet("digest")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    public async Task<ActionResult<IReadOnlyList<RelayTableDigest>>> Digest(
        [FromHeader(Name = BuildHeader)] string? build, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetRelayDigestQuery(build), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

    [HttpGet("blob")]
    [AcceptsScopedToken(LocalAuthScopes.ClinicRelay)]
    public async Task<IActionResult> Blob(
        [FromQuery] string? key,
        [FromHeader(Name = BuildHeader)] string? build,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetRelayBlobQuery(key ?? string.Empty, build), cancellationToken);
        return result.IsFailure
            ? HandleFailure(result, RelayController.StatusFor(result.Code))
            : File(result.Value!, "application/octet-stream");
    }
}

public sealed record PairRelayRequest(
    string? Code, string? Label, string? PublicKey, string? CertificateFingerprint, string? LanAddresses, string? Build);

public sealed record RelayTokenRequest(Guid RelayId);
