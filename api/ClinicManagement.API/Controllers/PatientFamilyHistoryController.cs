using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/family-history")]
[Authorize]
public class PatientFamilyHistoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public PatientFamilyHistoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientFamilyHistoryDto>>> GetFamilyHistory(Guid patientId)
    {
        var query = new GetPatientFamilyHistoryQuery { PatientId = patientId };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<PatientFamilyHistoryDto>> CreateFamilyHistory(
        Guid patientId,
        [FromBody] CreatePatientFamilyHistoryCommand command)
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
    public async Task<ActionResult<PatientFamilyHistoryDto>> UpdateFamilyHistory(
        Guid patientId,
        Guid id,
        [FromBody] UpdatePatientFamilyHistoryCommand command)
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
    public async Task<ActionResult> DeleteFamilyHistory(Guid patientId, Guid id)
    {
        var command = new DeletePatientFamilyHistoryCommand
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










