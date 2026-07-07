using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Stock.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize]
public class StockController : ControllerBase
{
    private readonly IMediator _mediator;

    public StockController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all stock items for the current user's clinic.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StockItemDto>>> GetStockItems([FromQuery] bool lowStockOnly = false)
    {
        var result = await _mediator.Send(new GetStockItemsQuery { LowStockOnly = lowStockOnly });

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new stock item.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<StockItemDto>> CreateStockItem([FromBody] CreateStockItemCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Update an existing stock item.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<StockItemDto>> UpdateStockItem(Guid id, [FromBody] UpdateStockItemCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete a stock item.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteStockItem(Guid id)
    {
        var result = await _mediator.Send(new DeleteStockItemCommand { Id = id });

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}
