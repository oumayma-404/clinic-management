using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Application.Features.Expenses.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Clinic expenses / caisse cash-out. CRUD over the clinic's expense entries; the caisse net figure is
/// on the billing controller (« caisse »). Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ExpensesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the clinic's expenses, optionally within a [from, to) date range (newest first).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseDto>>> GetExpenses([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var result = await _mediator.Send(new GetExpensesQuery { From = from, To = to });
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
    public async Task<IActionResult> DeleteExpense(Guid id)
    {
        var result = await _mediator.Send(new DeleteExpenseCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
