using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Queries;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// The whole dashboard for the current user's clinic: the resolved window, the comparable Activité and Argent
    /// sections, the point-in-time créances total, the À-traiter counts, and the six-month collected trend.
    /// </summary>
    /// <param name="period">
    /// <c>Today</c> | <c>Week</c> | <c>Month</c> (default). The <b>only</b> period input — both the current and the
    /// previous window are derived server-side from the clinic clock, so the two halves of every comparison can never
    /// have been computed by different rules. The retired <c>GET stats</c> endpoint took six boundary parameters from
    /// the client instead.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard(
        [FromQuery] DashboardPeriodKey period = DashboardPeriodKey.Month)
    {
        var result = await _mediator.Send(new GetDashboardQuery { Period = period });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
