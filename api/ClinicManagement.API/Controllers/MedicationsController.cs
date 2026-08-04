using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Medications.Commands;
using ClinicManagement.Application.Features.Medications.Queries;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/medications")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class MedicationsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public MedicationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get the DB-backed medication catalog, optionally filtered by free-text query. Global reference data
    /// (not clinic-scoped); requires an authenticated user. Active-only by default (the ordonnance picker).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<MedicationDto>>> GetMedications(
        [FromQuery] string? q = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new GetMedicationsQuery
        {
            Q = q,
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize
        };
        var result = await _mediator.Send(query);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a catalog entry. AdminOnly.</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<MedicationDto>> CreateMedication([FromBody] CreateMedicationRequest request)
    {
        var command = new CreateMedicationCommand
        {
            BrandName = request.BrandName,
            Form = request.Form,
            Strength = request.Strength,
            Dcis = request.Dcis ?? new List<string>(),
        };
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update a catalog entry. AdminOnly.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<MedicationDto>> UpdateMedication(Guid id, [FromBody] UpdateMedicationRequest request)
    {
        var command = new UpdateMedicationCommand
        {
            Id = id,
            BrandName = request.BrandName,
            Form = request.Form,
            Strength = request.Strength,
            Dcis = request.Dcis ?? new List<string>(),
        };
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Deactivate (soft-delete) a catalog entry. AdminOnly. A missing id is a genuine not-found (404).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeactivateMedication(Guid id)
    {
        var result = await _mediator.Send(new DeactivateMedicationCommand { Id = id });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : NoContent();
    }

    /// <summary>Confirm the provisional dataset (clears "à vérifier" on all entries). AdminOnly.</summary>
    [HttpPost("confirm")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ConfirmData()
    {
        var result = await _mediator.Send(new ConfirmMedicationDataCommand());
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
