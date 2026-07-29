using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.LabOrders.Commands;
using ClinicManagement.Application.Features.LabOrders.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Dental lab / prosthetics work orders (bons de laboratoire / prothèse). CRUD + status transitions
/// over a clinic's lab orders, optionally scoped to a patient. Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/lab-orders")]
[Authorize]
public class LabOrdersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public LabOrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the clinic's lab work orders, or a single patient's when patientId is given (newest first).</summary>
    /// <param name="status">Optional stage filter (Sent / InProgress / Received / Fitted). An unknown value is
    /// ignored rather than refused, so a stale deep link lands on the full list instead of an error.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LabWorkOrderDto>>> GetLabWorkOrders(
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null)
    {
        var result = await _mediator.Send(new GetLabWorkOrdersQuery { PatientId = patientId, Status = status });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a new lab work order for a patient.</summary>
    [HttpPost]
    public async Task<ActionResult<LabWorkOrderDto>> CreateLabWorkOrder([FromBody] CreateLabWorkOrderCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update an existing lab work order.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LabWorkOrderDto>> UpdateLabWorkOrder(Guid id, [FromBody] UpdateLabWorkOrderCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Move a lab work order to a new lab stage (Sent / InProgress / Received / Fitted).</summary>
    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<LabWorkOrderDto>> UpdateLabWorkOrderStatus(Guid id, [FromBody] UpdateLabWorkOrderStatusCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Delete a lab work order.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteLabWorkOrder(Guid id)
    {
        var result = await _mediator.Send(new DeleteLabWorkOrderCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
