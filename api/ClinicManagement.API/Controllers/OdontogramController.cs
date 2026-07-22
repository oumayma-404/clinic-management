using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId:guid}/odontogram")]
[Authorize]
public class OdontogramController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public OdontogramController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get a patient's odontogram: all recorded tooth conditions (many-per-tooth) — both charted diagnoses
    /// and completed treatments (from dental records).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ToothStateDto>>> GetOdontogram(Guid patientId)
    {
        var result = await _mediator.Send(new GetOdontogramQuery { PatientId = patientId });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : Ok(result.Value);
    }

    /// <summary>Chart a diagnosis on a tooth (existing pathology / à traiter), before any treatment.</summary>
    [HttpPost("conditions")]
    public async Task<ActionResult<ToothStateDto>> DiagnoseTooth(Guid patientId, [FromBody] DiagnoseToothInput input)
    {
        var result = await _mediator.Send(new DiagnoseToothCommand
        {
            PatientId = patientId,
            ToothNumber = input.ToothNumber,
            Condition = input.Condition,
            Surfaces = input.Surfaces,
            Note = input.Note
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Remove a charted diagnosis (diagnosis entries only; treatments are edited via their record).</summary>
    [HttpDelete("conditions/{toothStateId:guid}")]
    public async Task<IActionResult> RemoveCondition(Guid patientId, Guid toothStateId)
    {
        var result = await _mediator.Send(new RemoveToothConditionCommand
        {
            PatientId = patientId,
            ToothStateId = toothStateId
        });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : NoContent();
    }
}
