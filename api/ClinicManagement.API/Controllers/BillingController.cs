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

    /// <summary>Download the receipt (reçu) PDF for a single invoice payment. 404 if the payment is not found.</summary>
    [HttpGet("payments/{paymentId:guid}/receipt-pdf")]
    public async Task<IActionResult> GetPaymentReceiptPdf(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPaymentReceiptPdfQuery { PaymentId = paymentId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }
}
