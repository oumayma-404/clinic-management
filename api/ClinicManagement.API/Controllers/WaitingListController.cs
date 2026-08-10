using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.WaitingList.Commands;
using ClinicManagement.Application.Features.WaitingList.Queries;

using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Authorization;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Clinic waiting list (salle d'attente / liste d'attente). CRUD over the clinic's waiting-list entries,
/// plus promoting an entry to a real appointment. Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/waiting-list")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class WaitingListController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public WaitingListController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the clinic's waiting-list entries (highest priority first); activeOnly keeps only those still waiting.</summary>
    [HttpGet]
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter, applied in SQL <b>before</b> the page is cut so it spans the whole clinic.
    /// </param>
    public async Task<ActionResult<PagedResult<WaitingListEntryDto>>> GetWaitingList(
        [FromQuery] bool activeOnly = true,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetWaitingListQuery
        {
            ActiveOnly = activeOnly,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Add a patient to the waiting list.</summary>
    [HttpPost]
    public async Task<ActionResult<WaitingListEntryDto>> CreateWaitingListEntry([FromBody] CreateWaitingListEntryCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update a waiting-list entry (priority, preferred doctor, desired timeframe, note).</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WaitingListEntryDto>> UpdateWaitingListEntry(Guid id, [FromBody] UpdateWaitingListEntryCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Promote a waiting-list entry to a real appointment (optionally linking the resulting appointment).</summary>
    [HttpPost("{id:guid}/promote")]
    public async Task<ActionResult<WaitingListEntryDto>> PromoteWaitingListEntry(Guid id, [FromBody] PromoteWaitingListEntryCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// « Retirer de la liste » — the patient stopped waiting (AC-25). Keeps the row and records the outcome,
    /// unlike the delete below, which destroys the evidence that they ever waited.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<WaitingListEntryDto>> CancelWaitingListEntry(Guid id)
    {
        var result = await _mediator.Send(new CancelWaitingListEntryCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Remove a waiting-list entry — for a mistaken row. To record that a patient stopped waiting, cancel.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWaitingListEntry(Guid id)
    {
        var result = await _mediator.Send(new DeleteWaitingListEntryCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
