using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.DentalActs.Commands;
using ClinicManagement.Application.Features.DentalActs.Queries;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/dental-acts")]
[Authorize]
public class DentalActsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public DentalActsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the global dental act catalog (chapitre DCH). Any authenticated user; active-only by default.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<DentalActDto>>> GetDentalActs(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await _mediator.Send(new GetDentalActsQuery
        {
            Q = q,
            Category = category,
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a catalog entry. AdminOnly.</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<DentalActDto>> CreateAct([FromBody] CreateDentalActCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update a catalog entry. AdminOnly.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<DentalActDto>> UpdateAct(Guid id, [FromBody] UpdateDentalActCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Deactivate (soft-delete) a catalog entry. AdminOnly. A missing id is a genuine not-found (404).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeactivateAct(Guid id)
    {
        var result = await _mediator.Send(new DeactivateDentalActCommand { Id = id });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : NoContent();
    }

    /// <summary>Confirm the provisional dataset (clears "à vérifier" on all acts). AdminOnly.</summary>
    [HttpPost("confirm")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ConfirmData()
    {
        var result = await _mediator.Send(new ConfirmDentalActsCommand());
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
