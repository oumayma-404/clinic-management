using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Commands;
using ClinicManagement.Application.Features.Dashboard.Queries;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
// Its Argent section *is* the clinic's revenue, and its Activité section is the practice's throughput — the
// two figures a secretary must not be able to read. The preferences pair below is gated with it rather than
// one step looser: they configure this screen, and a role that cannot open it has nothing to configure.
[Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
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
    /// <param name="doctorId">
    /// L9 — narrow the <b>Argent</b> section to one practitioner. ⚠️ Dépenses, Net and Créances stay clinic-wide
    /// even then (an expense has no practitioner), and the response flags that so the client can label them —
    /// see <c>DashboardMoneyDto.ClinicWideOutgoings</c>.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard(
        [FromQuery] DashboardPeriodKey period = DashboardPeriodKey.Month,
        [FromQuery] Guid? doctorId = null)
    {
        var result = await _mediator.Send(new GetDashboardQuery { Period = period, DoctorId = doctorId });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The signed-in user's dashboard layout choices, plus every block the dashboard can show.
    /// </summary>
    /// <remarks>
    /// Per-user, not per-clinic: the user is resolved from the token, never from a route or query parameter, so
    /// there is no addressable way to read or write anyone else's layout.
    /// </remarks>
    [HttpGet("preferences")]
    public async Task<ActionResult<DashboardPreferencesDto>> GetPreferences()
    {
        var result = await _mediator.Send(new GetDashboardPreferencesQuery());

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Replaces the signed-in user's hidden-block set and returns the persisted state.
    /// </summary>
    /// <remarks>
    /// <c>PUT</c> rather than <c>PATCH</c> because the semantics really are replace: the customiser always holds
    /// the full intended state, and a merge could not express "show this one again".
    /// <para>
    /// Not admin-gated, and it must not be: this is the user's own view of their own dashboard. It is also the
    /// reason there is no clinic-wide realtime broadcast (see <c>UpdateDashboardPreferencesCommand</c>).
    /// </para>
    /// </remarks>
    [HttpPut("preferences")]
    public async Task<ActionResult<DashboardPreferencesDto>> UpdatePreferences(
        [FromBody] UpdateDashboardPreferencesCommand command)
    {
        var result = await _mediator.Send(command ?? new UpdateDashboardPreferencesCommand());

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
