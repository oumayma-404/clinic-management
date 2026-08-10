using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// The vendor's portfolio: every cabinet's activity, and the counts above it (<c>platform-console</c> US-2).
///
/// <para><b>Read-only, and structurally so.</b> There is no action here that writes anything — the three writes
/// this console will ever have (grant a period, correct one, suspend a cabinet) belong to Part 4 and to
/// <c>features/clinic-subscription/</c>. A vendor surface that can read every practice must be able to prove what
/// it does with that reach, and « it has no write path » is the strongest form of that proof.</para>
///
/// <para>⚠️ <b>What these actions may return is a closed set</b> (AC-7.2): <c>PlatformReadShape</c> declares
/// every scalar name allowed on this surface, and <c>PlatformReadShapeTests</c> recurses into the response types
/// and fails the build on a leaf outside it. That — not the tenant query filter, which is deliberately
/// <i>lifted</i> here — is what makes « nous ne pouvons pas voir vos dossiers patients » a property of the code
/// (AC-7.2a).</para>
///
/// <para>⚠️ Reachable only on the console's own Kestrel listener: <c>ConsolePortGate</c> 404s
/// <c>/api/platform/*</c> on the public port and 404s every console path when <c>Console:Port</c> is 0.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformPortfolioController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformPortfolioController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// One page of the portfolio, with each cabinet's activity beside it.
    /// </summary>
    /// <param name="dormant">« Rien enregistré depuis 30 jours ». A cabinet never covered by the counter pass is
    /// deliberately not matched — see <c>PlatformPortfolioFilter</c>.</param>
    /// <param name="q">Matches the cabinet's name, its city or an administrator's e-mail address (AC-2.5).</param>
    /// <param name="sort">`name` | `activity` | `createdAt`. An unrecognised value falls back to `name`.</param>
    /// <param name="page">1-based. Omitting it gets the first page — this read is never unbounded.</param>
    /// <param name="pageSize">Clamped to <c>PageRequest.MaxPageSize</c>.</param>
    [HttpGet("clinics")]
    public async Task<ActionResult<PlatformClinicPageDto>> ListClinics(
        [FromQuery] bool dormant = false,
        [FromQuery] string? q = null,
        [FromQuery] string? sort = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListPlatformClinicsQuery { Dormant = dormant, Q = q, Sort = sort, Page = page, PageSize = pageSize },
            cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>The counts above the list (AC-2.7), read over the whole portfolio rather than over a page.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<PlatformSummaryDto>> GetSummary(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPlatformSummaryQuery(), cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
