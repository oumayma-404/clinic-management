using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Stock.Queries;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize]
public class StockController : ApiControllerBase
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
            return HandleFailure(result);
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
            return HandleFailure(result);
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
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Movement history (sorties/entrées) for a stock item, newest-first.</summary>
    [HttpGet("{id:guid}/movements")]
    public async Task<ActionResult<IEnumerable<StockMovementDto>>> GetMovements(Guid id)
    {
        var result = await _mediator.Send(new GetStockMovementsQuery { StockItemId = id });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : Ok(result.Value);
    }

    /// <summary>Record a stock consumption (sortie) — decrements by a delta and audits the movement.</summary>
    [HttpPost("{id:guid}/consume")]
    public async Task<ActionResult<StockItemDto>> ConsumeStock(Guid id, [FromBody] ConsumeStockCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Record a stock replenishment (entrée) — increments by a delta, optionally with expiry/batch.</summary>
    [HttpPost("{id:guid}/restock")]
    public async Task<ActionResult<StockItemDto>> RestockStock(Guid id, [FromBody] RestockStockItemCommand command)
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
    /// Delete a stock item.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteStockItem(Guid id)
    {
        var result = await _mediator.Send(new DeleteStockItemCommand { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }
}
