using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

public class UploadPatientFileCommand : IRequest<Result<PatientFileDto>>
{
    public Guid PatientId { get; set; }
    public Guid? FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>ASP.NET's count of the parsed body part — a size hint, never the stored length.</summary>
    public long FileSize { get; set; }

    public Stream FileStream { get; set; } = null!;
    public string? Description { get; set; }
    public string? UploadedBy { get; set; }
}

public class UploadPatientFileCommandHandler : IRequestHandler<UploadPatientFileCommand, Result<PatientFileDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IFileResidencyPolicy _residencyPolicy;
    private readonly ILogger<UploadPatientFileCommandHandler> _logger;

    public UploadPatientFileCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IFileResidencyPolicy residencyPolicy,
        ILogger<UploadPatientFileCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _residencyPolicy = residencyPolicy;
        _logger = logger;
    }

    public async Task<Result<PatientFileDto>> Handle(UploadPatientFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return Result<PatientFileDto>.Failure("Le nom du fichier est requis.");
            }

            if (request.FileStream == null)
            {
                return Result<PatientFileDto>.Failure("Le contenu du fichier est requis.");
            }

            // Authoritative tenant guard: resolve the caller's clinic from the DB and verify the patient
            // belongs to it before storing any file (defense-in-depth, independent of the fail-open global
            // filter — cloud-security-and-tenant-isolation #6).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientFileDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientFileDto>.Failure("Patient introuvable.");
            }

            // Validate folder if provided
            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<PatientFileDto>.Failure("Dossier introuvable ou n'appartenant pas à ce patient.");
                }
            }

            // US-11 / AC-11.1–11.5: validate BEFORE anything is written, so a refused upload leaves no blob
            // and no row — no orphan cleanup required. The judgement itself lives in the catalog, which is what
            // lets the same rules cover the cachet, the logo, the document PDF and the CSV import.
            var validation = await FileUploadValidator.ValidateAsync(
                FileUploadProfile.PatientFile,
                request.FileName,
                request.FileSize,
                request.FileStream,
                cancellationToken);

            if (validation.IsFailure)
            {
                return Result<PatientFileDto>.Failure(validation.Error!);
            }

            var upload = validation.Value!;

            // The catalog decides where a file belongs, and this door only holds the ones the deployment keeps.
            // Without this the 25 Mo threshold would be advice the picker follows and nothing enforces.
            if (_residencyPolicy.Decide(upload.Entry, upload.ByteLength) != FileResidency.Hosted)
            {
                return Result<PatientFileDto>.Failure(FileResidencyRefusals.BelongsInTheVault());
            }

            // Store the blob first, then persist the record. If the DB save fails we must remove
            // the just-stored blob so no orphan remains (FR-C3).
            var storageKey = await _fileStorage.UploadAsync(
                upload.Content, upload.ContentType, patient.ClinicId, cancellationToken);

            try
            {
                // Persist the VALIDATED type, name and byte count, never the client's claims — the stored type
                // is what the download endpoint serves back (AC-11.6).
                var file = new PatientFile(
                    Guid.NewGuid(),
                    request.PatientId,
                    patient.ClinicId,
                    upload.FileName,
                    storageKey,
                    upload.ContentType,
                    upload.ByteLength,
                    upload.Entry.Category,
                    request.FolderId,
                    request.Description,
                    request.UploadedBy);

                await _fileRepository.AddAsync(file, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var dto = file.ToDto();

                return Result<PatientFileDto>.Success(dto);
            }
            catch
            {
                try { await _fileStorage.DeleteAsync(storageKey, cancellationToken); }
                catch { /* best-effort orphan cleanup: don't mask the original failure */ }
                throw;
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error uploading file for patient {PatientId}", request.PatientId);
            return Result<PatientFileDto>.Failure("Erreur lors de l'envoi du fichier.");
        }
    }
}









