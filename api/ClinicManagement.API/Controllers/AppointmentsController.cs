using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AppointmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentDto>>> GetAppointments(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? patientId,
        [FromQuery] string? doctorName)
    {
        var query = new GetAppointmentsQuery
        {
            StartDate = startDate,
            EndDate = endDate,
            PatientId = patientId,
            DoctorName = doctorName
        };

        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AppointmentDto>> GetAppointment(Guid id)
    {
        var query = new GetAppointmentQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return NotFound(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> CreateAppointment([FromBody] CreateAppointmentCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AppointmentDto>> UpdateAppointment(Guid id, [FromBody] UpdateAppointmentCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAppointment(Guid id)
    {
        // For now, we'll use UpdateAppointmentCommand to cancel the appointment
        // In the future, we might want a dedicated DeleteAppointmentCommand
        var updateCommand = new UpdateAppointmentCommand
        {
            Id = id,
            Status = "Cancelled"
        };

        var result = await _mediator.Send(updateCommand);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}

