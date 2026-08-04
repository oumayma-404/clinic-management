using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/dental-records")]
[Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
public class DentalRecordsController : ApiControllerBase
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
            return HandleFailure(result);
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
            return HandleFailure(result);
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
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete a fiche de soins. `AdminOrDoctor` — a fiche is the clinical assertion an invoice line and a devis
    /// act are built from, and deleting it detaches both, so it belongs to the same class as amending a plan or
    /// cancelling an issued invoice. The class-level <c>[Authorize]</c> was the only gate (audit adjacent defect
    /// A-12), which let a secretary destroy clinical records.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
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
            return HandleFailure(result);
        }

        return NoContent();
    }
}

