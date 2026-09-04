using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Domain.ValueObjects;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/procedure-types")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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
    /// <param name="category">
    /// Narrow to one clinical discipline. Applied in SQL alongside <paramref name="search"/>, for the same reason:
    /// narrowing an already-cut page would shrink pages unpredictably. An unrecognised value matches nothing
    /// rather than failing, so a stale bookmark shows an empty list, not an error.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Application.DTOs.ProcedureTypeDto>>> GetProcedureTypes(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null)
    {
        var query = new GetProcedureTypesQuery
        {
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search,
            Category = category
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

        // ⚠️ A BODY, not `NoContent()`. The two outcomes — archived because a future rendez-vous still refers to
        // the act, or deleted permanently — are decided server-side from usage, and the screen has no way to know
        // which happened: the row simply vanished either way, so a permanent delete was indistinguishable from a
        // deactivation on the one action that cannot be undone.
        return Ok(new { archived = result.Value });
    }

    /// <summary>
    /// Backfill the clinic's procedure menu with the common Tunisian dental procedures (idempotent — skips any
    /// whose name already exists), and give the starter protocols the clinic has not edited their step
    /// intervals. Returns both counts: a run can legitimately add nothing and update many protocols.
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

        return Ok(new { added = result.Value.Added, protocolsUpdated = result.Value.ProtocolsUpdated });
    }

    /// <summary>
    /// The agenda-colour palette the <c>ColorHex</c> value object accepts, grouped by hue family and named.
    /// </summary>
    /// <remarks>
    /// Grouped rather than flat because the catalogue outgrew the ten colours this used to serve: the picker shows
    /// one swatch per family and its nuances only once a family is chosen. Named because the client used to hold
    /// its own hex→French map, so a colour added here appeared under its raw hex until somebody updated that copy.
    /// </remarks>
    [HttpGet("colors")]
    public ActionResult<List<ProcedureColorFamilyDto>> GetAvailableColors()
    {
        return Ok(ColorHex.GetPalette().ToDto());
    }

    /// <summary>
    /// The categories to offer when filing or filtering an act: the suggested clinical disciplines plus every
    /// category this clinic has invented for itself.
    /// </summary>
    /// <remarks>
    /// Served rather than shipped as a browser constant because half the list is data — only the server knows
    /// which categories the clinic has actually used, and a suggestion list missing them is what makes an admin
    /// retype one and shard the group. Sibling of <c>GET colors</c>, which serves the palette for the same reason.
    /// </remarks>
    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
    {
        var result = await _mediator.Send(new GetProcedureTypeCategoriesQuery());

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}

