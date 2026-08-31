using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.DentalActs.Commands;
using ClinicManagement.Application.Features.DentalActs.Queries;
using ClinicManagement.API.Models;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/dental-acts")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class DentalActsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public DentalActsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the global dental act catalog (chapitre DCH). Any authenticated user; active-only by default.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<DentalActDto>>> GetDentalActs(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await _mediator.Send(new GetDentalActsQuery
        {
            Q = q,
            Category = category,
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a catalog entry. AdminOnly.</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<DentalActDto>> CreateAct([FromBody] CreateDentalActCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update a catalog entry. AdminOnly.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<DentalActDto>> UpdateAct(Guid id, [FromBody] UpdateDentalActCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Deactivate (soft-delete) a catalog entry. AdminOnly. A missing id is a genuine not-found (404).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeactivateAct(Guid id)
    {
        var result = await _mediator.Send(new DeactivateDentalActCommand { Id = id });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : NoContent();
    }

    /// <summary>
    /// Reactivate an entry switched off by mistake. AdminOnly. A missing id is a genuine not-found (404).
    ///
    /// <para>⚠️ A separate route rather than a flag on the DELETE, so no existing caller changes and the inverse of
    /// a soft delete is a thing a client can point at. Without it, cet acte désactivé par erreur ne revenait jamais —
    /// the entity's own <c>Activate()</c> was unreachable from the product.</para>
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ReactivateAct(Guid id)
    {
        var result = await _mediator.Send(new DeactivateDentalActCommand { Id = id, Deactivate = false });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : NoContent();
    }

    /// <summary>Confirm the provisional dataset (clears "à vérifier" on all acts and every VLC). AdminOnly.</summary>
    [HttpPost("confirm")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ConfirmData()
    {
        var result = await _mediator.Send(new ConfirmDentalActsCommand());
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }

    // ── Valeurs de la lettre clé (VLC) ──────────────────────────────────────────────────────────────────
    //
    // These four moved here from `api/cnam-nomenclature` when feature single-act-catalogue retired that
    // controller. They belong beside the catalogue they value: a cotation is meaningless without its lettre clé.

    /// <summary>Read the valeurs de la lettre clé. Any authenticated user (the estimate depends on them).</summary>
    [HttpGet("letter-values")]
    public async Task<ActionResult<IEnumerable<CnamLetterValueDto>>> GetLetterValues()
    {
        var result = await _mediator.Send(new GetCnamLetterValuesQuery());
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Update one valeur de la lettre clé. AdminOnly. A missing id is a genuine not-found (404).</summary>
    [HttpPut("letter-values/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<CnamLetterValueDto>> UpdateLetterValue(
        Guid id, [FromBody] UpdateCnamLetterValueRequest request)
    {
        var result = await _mediator.Send(new UpdateCnamLetterValueCommand
        {
            Id = id,
            Value = request.Value,
            Version = request.Version,
        });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : Ok(result.Value);
    }

    /// <summary>
    /// Indicative reimbursement estimate for a single act. Any authenticated user (editor aid).
    /// Never persisted, never printed.
    /// </summary>
    [HttpGet("reimbursement-estimate")]
    public async Task<ActionResult<ReimbursementEstimateDto>> GetReimbursementEstimate(
        [FromQuery] string lettreCle,
        [FromQuery] decimal coefficient,
        [FromQuery] DateTime? patientDateOfBirth = null,
        [FromQuery] DateTime? careDate = null)
    {
        var result = await _mediator.Send(new GetReimbursementEstimateQuery
        {
            LettreCle = lettreCle,
            Coefficient = coefficient,
            PatientDateOfBirth = patientDateOfBirth,
            CareDate = careDate,
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Indicative estimates for <b>all acts of one bulletin</b>, in one round trip. Any authenticated user.
    ///
    /// <para>A <c>POST</c> for a read, deliberately: the acts are a list, and a GET would have to encode N
    /// cotations plus N care dates into the query string. It mutates nothing.</para>
    /// </summary>
    [HttpPost("reimbursement-estimates")]
    [AllowsWithoutSubscription("AC-4.9 — computes and persists nothing; a POST only because the acts are a list.")]
    public async Task<ActionResult<IEnumerable<ReimbursementEstimateDto>>> GetReimbursementEstimates(
        [FromBody] GetReimbursementEstimatesQuery query)
    {
        var result = await _mediator.Send(query);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
