using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Invoices.Queries;

using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Facturation — note d'honoraires. Create/edit drafts, issue (numbering + frozen totals), record
/// payments, cancel (admin/doctor), PDF export, and the Recettes list/aggregate. Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/invoices")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class InvoicesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List invoices (Recettes), filtered by period / patient / status.</summary>
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter, applied in SQL <b>before</b> the page is cut so it spans the whole clinic.
    /// </param>

    /// <summary>
    /// « Exporter » (L5) — the same list, as a CSV.
    ///
    /// <para>⚠️ It re-sends the <b>identical query with no paging</b>, which the paging primitive models as a
    /// first-class case rather than as a huge page. That is what makes « honours the current filters, exports the
    /// whole filtered set, never the current page » true by construction instead of by discipline — the export
    /// cannot see a page to accidentally export.</para>
    /// </summary>
    /// <remarks>
    /// <b>AdminOrDoctor</b>, like <c>invoices/revenue</c>: a CSV of every note d'honoraires is the clinic-wide
    /// money read in a file, and leaving it on the class policy would reopen spec I's hole from a different door.
    /// </remarks>
    [HttpGet("export")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult> ExportInvoices(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? doctorId = null)
    {
        var result = await _mediator.Send(new GetInvoicesQuery
        {
            From = from,
            To = to,
            PatientId = patientId,
            Status = status,
            SearchTerm = search,
            DoctorId = doctorId,
        });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Invoices(result.Value!.Items), "factures");
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<InvoiceDto>>> GetInvoices(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? patientId,
        [FromQuery] string? status,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        // L9 — the practitioner filter. Omit for the whole clinic; an unattributed note is excluded when it is
        // supplied, which is what keeps two practitioners' filtered totals from exceeding the clinic's.
        [FromQuery] Guid? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetInvoicesQuery
        {
            From = from,
            To = to,
            PatientId = patientId,
            Status = status,
            Page = page,
            PageSize = pageSize,
            SearchTerm = search,
            DoctorId = doctorId
        };
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Aggregate revenue over a period: invoiced / collected / outstanding.</summary>
    [HttpGet("revenue")]
    // Le chiffre d'affaires. Every other action on this controller is per-invoice — reception raises the note
    // and takes the payment — and this is the only one that is a clinic-wide aggregate.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<InvoiceRevenueDto>> GetRevenue(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = new GetInvoiceRevenueQuery { From = from, To = to };
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Get a single invoice.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<InvoiceDto>> GetInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetInvoiceQuery { Id = id }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    /// <summary>Download the note-d'honoraires PDF.</summary>
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetInvoicePdf(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetInvoicePdfQuery { Id = id }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }

    /// <summary>Create a draft invoice.</summary>
    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> CreateInvoice([FromBody] CreateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetInvoice), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>Create a draft invoice from an accepted treatment plan (devis→facture bridge).</summary>
    [HttpPost("from-plan/{planId:guid}")]
    public async Task<ActionResult<InvoiceDto>> CreateInvoiceFromPlan(Guid planId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new CreateInvoiceFromTreatmentPlanCommand { TreatmentPlanId = planId }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetInvoice), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// Bill a fiche de soins: raise the note d'honoraires from the session's acts, <b>issue</b> it, and — when
    /// <c>paidNow</c> is supplied — record that payment, atomically.
    /// <para>
    /// ⚠️ Unlike <c>from-plan</c> this does <b>not</b> produce a draft: a payment can only exist on an issued
    /// invoice, so a gapless per-clinic number is consumed. Correcting a mis-keyed amount afterwards means an
    /// <b>avoir</b>, not an edit — the client must confirm before calling.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ The command returns a typed <c>DentalRecordBillingResult</c> (which note, and what this call actually put
    /// in the till), and this endpoint unwraps <c>.Invoice</c> — so the route, the body and every existing client
    /// are unchanged. The outcome matters to the <b>fiche save</b> path, which reports it on
    /// <c>DentalRecordDto.Billing</c>; a caller pressing « Facturer cette intervention » is looking at the note.
    /// </remarks>
    [HttpPost("from-dental-record/{dentalRecordId:guid}")]
    public async Task<ActionResult<InvoiceDto>> CreateInvoiceFromDentalRecord(
        Guid dentalRecordId,
        [FromBody] DentalRecordPaymentRequest? paidNow = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new BillDentalRecordCommand { DentalRecordId = dentalRecordId, PaidNow = paidNow },
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetInvoice), new { id = result.Value!.Invoice.Id }, result.Value.Invoice);
    }

    /// <summary>Update a draft invoice (lines / patient).</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<InvoiceDto>> UpdateInvoice(Guid id, [FromBody] UpdateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Issue a draft: assign the number and freeze VAT/stamp + totals.</summary>
    [HttpPost("{id}/issue")]
    public async Task<ActionResult<InvoiceDto>> IssueInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new IssueInvoiceCommand { Id = id }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Record a payment against an issued invoice.</summary>
    [HttpPost("{id}/payments")]
    public async Task<ActionResult<InvoiceDto>> RecordPayment(Guid id, [FromBody] RecordPaymentCommand command, CancellationToken cancellationToken = default)
    {
        command.InvoiceId = id;
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Cancel an issued invoice (admin/doctor only).</summary>
    /// <summary>
    /// Void a recorded payment — "this was never received". The row is kept and marked with a motif, the actor
    /// and the moment; the collected total is recomputed and the status walks back. Not reversible: to correct
    /// a correction, record the right payment again.
    ///
    /// <para>
    /// AdminOrDoctor, like every operation that alters an issued financial document. A void is a correction,
    /// not a refund — money actually returned to the patient is an avoir.
    /// </para>
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    [HttpPost("{id}/payments/{paymentId}/void")]
    public async Task<ActionResult<InvoiceDto>> VoidPayment(
        Guid id,
        Guid paymentId,
        [FromBody] VoidPaymentCommand command,
        CancellationToken cancellationToken)
    {
        command.InvoiceId = id;
        command.PaymentId = paymentId;

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Mark a cheque payment as taken to the bank, or clear that mark (Group B). Body <c>{ banked: bool }</c>.
    ///
    /// <para>
    /// AdminOrDoctor, mirroring the void route one for one — the two are the same shape and the same audience.
    /// It moves no money: la caisse counts a cheque on the day it was received, so this changes only whether the
    /// row is still on the « à encaisser » to-do list. Reversible, because a cheque can bounce.
    /// </para>
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    [HttpPost("{id}/payments/{paymentId}/banked")]
    public async Task<ActionResult<InvoiceDto>> SetPaymentBanked(
        Guid id,
        Guid paymentId,
        [FromBody] SetPaymentBankedCommand command,
        CancellationToken cancellationToken)
    {
        command.InvoiceId = id;
        command.PaymentId = paymentId;

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<InvoiceDto>> CancelInvoice(Guid id, [FromBody] CancelInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Establish an avoir (credit note) against a (partially) paid invoice (admin/doctor only).</summary>
    [HttpPost("{id}/avoir")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<CreditNoteDto>> CreateCreditNote(Guid id, [FromBody] CreateCreditNoteCommand command, CancellationToken cancellationToken = default)
    {
        command.InvoiceId = id;
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The avoirs established against this invoice, newest first. Readable by anyone who can see the
    /// invoice — establishing one is admin/doctor, reading one is not a financial action.
    /// </summary>
    [HttpGet("{id}/avoirs")]
    public async Task<ActionResult<List<CreditNoteDto>>> GetCreditNotes(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetInvoiceCreditNotesQuery { InvoiceId = id }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Download an avoir's PDF — the patient's proof of the refund.</summary>
    [HttpGet("avoirs/{creditNoteId}/pdf")]
    public async Task<IActionResult> GetCreditNotePdf(Guid creditNoteId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCreditNotePdfQuery { Id = creditNoteId }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }

    /// <summary>Delete a draft invoice (an issued invoice cannot be deleted).</summary>
    [HttpDelete("{id}")]
    // Only a draft can be deleted (no number consumed, no money attached), but it is still the removal of a
    // money document — the same bracket as cancelling a note or issuing an avoir.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<IActionResult> DeleteInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteInvoiceCommand { Id = id }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }
}
