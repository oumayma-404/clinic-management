using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.DocumentEmails.Queries;

/// <summary>
/// The send history of one document, newest first — « ce document a-t-il été envoyé, à qui, et est-ce parti ? ».
/// </summary>
public class GetDocumentEmailsQuery : IRequest<Result<IReadOnlyList<DocumentEmailDto>>>
{
    public string DocumentKind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
}

public class GetDocumentEmailsQueryHandler
    : IRequestHandler<GetDocumentEmailsQuery, Result<IReadOnlyList<DocumentEmailDto>>>
{
    private readonly IDocumentEmailRepository _documentEmailRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetDocumentEmailsQueryHandler> _logger;

    public GetDocumentEmailsQueryHandler(
        IDocumentEmailRepository documentEmailRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetDocumentEmailsQueryHandler> logger)
    {
        _documentEmailRepository = documentEmailRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<DocumentEmailDto>>> Handle(
        GetDocumentEmailsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IReadOnlyList<DocumentEmailDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var kind = DocumentEmail.NormalizeKind(request.DocumentKind);
            if (kind == null)
            {
                return Result<IReadOnlyList<DocumentEmailDto>>.Failure(
                    "Type de document non pris en charge pour l'envoi par email.");
            }

            // The clinic is a repository parameter, not a post-filter: the history of a document that is not the
            // cabinet's must read as empty, never as somebody else's sends.
            var rows = await _documentEmailRepository.GetForDocumentAsync(
                clinicResult.Value, kind, request.DocumentId, cancellationToken);

            return Result<IReadOnlyList<DocumentEmailDto>>.Success(
                rows.Select(r => r.ToDto()).ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading document email history for {DocumentKind} {DocumentId}", request.DocumentKind, request.DocumentId);
            return Result<IReadOnlyList<DocumentEmailDto>>.Failure("Erreur lors de la lecture des envois.");
        }
    }
}
