using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Audit.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Journal d'activité » — the audit ledger (I6). Read-only by construction: rows are written by
/// <c>AuditSaveChangesInterceptor</c> and there is no endpoint that creates, edits or deletes one. A ledger with a
/// write endpoint is a ledger somebody can correct.
/// </summary>
[ApiController]
[Route("api/audit")]
// The whole feature is for the owner: « qui a supprimé ce patient ? » is their question, and every other role
// appears *in* the ledger rather than reading it.
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class AuditController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AuditController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// One page of the clinic's ledger, newest first, plus the entity types it has rows for.
    /// </summary>
    /// <param name="entityType">A CLR aggregate name as offered by the response's <c>entityTypes</c>.</param>
    /// <param name="entityId">One record's whole history.</param>
    /// <param name="from">Inclusive first day, in the <b>clinic's</b> calendar (Tunisia, UTC+1).</param>
    /// <param name="to">Inclusive last day, same zone.</param>
    /// <param name="action">`Insert` | `Update` | `Delete`. An unrecognised value is ignored, not refused.</param>
    /// <param name="userId">One actor's entries — « qu'a fait cette personne ? ». An unknown id yields no rows.</param>
    /// <param name="page">1-based. Omitting it gets the first page — this read is never unbounded.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    [HttpGet]
    public async Task<ActionResult<AuditPageDto>> GetAuditEntries(
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? action = null,
        [FromQuery] string? userId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetAuditEntriesQuery
            {
                EntityType = entityType,
                EntityId = entityId,
                From = from,
                To = to,
                Action = action,
                UserId = userId,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
