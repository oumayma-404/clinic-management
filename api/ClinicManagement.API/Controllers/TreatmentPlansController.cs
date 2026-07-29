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
    /// <param name="acceptedFrom">Inclusive lower bound on the <b>acceptance</b> date — a different date from
    /// <paramref name="from"/>, which bounds creation. Backs the dashboard's « Devis acceptés » drill-through,
    /// which counts by acceptance and so cannot filter by creation.</param>
    /// <param name="acceptedTo">Inclusive upper bound on the acceptance date.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TreatmentPlanDto>>> GetPlans(
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] DateTime? acceptedFrom = null,
        [FromQuery] DateTime? acceptedTo = null)
    {
        var result = await _mediator.Send(new GetTreatmentPlansQuery
        {
            PatientId = patientId,
            Status = status,
            From = from,
            To = to,
            AcceptedFrom = acceptedFrom,
            AcceptedTo = acceptedTo
        });
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

    /// <summary>Close a fully-treated plan (« Terminer »). 400 if not all acts are done.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<TreatmentPlanDto>> CompletePlan(Guid id)
    {
        var result = await _mediator.Send(new CompleteTreatmentPlanCommand { Id = id });
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

    /// <summary>
    /// Mark a planned act as carried out. `AdminOrDoctor` — marking an act réalisé auto-completes the devis once
    /// it is the last one, and it is the clinical assertion the invoice is later built from, so it belongs to the
    /// same class as amending the plan rather than to the unpoliced reads. It carried **no** policy at all before
    /// (audit adjacent defect A-13), so a secretary could close a devis.
    /// </summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/done")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<TreatmentPlanDto>> MarkItemDone(Guid id, Guid itemId, [FromBody] MarkTreatmentPlanItemDoneCommand command)
    {
        command.PlanId = id;
        command.ItemId = itemId;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Undo <see cref="MarkItemDone"/> — return the act to « prévu » and detach its fiche de soins, reopening the
    /// devis if that act had closed it. `AdminOrDoctor`, the same class as marking it done: this is the correction
    /// path for a clinical assertion, and it is refused outright once a live invoice bills the plan.
    /// </summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/undone")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<TreatmentPlanDto>> UnmarkItemDone(Guid id, Guid itemId)
    {
        var result = await _mediator.Send(new UnmarkTreatmentPlanItemDoneCommand { PlanId = id, ItemId = itemId });
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Amend an accepted devis: add, edit and remove acts, retitle it and revise the échéancier in one call.
    /// `AdminOrDoctor` —
    /// this alters what the patient owes on a numbered document, the same class as cancelling an issued
    /// invoice or issuing an avoir. Enforcement is controller-only, deliberately, as for that whole class.
    /// </summary>
    [HttpPost("{id:guid}/amend")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<TreatmentPlanDto>> AmendPlan(Guid id, [FromBody] AmendTreatmentPlanCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Revise only the échéancier of an accepted devis (no act change). `AdminOrDoctor` — same class.</summary>
    [HttpPut("{id:guid}/installments")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<TreatmentPlanDto>> ReviseInstallments(
        Guid id, [FromBody] ReviseTreatmentPlanInstallmentsCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Reorder the plan's acts. No method-level policy: reordering is cosmetic and changes no money, so it
    /// matches the unpoliced accept/complete rather than the financial-reversal class above.
    /// </summary>
    [HttpPut("{id:guid}/items/order")]
    public async Task<ActionResult<TreatmentPlanDto>> ReorderItems(
        Guid id, [FromBody] SetTreatmentPlanItemOrderCommand command)
    {
        command.Id = id;
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

    /// <summary>
    /// Download the receipt (reçu) PDF for one installment payment. The payment id is required: an échéance
    /// can hold several payments, and the receipt used to print the cumulative total instead of the money
    /// actually handed over. A voided payment still renders, over-stamped « REÇU ANNULÉ ».
    /// </summary>
    [HttpGet("{id:guid}/installments/{installmentId:guid}/payments/{paymentId:guid}/receipt-pdf")]
    public async Task<IActionResult> GetInstallmentReceiptPdf(Guid id, Guid installmentId, Guid paymentId)
    {
        var result = await _mediator.Send(new GetInstallmentReceiptPdfQuery
        {
            PlanId = id,
            InstallmentId = installmentId,
            PaymentId = paymentId,
        });
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }
        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }

    /// <summary>
    /// Void a payment recorded against an échéance — "this was never received". The ledger row is kept and
    /// marked; the installment's totals are re-derived. The plan's status is NOT walked back: it tracks
    /// clinical progress, not payment.
    ///
    /// <para>AdminOrDoctor — it alters what a patient has paid on a numbered document.</para>
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    [HttpPost("{id:guid}/installments/{installmentId:guid}/payments/{paymentId:guid}/void")]
    public async Task<ActionResult<TreatmentPlanDto>> VoidInstallmentPayment(
        Guid id,
        Guid installmentId,
        Guid paymentId,
        [FromBody] VoidInstallmentPaymentCommand command,
        CancellationToken cancellationToken)
    {
        command.PlanId = id;
        command.InstallmentId = installmentId;
        command.PaymentId = paymentId;

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }
        return Ok(result.Value);
    }
}
