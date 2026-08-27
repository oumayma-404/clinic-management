using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Suppliers.Commands;
using ClinicManagement.Application.Features.Suppliers.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Les fournisseurs — the cabinet's outside contacts: laboratoires de prothèse, laboratoires d'analyses, dépôts
/// dentaires, maintenance.
/// <para>
/// ⚠️ <b><c>AnyClinicRole</c> throughout, deliberately.</b> Ordering supplies and chasing a prothèse is reception's
/// job, and none of this is clinic-wide money — the distinction `adoption-qa-i` draws is between per-patient work
/// (open) and clinic-wide aggregates (not), and a supplier list is neither. Gating it `AdminOrDoctor` would leave
/// the person who actually phones the laboratory unable to find its number.
/// </para>
/// </summary>
[ApiController]
[Route("api/suppliers")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class SuppliersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public SuppliersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>One page of fournisseurs. Omit both paging parameters to get every match (the pickers).</summary>
    [HttpGet]
    public async Task<ActionResult<SupplierPageDto>> GetSuppliers(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await _mediator.Send(new GetSuppliersQuery
        {
            SearchTerm = q,
            Category = category,
            IncludeInactive = includeInactive,
            Page = page,
            PageSize = pageSize,
        });

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> CreateSupplier([FromBody] CreateSupplierCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier(Guid id, [FromBody] UpdateSupplierCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Deletes a fournisseur nothing references. A referenced one is a 400 carrying
    /// <c>supplier_in_use</c> and naming the counts — « Désactiver » is the route for one that is in use.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        var result = await _mediator.Send(new DeleteSupplierCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
