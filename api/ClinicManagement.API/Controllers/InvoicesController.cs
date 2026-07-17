using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Invoices.Queries;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Facturation — note d'honoraires. Create/edit drafts, issue (numbering + frozen totals), record
/// payments, cancel (admin/doctor), PDF export, and the Recettes list/aggregate. Clinic-scoped.
/// </summary>
[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List invoices (Recettes), filtered by period / patient / status.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InvoiceDto>>> GetInvoices(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? patientId,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        var query = new GetInvoicesQuery { From = from, To = to, PatientId = patientId, Status = status };
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>Aggregate revenue over a period: invoiced / collected / outstanding.</summary>
    [HttpGet("revenue")]
    public async Task<ActionResult<InvoiceRevenueDto>> GetRevenue(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = new GetInvoiceRevenueQuery { From = from, To = to };
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
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
            return NotFound(result.Error);
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
            return BadRequest(result.Error);
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
            return BadRequest(result.Error);
        }

        return CreatedAtAction(nameof(GetInvoice), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>Update a draft invoice (lines / patient).</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<InvoiceDto>> UpdateInvoice(Guid id, [FromBody] UpdateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
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
            return BadRequest(result.Error);
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
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>Cancel an issued invoice (admin/doctor only).</summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<ActionResult<InvoiceDto>> CancelInvoice(Guid id, [FromBody] CancelInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>Delete a draft invoice (an issued invoice cannot be deleted).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteInvoiceCommand { Id = id }, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}
