using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Models;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PatientsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PatientsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get patients for the current user's clinic, optionally filtered by a search term (name / phone),
    /// by registration date, and capped at <paramref name="limit"/>.
    /// </summary>
    /// <param name="createdFrom">Inclusive lower bound on the registration date — backs the dashboard's
    /// « Nouveaux patients » drill-through, which must list exactly the patients that KPI counted.</param>
    /// <param name="createdTo">Inclusive upper bound on the registration date.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientDto>>> GetPatients(
        [FromQuery] string? searchTerm = null,
        [FromQuery] int? limit = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null)
    {
        var query = new GetPatientsQuery
        {
            SearchTerm = searchTerm,
            Limit = limit,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a patient by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PatientDto>> GetPatient(Guid id)
    {
        var query = new GetPatientQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new patient
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PatientDto>> CreatePatient([FromBody] CreatePatientCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetPatients), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// Update an existing patient
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<PatientDto>> UpdatePatient(Guid id, [FromBody] UpdatePatientCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete a patient. Refused with a message naming what is attached whenever anything is — the pre-check
    /// counts invoices and treatment plans explicitly, since neither has a foreign key to Patients and no
    /// database constraint has ever fired for them.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePatient(Guid id)
    {
        var result = await _mediator.Send(new DeletePatientCommand { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }

    /// <summary>
    /// What blocks this patient's deletion, and whether archiving is available instead. Read when the confirm
    /// dialog opens so the user learns the answer before clicking, not after.
    /// </summary>
    [HttpGet("{id}/deletion-check")]
    public async Task<ActionResult<PatientDeletionCheckDto>> GetDeletionCheck(Guid id)
    {
        var result = await _mediator.Send(new GetPatientDeletionCheckQuery { PatientId = id });

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Archive a patient — hidden from lists, search, recall and every picker, nothing destroyed, reversible.
    /// The escape hatch that keeps deletion refusable: this app has no merge and no soft delete, so without it
    /// a duplicate patient with a single booking could never be removed from the list. Refused when a balance
    /// is due or a visit is booked.
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<ActionResult<PatientDto>> ArchivePatient(Guid id, [FromBody] ArchivePatientRequest? request)
    {
        var result = await _mediator.Send(new ArchivePatientCommand { Id = id, Reason = request?.Reason });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Restore an archived patient everywhere.</summary>
    [HttpPost("{id}/unarchive")]
    public async Task<ActionResult<PatientDto>> UnarchivePatient(Guid id)
    {
        var result = await _mediator.Send(new UnarchivePatientCommand { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a live, AI-generated French summary of the patient (not persisted). Cross-clinic/missing
    /// patient → 404 (thrown NotFoundException); AI backend unavailable → 400 { error } (FR fallback on FE).
    /// </summary>
    [HttpGet("{patientId}/ai-summary")]
    public async Task<ActionResult<PatientAiSummaryDto>> GetAiSummary(Guid patientId)
    {
        var result = await _mediator.Send(new GetPatientAiSummaryQuery { PatientId = patientId });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
