using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Domain.ValueObjects;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/procedure-types")]
[Authorize]
public class ProcedureTypesController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ProcedureTypesController> _logger;

    public ProcedureTypesController(IMediator mediator, ILogger<ProcedureTypesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Get all procedure types
    /// </summary>
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter. Applied in SQL <b>before</b> the page is cut, so it searches the whole clinic — a
    /// search that only saw the current page would answer a different question from the one that was typed.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Application.DTOs.ProcedureTypeDto>>> GetProcedureTypes(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var query = new GetProcedureTypesQuery
        {
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a procedure type by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Application.DTOs.ProcedureTypeDto>> GetProcedureType(Guid id)
    {
        var query = new GetProcedureTypeQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new procedure type
    /// </summary>
    // Catalog WRITES are admin-only, matching the CNAM nomenclature, dental-act and medication catalogs —
    // this one was simply missed (audit § 2, finding 8). Procedure-type prices feed straight into what a
    // patient is charged. Reads stay open to all staff.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<Application.DTOs.ProcedureTypeDto>> CreateProcedureType([FromBody] CreateProcedureTypeCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetProcedureType), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// Update a procedure type
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{id}")]
    public async Task<ActionResult<Application.DTOs.ProcedureTypeDto>> UpdateProcedureType(Guid id, [FromBody] UpdateProcedureTypeCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Replace an act's material list — the stock performing it consumes (AC-P4.14). A separate endpoint from
    /// the PUT above because the list has replace semantics (an empty list clears it, which is the opt-out)
    /// while every field of the update command is null-means-unchanged.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{id}/materials")]
    public async Task<ActionResult<Application.DTOs.ProcedureTypeDto>> SetMaterials(
        Guid id, [FromBody] SetProcedureTypeMaterialsCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete (soft delete) a procedure type
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProcedureType(Guid id)
    {
        var command = new DeleteProcedureTypeCommand { Id = id };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }

    /// <summary>
    /// Backfill the clinic's procedure menu with the common Tunisian dental procedures (idempotent —
    /// skips any whose name already exists). Returns the number added.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("initialize-defaults")]
    public async Task<IActionResult> InitializeDefaults()
    {
        var result = await _mediator.Send(new InitializeDefaultProcedureTypesCommand());
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(new { added = result.Value });
    }

    /// <summary>
    /// Get available color palette
    /// </summary>
    [HttpGet("colors")]
    public IActionResult GetAvailableColors()
    {
        var colors = ColorHex.GetAvailableColors().ToList();
        return Ok(colors);
    }
}

