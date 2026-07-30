using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Domain.Common;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AppointmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all appointments for the current user's clinic
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentDto>>> GetAppointments(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? doctorId)
    {
        var query = new GetAppointmentsQuery
        {
            StartDate = startDate,
            EndDate = endDate,
            DoctorId = doctorId
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a single appointment by id (used by notification deep-links). Tenant-scoped: an appointment
    /// from another clinic reads as "not found".
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AppointmentDto>> GetAppointment(Guid id)
    {
        var result = await _mediator.Send(new GetAppointmentQuery { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new appointment
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> CreateAppointment([FromBody] CreateAppointmentCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetAppointments), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>List the clinic's recurring appointment series (active by default).</summary>
    [HttpGet("recurring")]
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter over the patient's name, the practitioner and the notes. Applied in SQL before the page
    /// is cut, so it spans the whole clinic.
    /// </param>
    public async Task<ActionResult<PagedResult<RecurringAppointmentDto>>> GetRecurringSeries(
        [FromQuery] bool activeOnly = true,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetRecurringSeriesQuery
        {
            ActiveOnly = activeOnly,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a recurring series; expands into linked appointments (returns created/skipped/conflict counts).</summary>
    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringSeriesResultDto>> CreateRecurringSeries([FromBody] CreateRecurringSeriesCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Cancel part or all of a recurring series (scope: Occurrence / Following / WholeSeries).</summary>
    [HttpPost("recurring/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRecurringSeries(Guid id, [FromBody] CancelRecurringSeriesCommand command)
    {
        command.RecurringAppointmentId = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(new { cancelled = result.Value });
    }

    /// <summary>
    /// Update an existing appointment (can be used to cancel by setting status to "Cancelled")
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<AppointmentDto>> UpdateAppointment(Guid id, [FromBody] UpdateAppointmentCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
