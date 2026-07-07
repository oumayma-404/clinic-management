using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using System.Text.RegularExpressions;

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
}

public class CreateMedicalDocumentCommandHandler : IRequestHandler<CreateMedicalDocumentCommand, Result<MedicalDocumentDto>>
{
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public CreateMedicalDocumentCommandHandler(
        IMedicalDocumentRepository documentRepository,
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork)
    {
        _documentRepository = documentRepository;
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<MedicalDocumentDto>> Handle(CreateMedicalDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
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
                var documentTypeName = GetDocumentTypeName(request.DocumentType);
                var sanitizedPatientName = SanitizeFileName(patientName.ToLowerInvariant());
                var baseFileName = $"{documentTypeName}-{sanitizedPatientName}";
                var fileName = await GenerateUniqueFileName(_fileRepository, documentsFolder.Id, baseFileName, "pdf", cancellationToken);

                // Upload PDF to MinIO
                using var pdfStream = new MemoryStream(request.PdfFile);
                var storageKey = await _fileStorage.UploadAsync(pdfStream, "application/pdf", cancellationToken);

                // Create PatientFile entry in "documents" folder
                var patientFile = new PatientFile(
                    Guid.NewGuid(),
                    request.PatientId,
                    fileName,
                    storageKey,
                    "application/pdf",
                    request.PdfFile.Length,
                    FileType.MedicalRecord,
                    documentsFolder.Id, // Always use documents folder for PDFs
                    $"Document médical: {documentTypeName}");

                await _fileRepository.AddAsync(patientFile, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // Save file first
                fileId = patientFile.Id;
            }

            var document = new MedicalDocument(
                Guid.NewGuid(),
                request.PatientId,
                request.DocumentType,
                request.DocumentDate,
                patientName,
                patientAge,
                request.ContentJson,
                request.ClinicName,
                request.ClinicAddress,
                request.ClinicPhone,
                request.DoctorName,
                request.DoctorSpecialty,
                isDraft: false, // Always set to false
                request.RecipientDoctorName,
                request.RecipientDoctorSpecialty,
                fileId);

            await _documentRepository.AddAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt
            };

            return Result<MedicalDocumentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<MedicalDocumentDto>.Failure($"Error creating medical document: {ex.Message}");
        }
    }

    private static string GetDocumentTypeName(string documentType)
    {
        return documentType.ToLowerInvariant() switch
        {
            "prescription" => "ordonnance",
            "liaison" => "lettre-de-liaison",
            "honoraires" => "note-d-honoraires",
            "certificat" => "certificat-medical",
            _ => documentType.ToLowerInvariant()
        };
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

