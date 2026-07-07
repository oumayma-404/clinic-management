using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/dental-records")]
[Authorize]
public class DentalRecordsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DentalRecordsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DentalRecordDto>>> GetDentalRecords(Guid patientId)
    {
        var query = new GetDentalRecordsQuery { PatientId = patientId };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<DentalRecordDto>> CreateDentalRecord(
        Guid patientId,
        [FromBody] CreateDentalRecordCommand command)
    {
        command.PatientId = patientId;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DentalRecordDto>> UpdateDentalRecord(
        Guid patientId,
        Guid id,
        [FromBody] UpdateDentalRecordCommand command)
    {
        command.Id = id;
        command.PatientId = patientId;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDentalRecord(Guid patientId, Guid id)
    {
        var command = new DeleteDentalRecordCommand
        {
            PatientId = patientId,
            Id = id
        };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}

