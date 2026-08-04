using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Application.Features.Invoices.Queries;

using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Unified billing ledger — the per-patient balance (« Solde patient »), the clinic-wide receivables
/// (« Créances ») list, and payment receipts. Clinic-scoped; every figure is computed on read from the
/// invoice + treatment-plan tracks.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
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


    /// <summary>
    /// « Plafond annuel CNAM » for one patient (L10) — the ceiling, what this clinic has consumed of it this year,
    /// and what is left. 404 if the patient is missing / in another clinic.
    ///
    /// <para>⚠️ <b>Deliberately on the class policy</b> (`AnyClinicRole`), beside « Solde patient » and for the same
    /// reason: it is per-patient money, and « combien reste-t-il à ce patient ? » is asked at the desk with the
    /// patient standing there. The I1 line is per-patient yes, clinic-wide aggregates no.</para>
    /// </summary>
    /// <param name="year">The year to report on. Omit for the current <b>clinic</b> year.</param>
    [HttpGet("patients/{patientId:guid}/cnam-ceiling")]
    public async Task<ActionResult<CnamCeilingDto>> GetPatientCnamCeiling(
        Guid patientId, [FromQuery] int? year = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPatientCnamCeilingQuery { PatientId = patientId, Year = year }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// « Exporter » (L5) — the same list, as a CSV.
    ///
    /// <para>⚠️ It re-sends the <b>identical query with no paging</b>, which the paging primitive models as a
    /// first-class case rather than as a huge page. That is what makes « honours the current filters, exports the
    /// whole filtered set, never the current page » true by construction rather than by discipline.</para>
    /// </summary>
    /// <remarks>
    /// <b>AdminOrDoctor</b>, matching the two reads it exports. A CSV is strictly more portable than the screen,
    /// so it cannot be laxer than the screen.
    /// </remarks>
    [HttpGet("billing/receivables/export")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult> ExportReceivables(
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetReceivablesQuery { SearchTerm = search }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Receivables(result.Value!.Items), "creances");
    }

    /// <summary>
    /// The « extrait de caisse » as a CSV (L5) — the file an accountant reconciles against a bank statement.
    ///
    /// <para>⚠️ Exported <b>unfiltered by the search term</b> but over the requested window, and each row keeps
    /// the running balance it had in that window. Exporting a text-filtered subset would produce a « Solde de la
    /// période » column that sums to nothing — the same reason the screen computes the balance before filtering.</para>
    /// </summary>
    [HttpGet("billing/caisse/ledger/export")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult> ExportCaisseLedger(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetCaisseLedgerQuery { From = from, To = to }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.CaisseLedger(result.Value!.Movements), "extrait-de-caisse");
    }

    /// <summary>The clinic-wide receivables list — patients with a positive balance, sorted by amount owed.</summary>
    [HttpGet("billing/receivables")]
    // Clinic-wide debt: the practice's exposure in one figure. This is the fork the class policy exists for —
    // the sibling read above (« Solde patient ») stays open because reception cannot collect without it.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    /// <param name="page">1-based page number. Omit both paging parameters to get every row.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">Free-text filter on the patient's name, applied before the page is cut.</param>
    public async Task<ActionResult<ReceivablesPageDto>> GetReceivables(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetReceivablesQuery { Page = page, PageSize = pageSize, SearchTerm = search },
            cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// The caisse (daily cash) summary — encaissements (collected payments) minus dépenses (expenses)
    /// and the net, over [from, to). Both default to the current day when omitted.
    /// </summary>
    [HttpGet("billing/caisse")]
    // The till's totals for a whole window — clinic-wide money.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
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
    // Every movement behind those totals — strictly more than the totals, so it cannot be laxer.
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    /// <param name="page">1-based page over the movements. Omit both paging parameters for the whole window.</param>
    /// <param name="pageSize">Movements per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter over the movement label, patient, reference and method. ⚠️ Each row keeps the running
    /// balance it had in the <b>unfiltered</b> window — « Solde de la période » is a fact about a movement's place
    /// in the period, not about the filtered subset, and recomputing it over a filter would print a column that
    /// sums to nothing.
    /// </param>
    /// <param name="method">
    /// Optional <c>PaymentMethod</c> name (<c>Cash</c>/<c>Cheque</c>/<c>Card</c>/<c>Transfer</c>) — L8 slice B's
    /// « ne montre que les chèques ». Applied after the running balance for the same reason as
    /// <paramref name="search"/>; an unrecognised value is ignored rather than refused.
    /// </param>
    public async Task<ActionResult<CaisseLedgerDto>> GetCaisseLedger(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        [FromQuery] string? method = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetCaisseLedgerQuery
            {
                From = from,
                To = to,
                Page = page,
                PageSize = pageSize,
                SearchTerm = search,
                Method = method
            },
            cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// « Chèques à encaisser » (L8 slice B) — every cheque the clinic holds, across both payment ledgers,
    /// soonest-due first, with per-bucket counts and totals over the whole matching set.
    ///
    /// <para>⚠️ <b>AdminOrDoctor.</b> It is a clinic-wide money read — the practice's uncashed exposure in one
    /// figure — so it belongs with la caisse and les créances, not with the per-patient reads reception needs.</para>
    /// </summary>
    /// <param name="dueFrom">Inclusive lower bound on the cheque's <b>due date</b> (not on when it was received). Cheques with no due date are always returned.</param>
    /// <param name="dueTo">Inclusive upper bound on the due date.</param>
    /// <param name="page">1-based page. Omit both paging parameters for every cheque held.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">Free-text filter over the cheque number, the bank, the patient and the document reference.</param>
    [HttpGet("billing/cheques")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<ChequesDueDto>> GetChequesDue(
        [FromQuery] DateTime? dueFrom = null,
        [FromQuery] DateTime? dueTo = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetChequesDueQuery
            {
                DueFrom = dueFrom,
                DueTo = dueTo,
                Page = page,
                PageSize = pageSize,
                SearchTerm = search
            },
            cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// « Chèques à encaisser » as a CSV — the list an owner takes to the bank.
    /// <para>Honours the same filters as the screen, over the whole filtered set (`paging: null`), per L5.</para>
    /// </summary>
    [HttpGet("billing/cheques/export")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult> ExportChequesDue(
        [FromQuery] DateTime? dueFrom = null,
        [FromQuery] DateTime? dueTo = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetChequesDueQuery { DueFrom = dueFrom, DueTo = dueTo, SearchTerm = search },
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Csv(ExportTables.Cheques(result.Value!.Items), "cheques-a-encaisser");
    }

    /// <summary>Download the receipt (reçu) PDF for a single invoice payment. 404 if the payment is not found.</summary>
    [HttpGet("payments/{paymentId:guid}/receipt-pdf")]
    public async Task<IActionResult> GetPaymentReceiptPdf(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPaymentReceiptPdfQuery { PaymentId = paymentId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }
}
