using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Application.Common.Authorization;

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
    /// Get patients for the current user's clinic, optionally filtered by a search term (name / phone)
    /// and capped at <paramref name="limit"/>.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientDto>>> GetPatients(
        [FromQuery] string? searchTerm = null,
        [FromQuery] int? limit = null)
    {
        var query = new GetPatientsQuery { SearchTerm = searchTerm, Limit = limit };
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

    /// <summary>Delete a patient. 400 with a clear message if related records block it.</summary>
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
