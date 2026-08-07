using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// The odontogram — charted diagnoses and completed treatments per tooth. <b><c>AnyClinicRole</c></b>: the chart
/// is part of the patient record the whole cabinet works from, and it was <c>AdminOrDoctor</c>, so the strip on
/// the patient page 403'd for a secretary before they had touched anything.
///
/// <para><see cref="RemoveCondition"/> deliberately inherits the class policy rather than tightening to
/// <c>AdminOrDoctor</c> like the clinical-record deletes: it removes a <b>charted diagnosis</b> — charting's own
/// undo, for the tooth someone just mis-clicked — and it cannot touch a treatment entry, which is edited through
/// the fiche de soins that produced it.</para>
/// </summary>
[ApiController]
[Route("api/patients/{patientId:guid}/odontogram")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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
