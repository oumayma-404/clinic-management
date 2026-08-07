using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Stock.Queries;
using Microsoft.AspNetCore.Http;

using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter. Applied in SQL <b>before</b> the page is cut, so it searches the whole clinic — a
    /// search that only saw the current page would answer a different question from the one that was typed.
    /// </param>

    /// <summary>
    /// « Exporter » (L5) — the same list, as a CSV.
    ///
    /// <para>⚠️ It re-sends the <b>identical query with no paging</b>, which the paging primitive models as a
    /// first-class case rather than as a huge page. That is what makes « honours the current filters, exports the
    /// whole filtered set, never the current page » true by construction rather than by discipline.</para>
    /// </summary>
    [HttpGet("export")]
    public async Task<ActionResult> ExportStock(
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] bool expiringOnly = false)
    {
        var result = await _mediator.Send(new GetStockItemsQuery
        {
            LowStockOnly = lowStockOnly,
            SearchTerm = search,
            Category = category,
            ExpiringOnly = expiringOnly,
        });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Stock(result.Value!.Items), "stock");
    }

    [HttpGet]
    public async Task<ActionResult<StockPageDto>> GetStockItems(
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] bool expiringOnly = false)
    {
        var result = await _mediator.Send(new GetStockItemsQuery
        {
            LowStockOnly = lowStockOnly,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search,
            Category = category,
            ExpiringOnly = expiringOnly
        });

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
    // Takes the article's whole movement history with it — the one operation here whose effect cannot be read
    // off any screen afterwards, because the screen that would show it is what disappears.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
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
