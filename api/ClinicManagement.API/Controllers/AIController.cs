using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.Features.AI.Commands;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class AIController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AIController> _logger;

    public AIController(IMediator mediator, ILogger<AIController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Chat with AI assistant
    /// </summary>
    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}



