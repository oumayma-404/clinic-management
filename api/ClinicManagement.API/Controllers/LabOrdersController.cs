using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.LabOrders.Commands;
using ClinicManagement.Application.Features.LabOrders.Queries;

using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Dental lab / prosthetics work orders (bons de laboratoire / prothèse). CRUD + status transitions
/// over a clinic's lab orders, optionally scoped to a patient. Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/lab-orders")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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

    /// <summary>
    /// « Exporter » (L5) — the same list, as a CSV.
    ///
    /// <para>⚠️ It re-sends the <b>identical query with no paging</b>, which the paging primitive models as a
    /// first-class case rather than as a huge page. That is what makes « honours the current filters, exports the
    /// whole filtered set, never the current page » true by construction instead of by discipline — the export
    /// cannot see a page to accidentally export.</para>
    /// </summary>
    [HttpGet("export")]
    public async Task<ActionResult> ExportLabWorkOrders(
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] string? sortBy = null)
    {
        var result = await _mediator.Send(new GetLabWorkOrdersQuery
        {
            PatientId = patientId,
            Status = status,
            SearchTerm = search,
            SupplierId = supplierId,
            SortBy = sortBy,
        });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.LabOrders(result.Value!.Items), "bons-de-prothese");
    }

    [HttpGet]
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter, applied in SQL <b>before</b> the page is cut so it spans the whole clinic.
    /// </param>
    public async Task<ActionResult<PagedResult<LabWorkOrderDto>>> GetLabWorkOrders(
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] string? sortBy = null)
    {
        var result = await _mediator.Send(new GetLabWorkOrdersQuery
        {
            PatientId = patientId,
            Status = status,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search,
            SupplierId = supplierId,
            SortBy = sortBy
        });
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
