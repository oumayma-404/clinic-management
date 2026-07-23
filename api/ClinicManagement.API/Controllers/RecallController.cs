using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Recall.Commands;
using ClinicManagement.Application.Features.Recall.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Patient recall / relance: the "patients à relancer" list, per-patient actions (mark contacted, snooze,
/// send an SMS/WhatsApp recall), and the clinic recall interval. Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/patients/recalls")]
[Authorize]
public class RecallController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public RecallController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The clinic's due/overdue patients (« à relancer »), most overdue first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecallDto>>> GetRecalls()
    {
        var result = await _mediator.Send(new GetPatientsToRecallQuery());
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Get the clinic recall settings (interval in months).</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<RecallSettingsDto>> GetSettings()
    {
        var result = await _mediator.Send(new GetRecallSettingsQuery());
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Set the clinic recall interval (months).</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<RecallSettingsDto>> SetSettings([FromBody] SetRecallSettingsCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Mark a patient as contacted about their recall (stamps + snoozes ~1 month).</summary>
    [HttpPost("{patientId:guid}/contacted")]
    public async Task<IActionResult> MarkContacted(Guid patientId, [FromBody] MarkRecallContactedCommand command)
    {
        command.PatientId = patientId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }

    /// <summary>Snooze a patient's recall for a number of days (default 30).</summary>
    [HttpPost("{patientId:guid}/snooze")]
    public async Task<IActionResult> Snooze(Guid patientId, [FromBody] SnoozeRecallCommand command)
    {
        command.PatientId = patientId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }

    /// <summary>Send an SMS/WhatsApp recall to a patient (connectivity-gated) and record the contact.</summary>
    [HttpPost("{patientId:guid}/send")]
    public async Task<IActionResult> Send(Guid patientId, [FromBody] SendRecallCommand command)
    {
        command.PatientId = patientId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
