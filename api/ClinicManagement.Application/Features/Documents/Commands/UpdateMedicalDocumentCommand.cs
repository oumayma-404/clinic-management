using MediatR;
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
    private readonly IUnitOfWork _unitOfWork;

    public UpdateMedicalDocumentCommandHandler(
        IMedicalDocumentRepository documentRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork)
    {
        _documentRepository = documentRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<MedicalDocumentDto>> Handle(UpdateMedicalDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (document == null)
            {
                return Result<MedicalDocumentDto>.Failure("Medical document not found");
            }

            Guid? fileId = request.FileId ?? document.FileId;

            // If PDF file is provided, save it to patient files
            if (request.PdfFile != null && request.PdfFile.Length > 0)
            {
                // ALWAYS save PDF files to "documents" folder (not "brouillons")
                // This ensures all PDFs are in the documents folder regardless of draft status
                const string folderName = "documents";
                
                System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Saving PDF to folder: {folderName}, PatientId: {document.PatientId}, PDF size: {request.PdfFile.Length} bytes");
                
                // Get or create the "documents" folder for this patient
                var folder = await _folderRepository.GetByNameAndPatientIdAsync(folderName, document.PatientId, cancellationToken);
                
                if (folder == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Folder '{folderName}' not found for patient {document.PatientId}, creating new folder");
                    folder = new PatientFolder(
                        Guid.NewGuid(),
                        document.PatientId,
                        folderName);
                    await _folderRepository.AddAsync(folder, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Folder '{folderName}' created with ID: {folder.Id}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Folder '{folderName}' found with ID: {folder.Id}");
                }

                // Generate filename with collision detection
                var documentTypeName = GetDocumentTypeName(document.DocumentType);
                var sanitizedPatientName = SanitizeFileName(document.PatientName.ToLowerInvariant());
                var baseFileName = $"{documentTypeName}-{sanitizedPatientName}";
                var fileName = await GenerateUniqueFileName(_fileRepository, folder.Id, baseFileName, "pdf", cancellationToken);
                
                System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Generated filename: {fileName}");

                // Upload PDF to MinIO
                using var pdfStream = new MemoryStream(request.PdfFile);
                var storageKey = await _fileStorage.UploadAsync(pdfStream, "application/pdf", cancellationToken);
                
                System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] PDF uploaded to storage, key: {storageKey}");

                // If document already has a file, delete the old one
                if (document.FileId.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Document already has file ID: {document.FileId.Value}, deleting old file");
                    var oldFile = await _fileRepository.GetByIdAsync(document.FileId.Value, cancellationToken);
                    if (oldFile != null)
                    {
                        await _fileStorage.DeleteAsync(oldFile.StorageKey, cancellationToken);
                        await _fileRepository.DeleteAsync(oldFile, cancellationToken);
                        System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Old file deleted");
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

                System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] Creating PatientFile: Id={patientFile.Id}, FolderId={folder.Id}, FileName={fileName}");
                
                await _fileRepository.AddAsync(patientFile, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // Save file immediately
                fileId = patientFile.Id;
                
                System.Diagnostics.Debug.WriteLine($"[UpdateMedicalDocumentCommand] PatientFile saved successfully with ID: {fileId}");
            }

            // Update document
            document.Update(
                request.DocumentDate,
                request.ContentJson,
                request.RecipientDoctorName,
                request.RecipientDoctorSpecialty,
                isDraft: null, // Don't update draft status
                fileId);

            await _documentRepository.UpdateAsync(document, cancellationToken);
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
            return Result<MedicalDocumentDto>.Failure($"Error updating medical document: {ex.Message}");
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

