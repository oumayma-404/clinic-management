using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace ClinicManagement.Application.Features.Documents.Commands;

public class CreateMedicalDocumentCommand : IRequest<Result<MedicalDocumentDto>>
{
    public Guid PatientId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    public string ClinicName { get; set; } = string.Empty;
    public string ClinicAddress { get; set; } = string.Empty;
    public string ClinicPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string DoctorSpecialty { get; set; } = string.Empty;
    public byte[]? PdfFile { get; set; } // PDF file as byte array (optional, generated on frontend)
    public Guid? AppointmentId { get; set; } // Optional link to the documented appointment (post-visit review)
}

public class CreateMedicalDocumentCommandHandler : IRequestHandler<CreateMedicalDocumentCommand, Result<MedicalDocumentDto>>
{
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateMedicalDocumentCommandHandler> _logger;

    public CreateMedicalDocumentCommandHandler(
        IMedicalDocumentRepository documentRepository,
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        INotificationGenerator notificationGenerator,
        IRealtimeNotifier realtimeNotifier,
        IUnitOfWork unitOfWork,
        ILogger<CreateMedicalDocumentCommandHandler> logger)
    {
        _documentRepository = documentRepository;
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _notificationGenerator = notificationGenerator;
        _realtimeNotifier = realtimeNotifier;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MedicalDocumentDto>> Handle(CreateMedicalDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // FR-1.4: the "note d'honoraires" document type is retired — compliant honoraires are now issued
            // through the Invoice pipeline (module Factures). No new honoraires MedicalDocument is created.
            if (request.DocumentType.Trim().ToLowerInvariant() == "honoraires")
            {
                return Result<MedicalDocumentDto>.Failure(
                    "Le type « note d'honoraires » n'est plus disponible. Créez une facture depuis le module Factures.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<MedicalDocumentDto>.Failure("Patient not found");
            }

            // Calculate patient age
            string? patientAge = null;
            if (patient.DateOfBirth != default)
            {
                var today = DateTime.UtcNow;
                var age = today.Year - patient.DateOfBirth.Year;
                if (patient.DateOfBirth.Date > today.AddYears(-age)) age--;
                patientAge = $"{age} ans";
            }

            var patientName = $"{patient.FirstName} {patient.LastName}".Trim();

            Guid? fileId = null;

            // Get or create the documents folder
            // ALWAYS create folder, even if no PDF yet - this ensures folder structure exists
            var folderName = "documents";
            var folder = await _folderRepository.GetByNameAndPatientIdAsync(folderName, request.PatientId, cancellationToken);
            
            if (folder == null)
            {
                folder = new PatientFolder(
                    Guid.NewGuid(),
                    request.PatientId,
                    folderName);
                await _folderRepository.AddAsync(folder, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // If PDF file is provided, save it to patient files
            // IMPORTANT: Always save PDFs to "documents" folder, even if document is a draft
            // This ensures all PDFs are accessible in the documents folder
            byte[]? pdfFileToSave = request.PdfFile;
            if (pdfFileToSave != null && pdfFileToSave.Length > 0)
            {
                // Ensure "documents" folder exists (not "brouillons")
                var documentsFolderName = "documents";
                var documentsFolder = await _folderRepository.GetByNameAndPatientIdAsync(documentsFolderName, request.PatientId, cancellationToken);
                
                if (documentsFolder == null)
                {
                    documentsFolder = new PatientFolder(
                        Guid.NewGuid(),
                        request.PatientId,
                        documentsFolderName);
                    await _folderRepository.AddAsync(documentsFolder, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                // Generate filename with collision detection
                var documentTypeName = DocumentFileNaming.GetDocumentTypeName(request.DocumentType);
                var sanitizedPatientName = SanitizeFileName(patientName.ToLowerInvariant());
                var baseFileName = $"{documentTypeName}-{sanitizedPatientName}";
                var fileName = await GenerateUniqueFileName(_fileRepository, documentsFolder.Id, baseFileName, "pdf", cancellationToken);

                // Store the PDF blob first, then persist the record. If the DB save fails we must
                // remove the just-stored blob so no orphan remains (FR-C3).
                using var pdfStream = new MemoryStream(pdfFileToSave);
                var storageKey = await _fileStorage.UploadAsync(pdfStream, "application/pdf", cancellationToken);

                try
                {
                    // Create PatientFile entry in "documents" folder
                    var patientFile = new PatientFile(
                        Guid.NewGuid(),
                        request.PatientId,
                        fileName,
                        storageKey,
                        "application/pdf",
                        pdfFileToSave.Length,
                        FileType.MedicalRecord,
                        documentsFolder.Id, // Always use documents folder for PDFs
                        $"Document médical: {documentTypeName}");

                    await _fileRepository.AddAsync(patientFile, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken); // Save file first
                    fileId = patientFile.Id;
                }
                catch
                {
                    try { await _fileStorage.DeleteAsync(storageKey, cancellationToken); }
                    catch { /* best-effort orphan cleanup: don't mask the original failure */ }
                    throw;
                }
            }

            // FR-3.3 / FR-6.1: snapshot the issuing practitioner's cachet + CNOMDT ordre and the cabinet
            // city into ContentJson at creation, so the unauthenticated background PDF job can render them
            // without a live doctor/clinic lookup. Best-effort: a resolution problem must never fail the
            // document creation — the document simply carries no snapshot (renderer falls back cleanly).
            var contentJson = await SnapshotPractitionerAndClinicAsync(request.ContentJson, cancellationToken);

            var document = new MedicalDocument(
                Guid.NewGuid(),
                request.PatientId,
                request.DocumentType,
                request.DocumentDate,
                patientName,
                patientAge,
                contentJson,
                request.ClinicName,
                request.ClinicAddress,
                request.ClinicPhone,
                request.DoctorName,
                request.DoctorSpecialty,
                isDraft: false, // Always set to false
                request.RecipientDoctorName,
                request.RecipientDoctorSpecialty,
                fileId,
                request.AppointmentId);

            await _documentRepository.AddAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Post-visit review completion (best-effort, post-commit): filling a record for an appointment
            // marks that appointment Completed and clears its pending review. A failure here must never fail
            // the record creation (spec AC-7 + the "best-effort side-effects" learning).
            if (request.AppointmentId.HasValue)
            {
                await CompleteReviewedAppointmentAsync(request.AppointmentId.Value, cancellationToken);
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
            _logger.LogError(ex, "Error creating medical document for patient {PatientId}", request.PatientId);
            return Result<MedicalDocumentDto>.Failure("Error creating medical document.");
        }
    }

    // Marks the documented appointment Completed (if it resolves in the caller's clinic) and removes its
    // pending post-visit review. Clinic is resolved from the DB (not the fail-open global query filter),
    // so a cross-clinic/missing id is a silent no-op. Wrapped so any failure only logs — never rolls back
    // the already-committed medical record.
    private async Task CompleteReviewedAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return;
            }

            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, cancellationToken);
            if (appointment == null || appointment.ClinicId != clinicResult.Value)
            {
                return; // cross-clinic or unknown id → leave everything unchanged
            }

            appointment.MarkVisitCompleted(); // idempotent no-op if already terminal
            // No explicit UpdateAsync: the appointment is change-tracked from GetByIdAsync, so SaveChanges
            // persists MarkVisitCompleted() on its own (repo "rely on EF change tracking" convention).
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The review is fulfilled — remove it so the popup/panel stops prompting.
            await _notificationGenerator.CancelPostVisitReviewAsync(appointment.ClinicId, appointmentId, cancellationToken);

            // The completion is driven from the "documents" command, so RealtimeBroadcastBehavior only tells
            // "documents" consumers to refetch — broadcast "appointments" too so calendar/appointment views
            // reflect the now-Completed status instead of staying stale until the next unrelated refetch.
            await _realtimeNotifier.NotifyEntityChangedAsync(appointment.ClinicId, "appointments", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-visit completion side-effect failed for appointment {AppointmentId}", appointmentId);
        }
    }

    // Merges the current practitioner's cachet/ordre + the cabinet city into the document's ContentJson
    // (FR-3.3 / FR-6.1). The keys are shared with the renderers via PractitionerRenderSnapshot. Any failure
    // (unresolved clinic, malformed JSON, missing doctor) falls back to the original ContentJson unchanged —
    // the snapshot is an enrichment, never a gate on document creation.
    private async Task<string> SnapshotPractitionerAndClinicAsync(string originalContentJson, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return originalContentJson;
            }

            var userId = _clinicContext.GetUserId();
            var snapshot = await PractitionerRenderSnapshot.ResolveAsync(
                userId, clinicResult.Value, _doctorRepository, _clinicRepository, cancellationToken);

            if (!snapshot.HasAny)
            {
                return originalContentJson;
            }

            var node = JsonNode.Parse(originalContentJson);
            if (node is not JsonObject content)
            {
                return originalContentJson;
            }

            // Only write present values; the renderer treats absent keys as "no cachet/city" and falls back.
            if (!string.IsNullOrWhiteSpace(snapshot.ClinicCity))
                content[PractitionerRenderSnapshot.ClinicCityKey] = snapshot.ClinicCity;
            if (!string.IsNullOrWhiteSpace(snapshot.DoctorOrdreNumber))
                content[PractitionerRenderSnapshot.DoctorOrdreNumberKey] = snapshot.DoctorOrdreNumber;
            if (!string.IsNullOrWhiteSpace(snapshot.DoctorCachetKey))
            {
                content[PractitionerRenderSnapshot.DoctorCachetKeyKey] = snapshot.DoctorCachetKey;
                content[PractitionerRenderSnapshot.DoctorCachetContentTypeKey] = snapshot.DoctorCachetContentType;
            }

            return content.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to snapshot practitioner/clinic data onto the document; continuing without it");
            return originalContentJson;
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

