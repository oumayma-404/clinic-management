using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// « Journal » — the console's own access ledger (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para><b>Read-only by construction</b>, exactly as <c>AuditController</c> is: rows are written only by the
/// reads and writes they record, and there is no action here that creates, edits or deletes one. A ledger with a
/// write endpoint is a ledger somebody can correct.</para>
///
/// <para>⚠️ <b>It is a console read, not a clinic one.</b> Showing a practice which vendor account opened its file
/// is named out of scope by the spec, so this lives behind the console's own listener and policy and has no
/// counterpart under <c>/api/audit</c>.</para>
///
/// <para>⚠️ Its DTOs join <c>PlatformReadShape</c> like every other console response — the ledger's subject is a
/// console account and a cabinet, so nothing it returns can name anybody at the practice.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformAccessLogController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformAccessLogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// One page of the ledger, newest first.
    /// </summary>
    /// <param name="accountId">Narrow to one console account. The options come back on the response itself
    /// (<c>actors</c>), derived from the rows so an account that has opened nothing is never offered.</param>
    /// <param name="clinicId">Narrow to one cabinet — « qui a ouvert la fiche de ce cabinet ? ».</param>
    /// <param name="page">1-based. Omitting it gets the first page; this read is never unbounded.</param>
    /// <param name="pageSize">Clamped to <c>PageRequest.MaxPageSize</c>.</param>
    [HttpGet("access-log")]
    public async Task<ActionResult<PlatformAccessLogPageDto>> GetAccessLog(
        [FromQuery] Guid? accountId = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPlatformAccessLogQuery
            {
                PlatformAccountId = accountId,
                ClinicId = clinicId,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
