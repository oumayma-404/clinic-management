using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// A patient's antécédents médicaux. <b><c>AnyClinicRole</c> to read and record, <c>AdminOrDoctor</c> to delete.</b>
///
/// <para>The controller was <c>AdminOrDoctor</c> while <c>POST /api/patients</c> — <c>AnyClinicRole</c> — has
/// always inserted rows into <b>this very table</b> (<c>CreatePatientCommand</c>'s
/// <c>MedicalHistoryEntries</c>), and <c>PUT /api/patients/{id}</c> writes the <c>Patient.MedicalHistory</c> free
/// text. So the gate never described the data it guarded; it only decided which door reception had to use.</para>
/// </summary>
[ApiController]
[Route("api/patients/{patientId}/medical-history")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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

    /// <summary>
    /// Delete an antécédent. <c>AdminOrDoctor</c> — this is where an <b>allergy</b> is recorded, and removing one
    /// is the one edit on this controller whose consequence is a clinical decision taken later on information
    /// that is no longer there. A typo stays correctable by <see cref="UpdateMedicalHistory"/>, which is open.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
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










