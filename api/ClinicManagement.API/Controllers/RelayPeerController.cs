using ClinicManagement.API.Authorization;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.API.Startup;
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
public class RelayPeerController : ApiControllerBase
{
    /// <summary>The PC's build, sent on every copy call so a mismatch is refused before any row moves (D10).</summary>
    public const string BuildHeader = "X-Relay-Build";

    /// <summary>The relay secret travels in a header, never in a URL (this application's URLs are logged).</summary>
    public const string SecretHeader = "X-Relay-Secret";

    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;

    public RelayPeerController(IMediator mediator, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _deployment = deployment;
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

        var result = await _mediator.Send(new RelayHeartbeatCommand(request), cancellationToken);
        return result.IsFailure ? HandleFailure(result, RelayController.StatusFor(result.Code)) : Ok(result.Value);
    }

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
