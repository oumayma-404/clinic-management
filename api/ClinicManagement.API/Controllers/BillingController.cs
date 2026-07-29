using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Application.Features.Invoices.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Unified billing ledger — the per-patient balance (« Solde patient »), the clinic-wide receivables
/// (« Créances ») list, and payment receipts. Clinic-scoped; every figure is computed on read from the
/// invoice + treatment-plan tracks.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class BillingController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public BillingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The unified per-patient balance + CNAM split. 404 if the patient is missing / in another clinic.</summary>
    [HttpGet("patients/{patientId:guid}/billing-summary")]
    public async Task<ActionResult<PatientBillingSummaryDto>> GetPatientBillingSummary(Guid patientId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPatientBillingSummaryQuery { PatientId = patientId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>The clinic-wide receivables list — patients with a positive balance, sorted by amount owed.</summary>
    [HttpGet("billing/receivables")]
    public async Task<ActionResult<IEnumerable<ReceivableDto>>> GetReceivables(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetReceivablesQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// The caisse (daily cash) summary — encaissements (collected payments) minus dépenses (expenses)
    /// and the net, over [from, to). Both default to the current day when omitted.
    /// </summary>
    [HttpGet("billing/caisse")]
    public async Task<ActionResult<CaisseSummaryDto>> GetCaisseSummary([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCaisseSummaryQuery { From = from, To = to }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// The « extrait de caisse » — every movement behind the totals above, oldest first, with a running
    /// period balance. Same window as <c>billing/caisse</c> and the same clinic-local default, so the lines and
    /// the totals always describe the same period.
    /// </summary>
    [HttpGet("billing/caisse/ledger")]
    public async Task<ActionResult<CaisseLedgerDto>> GetCaisseLedger([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCaisseLedgerQuery { From = from, To = to }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>Download the receipt (reçu) PDF for a single invoice payment. 404 if the payment is not found.</summary>
    [HttpGet("payments/{paymentId:guid}/receipt-pdf")]
    public async Task<IActionResult> GetPaymentReceiptPdf(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPaymentReceiptPdfQuery { PaymentId = paymentId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }
}
