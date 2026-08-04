using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/medical-history")]
[Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
public class PatientMedicalHistoryController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PatientMedicalHistoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientMedicalHistoryDto>>> GetMedicalHistory(Guid patientId)
    {
        var query = new GetPatientMedicalHistoryQuery { PatientId = patientId };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<PatientMedicalHistoryDto>> CreateMedicalHistory(
        Guid patientId,
        [FromBody] CreatePatientMedicalHistoryCommand command)
    {
        command.PatientId = patientId;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PatientMedicalHistoryDto>> UpdateMedicalHistory(
        Guid patientId,
        Guid id,
        [FromBody] UpdatePatientMedicalHistoryCommand command)
    {
        command.Id = id;
        command.PatientId = patientId;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMedicalHistory(Guid patientId, Guid id)
    {
        var command = new DeletePatientMedicalHistoryCommand
        {
            PatientId = patientId,
            Id = id
        };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }
}










