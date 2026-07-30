using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Application.Features.CnamNomenclature.Queries;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/cnam-nomenclature")]
[Authorize]
public class CnamNomenclatureController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public CnamNomenclatureController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get the DB-backed CNAM dental nomenclature, optionally filtered by free-text query and/or category.
    /// Global reference data (not clinic-scoped); requires an authenticated user. Active-only by default.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CnamNomenclatureEntryDto>>> GetNomenclature(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new GetCnamNomenclatureQuery
        {
            Q = q,
            Category = category,
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize
        };
        var result = await _mediator.Send(query);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Get the valeurs de la lettre clé (VLC). Any authenticated user (FR-5.3).</summary>
    [HttpGet("letter-values")]
    public async Task<ActionResult<IEnumerable<CnamLetterValueDto>>> GetLetterValues()
    {
        var result = await _mediator.Send(new GetCnamLetterValuesQuery());
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Indicative reimbursement estimate for a single act (FR-5.5). Any authenticated user (editor aid).
    /// Never persisted / never printed.
    /// </summary>
    [HttpGet("reimbursement-estimate")]
    public async Task<ActionResult<ReimbursementEstimateDto>> GetReimbursementEstimate(
        [FromQuery] string lettreCle,
        [FromQuery] decimal coefficient,
        [FromQuery] DateTime? patientDateOfBirth = null,
        [FromQuery] DateTime? careDate = null)
    {
        var query = new GetReimbursementEstimateQuery
        {
            LettreCle = lettreCle,
            Coefficient = coefficient,
            PatientDateOfBirth = patientDateOfBirth,
            CareDate = careDate,
        };
        var result = await _mediator.Send(query);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Indicative reimbursement estimates for <b>all acts of one bulletin</b>, in one round trip (AC-P6.15).
    /// Any authenticated user (editor aid). Never persisted / never printed.
    /// <para>
    /// A <c>POST</c> for a read, deliberately: the acts are a list, and a GET would have to encode N cotations
    /// plus N care dates into the query string. It mutates nothing — the sibling single-act GET above is what a
    /// cacheable one-act lookup uses.
    /// </para>
    /// </summary>
    [HttpPost("reimbursement-estimates")]
    public async Task<ActionResult<IEnumerable<ReimbursementEstimateDto>>> GetReimbursementEstimates(
        [FromBody] GetReimbursementEstimatesQuery query)
    {
        var result = await _mediator.Send(query);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a catalog entry. AdminOnly (FR-5.3/5.4).</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<CnamNomenclatureEntryDto>> CreateEntry([FromBody] CreateCnamEntryRequest request)
    {
        var command = new CreateCnamEntryCommand
        {
            CodeActe = request.CodeActe,
            DesignationFr = request.DesignationFr,
            LettreCle = request.LettreCle,
            Coefficient = request.Coefficient,
            Category = request.Category,
        };
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update a catalog entry. AdminOnly.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<CnamNomenclatureEntryDto>> UpdateEntry(Guid id, [FromBody] UpdateCnamEntryRequest request)
    {
        var command = new UpdateCnamEntryCommand
        {
            Id = id,
            CodeActe = request.CodeActe,
            DesignationFr = request.DesignationFr,
            LettreCle = request.LettreCle,
            Coefficient = request.Coefficient,
            Category = request.Category,
        };
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Deactivate (soft-delete) a catalog entry. AdminOnly. A missing id is a genuine not-found (404).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeactivateEntry(Guid id)
    {
        var result = await _mediator.Send(new DeactivateCnamEntryCommand { Id = id });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : NoContent();
    }

    /// <summary>Confirm the provisional dataset (clears "à vérifier" on all entries + VLC). AdminOnly.</summary>
    [HttpPost("confirm")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ConfirmData()
    {
        var result = await _mediator.Send(new ConfirmCnamDataCommand());
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }

    /// <summary>Update a VLC value. AdminOnly (FR-5.2).</summary>
    [HttpPut("letter-values/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<CnamLetterValueDto>> UpdateLetterValue(Guid id, [FromBody] UpdateCnamLetterValueRequest request)
    {
        var command = new UpdateCnamLetterValueCommand { Id = id, Value = request.Value };
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
