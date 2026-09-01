using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ClinicManagement.API.Startup;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class AppointmentsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AppointmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// « À clôturer » — the séances whose slot has passed and which still owe one of the three answers:
    /// est-il venu, qu'a-t-on fait, combien a-t-il payé.
    ///
    /// <para>⚠️ <b><c>AnyClinicRole</c>, from the class, and that is the point rather than an oversight.</b> The
    /// dashboard is <c>AdminOrDoctor</c> and <c>app/page.tsx</c> redirects a secretary to <c>/appointments</c>, so
    /// a worklist reachable only from the dashboard would be invisible to reception — who is exactly the person
    /// who knows whether the patient came and who took the money. The dashboard chip is the secondary surface;
    /// this read and the agenda strip are the primary ones.</para>
    ///
    /// <para>No <c>[AllowsWithoutSubscription]</c>: it is a GET, and the subscription gate never inspects one.</para>
    /// </summary>
    [HttpGet("to-close")]
    public async Task<ActionResult<VisitsToCloseDto>> GetVisitsToClose(
        [FromQuery] int? days = null,
        [FromQuery] Guid? doctorId = null,
        [FromQuery] bool disregarded = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await _mediator.Send(new GetVisitsToCloseQuery
        {
            Days = days,
            DoctorId = doctorId,
            Disregarded = disregarded,
            Paging = PageRequest.From(page, pageSize),
        });

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// « Retirer de la liste » — take one séance off « À clôturer » without claiming anything clinical about it.
    ///
    /// <para><b>Why the worklist needed a third exit.</b> Its only ones were <c>Completed</c>, <c>Cancelled</c>
    /// and <c>NoShow</c> — three statements about what happened to a patient — so clearing a row that should
    /// never have been there meant asserting one that was false, which is how a cabinet's « taux d'absence »
    /// climbed to a figure it knew was wrong.</para>
    ///
    /// <para>A <b>POST and not a DELETE</b>, and its withdrawal is a DELETE on the same path: unlike
    /// « rien à facturer » — one toggle on one row — this also exists in bulk, and a body carrying the direction
    /// would let a truncated request turn « remettre » into « retirer » across a selection.</para>
    ///
    /// <para><b>No body at all</b>: the mark carries no motif — see <see cref="DisregardVisitsCommand"/> for why
    /// « rien à facturer »'s mandatory one does not carry over to a mark that asserts nothing.</para>
    /// </summary>
    [HttpPost("{id:guid}/disregard")]
    public async Task<ActionResult> Disregard(Guid id)
    {
        var result = await _mediator.Send(new DisregardVisitsCommand
        {
            AppointmentIds = new List<Guid> { id },
            Disregard = true,
        });

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Put one séance back on « À clôturer » — and back into the dashboard's figures.</summary>
    [HttpDelete("{id:guid}/disregard")]
    public async Task<ActionResult> RestoreToWorklist(Guid id)
    {
        var result = await _mediator.Send(new DisregardVisitsCommand
        {
            AppointmentIds = new List<Guid> { id },
            Disregard = false,
        });

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// The same, over a selection — what a cabinet with a hundred phantom séances actually needs.
    ///
    /// <para>Nothing to fill in, for the reason on <see cref="DisregardVisitsCommand"/>: a mandatory motif over a
    /// selection that size made the exit that asserts nothing cost more than the one that asserts something
    /// false.</para>
    /// </summary>
    [HttpPost("disregard")]
    public async Task<ActionResult> DisregardMany([FromBody] DisregardVisitsRequest request)
    {
        var result = await _mediator.Send(new DisregardVisitsCommand
        {
            AppointmentIds = request.AppointmentIds,
            Disregard = request.Disregard,
        });

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Body of <see cref="DisregardMany"/>.</summary>
    public class DisregardVisitsRequest
    {
        public List<Guid> AppointmentIds { get; set; } = new();

        /// <summary>True to retire the selection, false to put it back.</summary>
        public bool Disregard { get; set; } = true;
    }

    /// <summary>
    /// « Rien à facturer » — record that a séance raises no note d'honoraires, or withdraw that.
    ///
    /// <para>A <b>POST and not a DELETE</b> in both directions: nothing is deleted either way, and the body's
    /// <c>nothingToBill</c> is what the URL cannot carry — unlike the console's suspension pair, this is one
    /// control the user toggles from one row, so a second route would be a second thing to keep in step for no
    /// safety gained.</para>
    /// </summary>
    [HttpPost("{id:guid}/nothing-to-bill")]
    public async Task<ActionResult> SetNothingToBill(Guid id, [FromBody] SetNothingToBillRequest request)
    {
        var result = await _mediator.Send(new MarkNothingToBillCommand
        {
            AppointmentId = id,
            NothingToBill = request.NothingToBill,
            Reason = request.Reason,
        });

        return result.IsFailure ? HandleFailure(result) : Ok(new { nothingToBill = result.Value });
    }

    /// <summary>Body of <see cref="SetNothingToBill"/>.</summary>
    public class SetNothingToBillRequest
    {
        public bool NothingToBill { get; set; } = true;

        /// <summary>Mandatory when marking; the handler refuses a blank one.</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Get all appointments for the current user's clinic
    /// </summary>

    /// <summary>
    /// « Exporter » (L5) — the same list, as a CSV.
    ///
    /// <para>⚠️ It re-sends the <b>identical query with no paging</b>, which the paging primitive models as a
    /// first-class case rather than as a huge page. That is what makes « honours the current filters, exports the
    /// whole filtered set, never the current page » true by construction rather than by discipline.</para>
    /// </summary>
    [HttpGet("export")]
    [EnableRateLimiting(RateLimiting.ListExportPolicy)]
    public async Task<ActionResult> ExportAppointments(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] Guid? doctorId = null)
    {
        // The agenda's CSV carries the patient's name beside the séance's acts and its free-text notes, so this
        // is clinical content leaving in bulk. It is bounded and recorded now; deliberately NOT behind a step-up,
        // unlike the patient roster — see ExportAppointmentsQuery for why the two differ.
        var result = await _mediator.Send(new ExportAppointmentsQuery
        {
            StartDate = startDate,
            EndDate = endDate,
            DoctorId = doctorId,
        });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Appointments(result.Value!), "rendez-vous");
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentDto>>> GetAppointments(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? doctorId,
        [FromQuery] Guid? patientId)
    {
        var query = new GetAppointmentsQuery
        {
            StartDate = startDate,
            EndDate = endDate,
            DoctorId = doctorId,
            PatientId = patientId
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a single appointment by id (used by notification deep-links). Tenant-scoped: an appointment
    /// from another clinic reads as "not found".
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AppointmentDto>> GetAppointment(Guid id)
    {
        var result = await _mediator.Send(new GetAppointmentQuery { Id = id });

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new appointment
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> CreateAppointment([FromBody] CreateAppointmentCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetAppointments), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>List the clinic's recurring appointment series (active by default).</summary>
    [HttpGet("recurring")]
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter over the patient's name, the practitioner and the notes. Applied in SQL before the page
    /// is cut, so it spans the whole clinic.
    /// </param>
    public async Task<ActionResult<PagedResult<RecurringAppointmentDto>>> GetRecurringSeries(
        [FromQuery] bool activeOnly = true,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetRecurringSeriesQuery
        {
            ActiveOnly = activeOnly,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Create a recurring series; expands into linked appointments (returns created/skipped/conflict counts).</summary>
    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringSeriesResultDto>> CreateRecurringSeries([FromBody] CreateRecurringSeriesCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Cancel part or all of a recurring series (scope: Occurrence / Following / WholeSeries).</summary>
    [HttpPost("recurring/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRecurringSeries(Guid id, [FromBody] CancelRecurringSeriesCommand command)
    {
        command.RecurringAppointmentId = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(new { cancelled = result.Value });
    }

    /// <summary>
    /// Update an existing appointment (can be used to cancel by setting status to "Cancelled")
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<AppointmentDto>> UpdateAppointment(Guid id, [FromBody] UpdateAppointmentCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
