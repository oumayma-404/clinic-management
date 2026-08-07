using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Fiches de soins. <b><c>AnyClinicRole</c> for reading and recording, <c>AdminOrDoctor</c> to delete.</b>
///
/// <para>This controller was <c>AdminOrDoctor</c> outright, which made « Dossiers médicaux » — the tab, not just
/// its buttons — return 403 for a secretary. That boundary was never true in the code it was drawn around:
/// <c>PUT /api/patients/{id}</c> is <c>AnyClinicRole</c> and writes <c>Allergies</c>, <c>MedicalHistory</c> and
/// the patient's notes, and <c>POST /api/patients</c> inserts <c>PatientMedicalHistory</c> rows — so reception
/// could always type a patient's medical history through « Modifier » and was refused reading it one tab over.
/// Practice settled it: in a Tunisian cabinet the assistant(e) is who fills much of the record in.</para>
///
/// <para>Recording is open; <b>erasing is not</b> (see <see cref="DeleteDentalRecord"/>). Every write is
/// attributable — <c>AuditSaveChangesInterceptor</c> stamps the actor on the aggregate — and the practitioner
/// credited is resolved by <c>PractitionerAttribution</c>, which puts the caller <b>last</b>, so a secretary
/// recording a dentist's work never credits themselves.</para>
/// </summary>
[ApiController]
[Route("api/patients/{patientId}/dental-records")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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
    ///
    /// <para>⚠️ Now that the class admits every clinic role, this attribute is the <b>only</b> thing gating the
    /// delete — it is load-bearing, not the redundant restatement of the class it used to be. Recording a visit
    /// is reversible by editing; destroying the record it was billed from is not.</para>
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

