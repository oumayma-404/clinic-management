using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.Common.Models;
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
        [FromQuery] string? fromDay = null,
        [FromQuery] string? toDay = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(
            new GetExpensesQuery { FromDay = fromDay, ToDay = toDay, From = from, To = to, SearchTerm = search });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Expenses(result.Value!.Items), "depenses");
    }

    [HttpGet]
    /// <param name="fromDay">
    /// Bare <c>YYYY-MM-DD</c> clinic-local days, the form la caisse sends so its dépenses table covers the same
    /// Tunisian window as the totals and the extrait above it (AC-6). Omit every date for the whole list — unlike
    /// the caisse reads, « no window » here means « toutes les dépenses », not « aujourd'hui ».
    /// </param>
    /// <param name="toDay">See <paramref name="fromDay"/>; defaults to it.</param>
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetExpenses(
        [FromQuery] string? fromDay = null,
        [FromQuery] string? toDay = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetExpensesQuery
        {
            FromDay = fromDay,
            ToDay = toDay,
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

    /// <summary>
    /// « Dépenses mensuelles » — the clinic's standing monthly commitments (loyer, salaire, crédit).
    ///
    /// <para>No window and no paging, unlike every other read la caisse makes: a series is a standing
    /// instruction rather than period data. Active only — a stopped series is off the list.</para>
    /// </summary>
    [HttpGet("recurring")]
    public async Task<ActionResult<IReadOnlyList<RecurringExpenseDto>>> GetRecurringExpenses()
    {
        var result = await _mediator.Send(new GetRecurringExpensesQuery());
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Modify a monthly dépense. Future months only — the occurrences already posted are untouched.</summary>
    [HttpPut("recurring/{id:guid}")]
    public async Task<ActionResult<RecurringExpenseDto>> UpdateRecurringExpense(
        Guid id,
        [FromBody] UpdateRecurringExpenseCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result, NotFoundOrBadRequest(result)) : Ok(result.Value);
    }

    /// <summary>
    /// « Arrêter » a monthly dépense — the credit is paid off. Not a deletion: nothing already posted moves, and
    /// the series is kept, stopped, so the journal can still say what those dépenses were.
    /// </summary>
    [HttpPost("recurring/{id:guid}/stop")]
    public async Task<IActionResult> StopRecurringExpense(Guid id)
    {
        var result = await _mediator.Send(new StopRecurringExpenseCommand { Id = id });
        return result.IsFailure ? HandleFailure(result, NotFoundOrBadRequest(result)) : NoContent();
    }

    /// <summary>
    /// 404 for a series that is not this clinic's (or has been stopped), 400 for a refused field.
    ///
    /// <para>Branching on the <c>Code</c>, never on the sentence — a reworded French message must not change a
    /// status code. See <c>OdontogramController</c> for the same shape.</para>
    /// </summary>
    private static int NotFoundOrBadRequest(Result result) =>
        result.Code == UpdateRecurringExpenseCommand.NotFoundCode
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

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
