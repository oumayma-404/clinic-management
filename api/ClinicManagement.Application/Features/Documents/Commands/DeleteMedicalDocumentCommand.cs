using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Documents.Commands;

public class DeleteMedicalDocumentCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteMedicalDocumentCommandHandler : IRequestHandler<DeleteMedicalDocumentCommand, Result<bool>>
{
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteMedicalDocumentCommandHandler> _logger;

    public DeleteMedicalDocumentCommandHandler(
        IMedicalDocumentRepository documentRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteMedicalDocumentCommandHandler> logger)
    {
        _documentRepository = documentRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeleteMedicalDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (document == null)
            {
                return Result<bool>.Failure("Document médical introuvable.");
            }

            // Verify the document's owning patient belongs to the caller's clinic (MedicalDocument has no
            // ClinicId of its own).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            if (document.Patient == null || document.Patient.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Document médical introuvable.");
            }

            // Resolve the underlying stored file (if any) so we can drop both the DB row and its blob —
            // otherwise deleting the document strands an orphaned PatientFile row + blob (FR-C3).
            string? storageKey = null;
            if (document.FileId.HasValue)
            {
                var file = await _fileRepository.GetByIdAsync(document.FileId.Value, cancellationToken);
                if (file != null)
                {
                    storageKey = file.StorageKey;
                    await _fileRepository.DeleteAsync(file, cancellationToken);
                }
            }

            await _documentRepository.DeleteAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The DB rows are gone; drop the blob. Best-effort (mirrors CreateMedicalDocumentCommand's
            // upload-failure cleanup): a leaked blob is preferable to failing an already-committed delete.
            if (storageKey != null)
            {
                try
                {
                    await _fileStorage.DeleteAsync(storageKey, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Best-effort: the DB rows are already committed, so a failed blob delete leaves an
                    // orphaned blob rather than failing the delete. Log it so the leak is discoverable.
                    _logger.LogWarning(ex, "Failed to delete orphaned blob {StorageKey} for medical document {DocumentId}",
                        storageKey, request.Id);
                }
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting medical document: {ex.Message}");
        }
    }
}








