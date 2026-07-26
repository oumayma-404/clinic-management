using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using System.Text.RegularExpressions;

namespace ClinicManagement.Application.Features.Documents.Commands;

public class UpdateMedicalDocumentCommand : IRequest<Result<MedicalDocumentDto>>
{
    public Guid Id { get; set; }
    public DateTime DocumentDate { get; set; }
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    public Guid? FileId { get; set; }
    public byte[]? PdfFile { get; set; } // PDF file as byte array (optional, for updating the file)
}

public class UpdateMedicalDocumentCommandHandler : IRequestHandler<UpdateMedicalDocumentCommand, Result<MedicalDocumentDto>>
{
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateMedicalDocumentCommandHandler> _logger;

    public UpdateMedicalDocumentCommandHandler(
        IMedicalDocumentRepository documentRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateMedicalDocumentCommandHandler> logger)
    {
        _documentRepository = documentRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MedicalDocumentDto>> Handle(UpdateMedicalDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (document == null)
            {
                return Result<MedicalDocumentDto>.Failure("Document médical introuvable.");
            }

            // Verify the document's owning patient belongs to the caller's clinic. Skipped when there is
            // no clinic in scope — PdfGenerationJob updates the document from a background scope with no
            // authenticated user (DEV-1, mirrors the global filter's AC-3 rule).
            var userId = _clinicContext.GetUserId();
            User? user = null;
            if (!string.IsNullOrEmpty(userId))
            {
                user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
                if (user == null || document.Patient == null || document.Patient.ClinicId != user.ClinicId)
                {
                    return Result<MedicalDocumentDto>.Failure("Document médical introuvable.");
                }
            }

            // FR-1.4: the "note d'honoraires" document type is retired. Existing (legacy) honoraires
            // documents remain readable via Get/List, but are no longer updated/re-rendered here.
            if (document.DocumentType.Trim().ToLowerInvariant() == DocumentTypes.Honoraires)
            {
                return Result<MedicalDocumentDto>.Failure(
                    "Le type « note d'honoraires » n'est plus disponible. Créez une facture depuis le module Factures.");
            }

            // FR-4.1/FR-4.2: mirror the create-path guard — a lettre de liaison must keep a recipient name
            // on edit (the only required field). A valid liaison always carries one, so the background job's
            // re-render (which passes the stored recipient) is unaffected.
            if (document.DocumentType.Trim().ToLowerInvariant() == DocumentTypes.Liaison
                && string.IsNullOrWhiteSpace(request.RecipientDoctorName))
            {
                return Result<MedicalDocumentDto>.Failure(
                    "Le nom du confrère destinataire est obligatoire pour une lettre de liaison.");
            }

            // FR-2.2 / FR-3.3: re-apply the practitioner/clinic snapshot on a genuine user edit, exactly as
            // the create path does — otherwise the structured editor (which rebuilds ContentJson from its
            // own form fields) would drop the cachet key + cabinet city + ordre on every save. Only when a
            // caller is authenticated: the background PdfGenerationJob runs with no user and feeds the
            // already-snapshotted stored ContentJson back through here, so it must be preserved verbatim
            // (ApplyTo would strip the reserved keys with no caller-doctor to re-add them).
            var contentJson = request.ContentJson;
            if (user != null)
            {
                // Re-apply the practitioner/clinic snapshot (the structured editor rebuilds ContentJson from
                // its own fields and drops the reserved keys). Resolve from the caller's own doctor record —
                // but a caller without one (a secretary/admin managing paperwork) would otherwise blank the
                // cachet + CNOMDT ordre. Fall back per-field to the values already snapshotted on the stored
                // document so an edit never strips the issuing practitioner's identity; client-supplied
                // reserved keys are still stripped by ApplyTo (only these trusted server values are written).
                var callerSnapshot = await PractitionerRenderSnapshot.ResolveAsync(
                    userId, user.ClinicId, _doctorRepository, _clinicRepository, cancellationToken);
                var effectiveSnapshot = callerSnapshot.OrElse(PractitionerRenderSnapshot.ReadFrom(document.ContentJson));
                contentJson = effectiveSnapshot.ApplyTo(request.ContentJson);
            }

            Guid? fileId = request.FileId ?? document.FileId;

            // Storage key of the file being replaced, if any. Its blob is deleted only AFTER the whole
            // update is committed, so a failed save never strands the document pointing at a missing blob (FR-C3).
            string? previousStorageKey = null;

            // If PDF file is provided, save it to patient files
            if (request.PdfFile != null && request.PdfFile.Length > 0)
            {
                // ALWAYS save PDF files to "documents" folder (not "brouillons")
                // This ensures all PDFs are in the documents folder regardless of draft status
                const string folderName = "documents";

                // Get or create the "documents" folder for this patient
                var folder = await _folderRepository.GetByNameAndPatientIdAsync(folderName, document.PatientId, cancellationToken);

                if (folder == null)
                {
                    folder = new PatientFolder(
                        Guid.NewGuid(),
                        document.PatientId,
                        folderName);
                    await _folderRepository.AddAsync(folder, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                // Generate filename with collision detection
                var documentTypeName = DocumentFileNaming.GetDocumentTypeName(document.DocumentType);
                var sanitizedPatientName = SanitizeFileName(document.PatientName.ToLowerInvariant());
                var baseFileName = $"{documentTypeName}-{sanitizedPatientName}";
                var fileName = await GenerateUniqueFileName(_fileRepository, folder.Id, baseFileName, "pdf", cancellationToken);

                // Store the PDF blob first, then persist the record. If the DB save fails we must
                // remove the just-stored blob so no orphan remains (FR-C3).
                using var pdfStream = new MemoryStream(request.PdfFile);
                var storageKey = await _fileStorage.UploadAsync(pdfStream, "application/pdf", cancellationToken);

                try
                {
                    // If the document already has a file, remove its record now (committed together with the
                    // new record below). Its blob is deleted only after the whole update commits — deleting it
                    // here would strand the document on a missing blob if a later save fails.
                    if (document.FileId.HasValue)
                    {
                        var oldFile = await _fileRepository.GetByIdAsync(document.FileId.Value, cancellationToken);
                        if (oldFile != null)
                        {
                            previousStorageKey = oldFile.StorageKey;
                            await _fileRepository.DeleteAsync(oldFile, cancellationToken);
                        }
                    }

                    // Create new PatientFile entry
                    var patientFile = new PatientFile(
                        Guid.NewGuid(),
                        document.PatientId,
                        fileName,
                        storageKey,
                        "application/pdf",
                        request.PdfFile.Length,
                        FileType.MedicalRecord,
                        folder.Id,
                        $"Document médical: {documentTypeName}");

                    await _fileRepository.AddAsync(patientFile, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken); // Save file immediately
                    fileId = patientFile.Id;
                }
                catch
                {
                    try { await _fileStorage.DeleteAsync(storageKey, cancellationToken); }
                    catch { /* best-effort orphan cleanup: don't mask the original failure */ }
                    throw;
                }
            }

            // Update document
            document.Update(
                request.DocumentDate,
                contentJson,
                request.RecipientDoctorName,
                request.RecipientDoctorSpecialty,
                isDraft: null, // Don't update draft status
                fileId);

            await _documentRepository.UpdateAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The replacement is now committed and the document points at the new file; drop the old blob.
            // Best-effort: a leaked blob is preferable to deleting one the document might still reference.
            if (previousStorageKey != null)
            {
                try { await _fileStorage.DeleteAsync(previousStorageKey, cancellationToken); }
                catch { /* best-effort cleanup of the replaced file's blob */ }
            }

            var dto = new MedicalDocumentDto
            {
                Id = document.Id,
                PatientId = document.PatientId,
                PatientName = document.PatientName,
                PatientAge = document.PatientAge,
                DocumentType = document.DocumentType,
                DocumentDate = document.DocumentDate,
                RecipientDoctorName = document.RecipientDoctorName,
                RecipientDoctorSpecialty = document.RecipientDoctorSpecialty,
                ContentJson = document.ContentJson,
                ClinicName = document.ClinicName,
                ClinicAddress = document.ClinicAddress,
                ClinicPhone = document.ClinicPhone,
                DoctorName = document.DoctorName,
                DoctorSpecialty = document.DoctorSpecialty,
                IsDraft = document.IsDraft,
                FileId = document.FileId,
                AppointmentId = document.AppointmentId,
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt
            };

            return Result<MedicalDocumentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating medical document {DocumentId}", request.Id);
            return Result<MedicalDocumentDto>.Failure("Erreur lors de la mise à jour du document médical.");
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        // Remove special characters and replace spaces with hyphens
        var sanitized = Regex.Replace(fileName, @"[^a-z0-9\s-]", "");
        sanitized = Regex.Replace(sanitized, @"\s+", "-");
        return sanitized.Trim('-');
    }

    private static async Task<string> GenerateUniqueFileName(
        IPatientFileRepository fileRepository,
        Guid folderId,
        string baseFileName,
        string extension,
        CancellationToken cancellationToken)
    {
        var fileName = $"{baseFileName}.{extension}";
        var filesInFolder = await fileRepository.GetByFolderIdAsync(folderId, cancellationToken);
        var existingFileNames = filesInFolder.Select(f => f.FileName.ToLowerInvariant()).ToHashSet();

        if (!existingFileNames.Contains(fileName.ToLowerInvariant()))
        {
            return fileName;
        }

        // File exists, add number suffix
        int counter = 1;
        string newFileName;
        do
        {
            newFileName = $"{baseFileName}({counter}).{extension}";
            counter++;
        } while (existingFileNames.Contains(newFileName.ToLowerInvariant()));

        return newFileName;
    }
}
