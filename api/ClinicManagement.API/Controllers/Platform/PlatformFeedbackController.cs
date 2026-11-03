using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>« Retours » — what the cabinets' people told the vendor (guided-first-days E). Read-only.</summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformFeedbackController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformFeedbackController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("feedback")]
    public async Task<ActionResult<PlatformFeedbackPageDto>> GetFeedback(
        [FromQuery] string? kind = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPlatformFeedbackQuery { Kind = kind, Page = page, PageSize = pageSize },
            cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
