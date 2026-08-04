using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Application.Features.Expenses.Queries;

using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Clinic expenses / caisse cash-out. CRUD over the clinic's expense entries; the caisse net figure is
/// on the billing controller (« caisse »). Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/expenses")]
[Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
public class ExpensesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ExpensesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the clinic's expenses, optionally within a [from, to) date range (newest first).</summary>
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
    /// whole filtered set, never the current page » true by construction instead of by discipline — the export
    /// cannot see a page to accidentally export.</para>
    /// </summary>
    [HttpGet("export")]
    public async Task<ActionResult> ExportExpenses(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetExpensesQuery { From = from, To = to, SearchTerm = search });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Expenses(result.Value!.Items), "depenses");
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetExpenses(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetExpensesQuery
        {
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Record a new expense.</summary>
    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> CreateExpense([FromBody] CreateExpenseCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update an existing expense.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> UpdateExpense(Guid id, [FromBody] UpdateExpenseCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Delete an expense.</summary>
    [HttpDelete("{id:guid}")]
    // Deleting a dépense silently *raises* the reported Net, and no screen shows what used to be there — the
    // exact shape of change the audit ledger (I6) exists to answer « qui a fait ça ? » about.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeleteExpense(Guid id)
    {
        var result = await _mediator.Send(new DeleteExpenseCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
