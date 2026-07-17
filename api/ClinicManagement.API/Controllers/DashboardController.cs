using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard.Queries;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get aggregate KPI counts for the current user's clinic.
    /// Optional local-day/week boundaries keep the counts aligned with the appointment list.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats(
        [FromQuery] DateTime? todayStart,
        [FromQuery] DateTime? todayEnd,
        [FromQuery] DateTime? weekStart,
        [FromQuery] DateTime? weekEnd,
        [FromQuery] DateTime? monthStart,
        [FromQuery] DateTime? monthEnd)
    {
        var query = new GetDashboardStatsQuery
        {
            TodayStart = todayStart,
            TodayEnd = todayEnd,
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            MonthStart = monthStart,
            MonthEnd = monthEnd
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }
}
