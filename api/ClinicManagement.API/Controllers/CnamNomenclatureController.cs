using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.CnamNomenclature.Queries;

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
    /// Get the curated CNAM dental nomenclature, optionally filtered by free-text query and/or category.
    /// Shared reference data (not clinic-scoped); requires an authenticated user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CnamNomenclatureEntryDto>>> GetNomenclature(
        [FromQuery] string? q = null, [FromQuery] string? category = null)
    {
        var query = new GetCnamNomenclatureQuery { Q = q, Category = category };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
