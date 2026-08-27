using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.DocumentEmails.Commands;
using ClinicManagement.Application.Features.DocumentEmails.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Sending a generated document (ordonnance, lettre de liaison, certificat, bulletin CNAM, note d'honoraires,
/// avoir, devis, reçu) to a recipient by email, and reading what has already been sent.
/// <para>
/// Authorization is the class-level <c>[Authorize]</c> and deliberately no stricter: sending confers no access
/// the caller does not already have — every one of these documents is downloadable by any authenticated member
/// of the cabinet, and emailing a facture or a reçu to a patient is ordinary secretary work. The document's own
/// PDF query performs the tenant check.
/// </para>
/// </summary>
[ApiController]
[Route("api/document-emails")]
[Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
public class DocumentEmailsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public DocumentEmailsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Queues one document for delivery. The PDF is rendered and stored before the row is created.</summary>
    [HttpPost]
    public async Task<ActionResult<DocumentEmailDto>> Queue(
        [FromBody] QueueDocumentEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetForDocument), new
        {
            documentKind = result.Value!.DocumentKind,
            documentId = result.Value.DocumentId
        }, result.Value);
    }

    /// <summary>The send history of one document, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentEmailDto>>> GetForDocument(
        [FromQuery] string documentKind,
        [FromQuery] Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDocumentEmailsQuery { DocumentKind = documentKind, DocumentId = documentId },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
