using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Application.Features.TreatmentPlans.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/treatment-plans")]
[Authorize]
public class TreatmentPlansController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public TreatmentPlansController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List the clinic's treatment plans, filtered by patient / status / created-date range.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TreatmentPlanDto>>> GetPlans(
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _mediator.Send(new GetTreatmentPlansQuery { PatientId = patientId, Status = status, From = from, To = to });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TreatmentPlanDto>> GetPlan(Guid id)
    {
        var result = await _mediator.Send(new GetTreatmentPlanQuery { Id = id });
        return result.IsFailure ? HandleFailure(result, StatusCodes.Status404NotFound) : Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<TreatmentPlanDto>> CreatePlan([FromBody] CreateTreatmentPlanCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure
            ? HandleFailure(result)
            : CreatedAtAction(nameof(GetPlan), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TreatmentPlanDto>> UpdatePlan(Guid id, [FromBody] UpdateTreatmentPlanCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<TreatmentPlanDto>> AcceptPlan(Guid id)
    {
        var result = await _mediator.Send(new AcceptTreatmentPlanCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/installments/{installmentId:guid}/payments")]
    public async Task<ActionResult<TreatmentPlanDto>> RecordInstallmentPayment(Guid id, Guid installmentId, [FromBody] RecordInstallmentPaymentCommand command)
    {
        command.PlanId = id;
        command.InstallmentId = installmentId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/items/{itemId:guid}/done")]
    public async Task<ActionResult<TreatmentPlanDto>> MarkItemDone(Guid id, Guid itemId, [FromBody] MarkTreatmentPlanItemDoneCommand command)
    {
        command.PlanId = id;
        command.ItemId = itemId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<TreatmentPlanDto>> CancelPlan(Guid id, [FromBody] CancelTreatmentPlanCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var result = await _mediator.Send(new DeleteTreatmentPlanCommand { Id = id });
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }

    [HttpGet("{id:guid}/devis-pdf")]
    public async Task<IActionResult> GetDevisPdf(Guid id)
    {
        var result = await _mediator.Send(new GetDevisPdfQuery { Id = id });
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }
        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }

    /// <summary>Download the receipt (reçu) PDF for an installment payment. 404 if the plan/installment is not found or unpaid.</summary>
    [HttpGet("{id:guid}/installments/{installmentId:guid}/receipt-pdf")]
    public async Task<IActionResult> GetInstallmentReceiptPdf(Guid id, Guid installmentId)
    {
        var result = await _mediator.Send(new GetInstallmentReceiptPdfQuery { PlanId = id, InstallmentId = installmentId });
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }
        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }
}
