using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Ancien logiciel » — a patient's history as the cabinet's previous software recorded it. Read by the patient
/// page; written only by the migration import (append). <c>AnyClinicRole</c> like the rest of the clinical
/// record: the import signs in as a secretary account, and recording is open while erasing is not — there is
/// deliberately no update and no delete.
/// </summary>
[ApiController]
[Route("api/patients/{patientId}/legacy-history")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PatientLegacyHistoryController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PatientLegacyHistoryController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientLegacyHistoryEntryDto>>> GetLegacyHistory(Guid patientId)
    {
        var result = await _mediator.Send(new GetPatientLegacyHistoryQuery { PatientId = patientId });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<object>> AddLegacyHistory(
        Guid patientId, [FromBody] AddPatientLegacyHistoryCommand command)
    {
        command.PatientId = patientId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(new { added = result.Value });
    }
}
