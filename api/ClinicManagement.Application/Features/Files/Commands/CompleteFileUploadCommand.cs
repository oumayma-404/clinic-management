using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Assembles the staged parts into one stored blob and records the file.
///
/// <para>⚠️ <b>What arrived is checked against what was declared before anything is assembled.</b> The parts were
/// each measured on the way in, so this is a second, cheap arithmetic check on the sum — and the two together are
/// what stop a file that is the right length in its row and short on disk.</para>
///
/// <para>⚠️ <b>The signature is NOT re-checked.</b> It was checked against the first chunk's header by
/// <see cref="UploadFileChunkCommandHandler"/>, and those exact bytes are what part 1 holds; re-reading the
/// assembled blob would mean a second full pass over a file that may be hundreds of megabytes to learn something
/// already known.</para>
/// </summary>
public class CompleteFileUploadCommand : IRequest<Result<PatientFileDto>>
{
    public Guid PatientId { get; set; }
    public Guid UploadId { get; set; }

    /// <summary>The stand-in image the browser built, or null — never load-bearing, exactly as on the other doors.</summary>
    public Stream? PreviewStream { get; set; }

    public string? PreviewFileName { get; set; }

    public long PreviewSize { get; set; }
}

public class CompleteFileUploadCommandHandler : IRequestHandler<CompleteFileUploadCommand, Result<PatientFileDto>>
{
    public const string IncompleteMessage =
        "Cet envoi n'est pas terminé : tous les morceaux ne sont pas arrivés.";

    public const string LengthMismatchMessage =
        "Les morceaux reçus ne totalisent pas la taille annoncée. Recommencez l'envoi.";

    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileUploadSessionRepository _sessions;
    private readonly IResumableUploadStore _uploadStore;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<CompleteFileUploadCommandHandler> _logger;

    public CompleteFileUploadCommandHandler(
        IPatientRepository patientRepository,
        IPatientFileRepository fileRepository,
        IFileUploadSessionRepository sessions,
        IResumableUploadStore uploadStore,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        ILogger<CompleteFileUploadCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _fileRepository = fileRepository;
        _sessions = sessions;
        _uploadStore = uploadStore;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PatientFileDto>> Handle(
        CompleteFileUploadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientFileDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var session = await _sessions.GetByIdAsync(request.UploadId, cancellationToken);
            if (session == null || session.ClinicId != clinicResult.Value || session.PatientId != request.PatientId)
            {
                return Result<PatientFileDto>.Failure("Envoi introuvable.");
            }

            if (session.HasExpired(DateTime.UtcNow))
            {
                return Result<PatientFileDto>.Failure(UploadFileChunkCommandHandler.ExpiredMessage);
            }

            if (!session.IsComplete)
            {
                return Result<PatientFileDto>.Failure(IncompleteMessage);
            }

            if (session.ReceivedBytes != session.DeclaredLength)
            {
                return Result<PatientFileDto>.Failure(LengthMismatchMessage);
            }

            // The authoritative tenant check, again and last: a patient may have been moved or removed between
            // opening the upload and finishing it, and this is the request that stores bytes against them.
            var patient = await _patientRepository.GetByIdAsync(session.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientFileDto>.Failure("Patient introuvable.");
            }

            var parts = Enumerable.Range(1, session.TotalParts).ToList();

            var storageKey = await _uploadStore.CompleteAsync(
                session.ClinicId, session.StorageReference, session.ContentType, parts, cancellationToken);

            var fileId = Guid.NewGuid();

            var previewKey = await PatientFilePreviewStore.StoreAsync(
                _fileStorage, _logger, fileId, session.ClinicId,
                request.PreviewStream, request.PreviewFileName, request.PreviewSize, cancellationToken);

            try
            {
                var entry = FileTypeCatalog.TryGet(FileNameSanitizer.ExtensionOf(session.FileName));

                var file = new PatientFile(
                    fileId,
                    session.PatientId,
                    session.ClinicId,
                    session.FileName,
                    storageKey,
                    session.ContentType,
                    session.DeclaredLength,
                    entry?.Category ?? Domain.Enums.FileType.Other,
                    session.FolderId,
                    session.Description,
                    session.UploadedBy,
                    previewKey);

                await _fileRepository.AddAsync(file, cancellationToken);

                // The session's whole purpose is spent; the file row is the record from here.
                await _sessions.RemoveAsync(session, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<PatientFileDto>.Success(file.ToDto());
            }
            catch
            {
                // ⚠️ BOTH blobs. The assembled original and the stand-in are each written before the row exists,
                // so cleaning up one of two leaves an orphan just as surely as cleaning up neither.
                try { await _fileStorage.DeleteAsync(storageKey, cancellationToken); }
                catch { /* best-effort orphan cleanup: don't mask the original failure */ }

                if (previewKey != null)
                {
                    try { await _fileStorage.DeleteAsync(previewKey, cancellationToken); }
                    catch { /* idem */ }
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error completing upload {Upload}", request.UploadId);
            return Result<PatientFileDto>.Failure("Erreur lors de la finalisation de l'envoi.");
        }
    }
}
