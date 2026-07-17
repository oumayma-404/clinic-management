using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Domain.ValueObjects;

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
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Application.DTOs.ProcedureTypeDto>>> GetProcedureTypes([FromQuery] bool includeInactive = false)
    {
        var query = new GetProcedureTypesQuery { IncludeInactive = includeInactive };
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
    /// Delete (soft delete) a procedure type
    /// </summary>
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
    /// Get available color palette
    /// </summary>
    [HttpGet("colors")]
    public IActionResult GetAvailableColors()
    {
        var colors = ColorHex.GetAvailableColors().ToList();
        return Ok(colors);
    }
}

