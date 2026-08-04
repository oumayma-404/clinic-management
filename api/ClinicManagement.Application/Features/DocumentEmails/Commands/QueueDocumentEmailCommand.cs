using System.Net.Mail;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.DocumentEmails.Commands;

/// <summary>
/// Queues one generated document for delivery by email. Everything fallible happens <b>here</b>, in the
/// request: the kind is validated, the address is validated, the document is rendered through its own PDF query
/// (which tenant-checks it and applies its own refusals) and the bytes are stored — so a row only ever reaches
/// the outbox if it can actually be sent. The alternative, queueing first and discovering the problem in a job
/// a minute later, gives the practitioner a success toast for a send that silently failed.
/// </summary>
public class QueueDocumentEmailCommand : IRequest<Result<DocumentEmailDto>>
{
    public string DocumentKind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }

    /// <summary>Extra render keys — only the two receipt kinds use them.</summary>
    public Guid? InstallmentId { get; set; }
    public Guid? PaymentId { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class QueueDocumentEmailCommandHandler : IRequestHandler<QueueDocumentEmailCommand, Result<DocumentEmailDto>>
{
    private readonly IDocumentEmailRepository _documentEmailRepository;
    private readonly IDocumentEmailAttachmentRenderer _attachmentRenderer;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<QueueDocumentEmailCommandHandler> _logger;

    public QueueDocumentEmailCommandHandler(
        IDocumentEmailRepository documentEmailRepository,
        IDocumentEmailAttachmentRenderer attachmentRenderer,
        IReminderSettingsProvider settingsProvider,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<QueueDocumentEmailCommandHandler> logger)
    {
        _documentEmailRepository = documentEmailRepository;
        _attachmentRenderer = attachmentRenderer;
        _settingsProvider = settingsProvider;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DocumentEmailDto>> Handle(
        QueueDocumentEmailCommand request, CancellationToken cancellationToken)
    {
        string? storageKey = null;
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DocumentEmailDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var kind = DocumentEmail.NormalizeKind(request.DocumentKind);
            if (kind == null)
            {
                return Result<DocumentEmailDto>.Failure("Type de document non pris en charge pour l'envoi par email.");
            }

            if (request.DocumentId == Guid.Empty)
            {
                return Result<DocumentEmailDto>.Failure("Le document est obligatoire.");
            }

            var recipient = NormalizeEmail(request.RecipientEmail);
            if (recipient == null)
            {
                return Result<DocumentEmailDto>.Failure("Adresse email invalide.");
            }

            // Refuse up-front when the cabinet cannot send at all, naming where to fix it — a queued row that
            // can only ever fail is worse than a refusal, because it reads as "sent" until someone checks.
            var settings = await _settingsProvider.ResolveAsync(clinicId, cancellationToken);
            if (!settings.EmailConfigured)
            {
                return Result<DocumentEmailDto>.Failure(
                    "L'envoi par email n'est pas configuré pour ce cabinet. Renseignez le serveur SMTP dans Paramètres → Rappels et email.");
            }

            var subject = string.IsNullOrWhiteSpace(request.Subject) ? "Document médical" : request.Subject.Trim();

            // Render through the document's own PDF query: tenant check, business refusals and French filename
            // all come from there. A failure here is the practitioner's answer, verbatim.
            var attachment = await _attachmentRenderer.RenderAsync(
                kind, request.DocumentId, request.InstallmentId, request.PaymentId, cancellationToken);
            if (attachment.IsFailure || attachment.Value == null)
            {
                return Result<DocumentEmailDto>.Failure(attachment.Error ?? "Erreur lors de la génération du PDF.");
            }

            using (var pdfStream = new MemoryStream(attachment.Value.Content, writable: false))
            {
                storageKey = await _fileStorage.UploadAsync(pdfStream, "application/pdf", cancellationToken);
            }

            var documentEmail = new DocumentEmail(
                clinicId,
                kind,
                request.DocumentId,
                recipient,
                subject,
                request.Body,
                storageKey,
                attachment.Value.FileName,
                request.InstallmentId,
                request.PaymentId,
                _clinicContext.GetUserId());

            await _documentEmailRepository.AddAsync(documentEmail, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<DocumentEmailDto>.Success(documentEmail.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // The blob is uploaded before the row is committed, so a failed save would otherwise leave an
            // orphan PDF in storage forever — the same store-blob-then-persist cleanup the patient-file upload
            // does. Best-effort: a cleanup failure must not replace the real error.
            if (storageKey != null)
            {
                try
                {
                    await _fileStorage.DeleteAsync(storageKey, cancellationToken);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Could not delete the orphaned attachment blob {StorageKey}", storageKey);
                }
            }

            _logger.LogError(ex, "Error queueing document email for {DocumentKind} {DocumentId}", request.DocumentKind, request.DocumentId);
            return Result<DocumentEmailDto>.Failure("Erreur lors de la mise en file de l'email.");
        }
    }

    /// <summary>
    /// The trimmed, valid address or <c>null</c>. Validated with <see cref="MailAddress"/> — the same parser the
    /// <c>Email</c> value object uses, so an address accepted here is one the sender can actually put in a
    /// header. The VO itself is not reused because it lower-cases, and a recipient's local part is technically
    /// case-sensitive.
    /// </summary>
    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        try
        {
            var parsed = new MailAddress(trimmed);
            return parsed.Address;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
