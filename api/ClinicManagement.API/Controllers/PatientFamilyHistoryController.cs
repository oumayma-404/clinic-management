using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// A patient's antécédents familiaux. <b><c>AnyClinicRole</c> to read and record, <c>AdminOrDoctor</c> to delete</b>
/// — the sibling of <see cref="PatientMedicalHistoryController"/>, and gated identically for the same reason:
/// <c>POST /api/patients</c> already creates these rows at <c>AnyClinicRole</c>.
/// </summary>
[ApiController]
[Route("api/patients/{patientId}/family-history")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PatientFamilyHistoryController : ApiControllerBase
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
            return HandleFailure(result);
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
            return HandleFailure(result);
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
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete an antécédent familial. <c>AdminOrDoctor</c>, matching its medical-history sibling: recording is
    /// reception's job, erasing a piece of the clinical picture is not.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
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
            return HandleFailure(result);
        }

        return NoContent();
    }
}










