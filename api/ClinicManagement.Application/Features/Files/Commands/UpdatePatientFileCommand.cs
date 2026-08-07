using System.Text.Json.Serialization;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Rename a file, describe it, or move it between folders (AC-4.2) — the first caller of
/// <c>PatientFile.Rename</c>, <c>UpdateDescription</c> and <c>MoveToFolder</c>, the last two of which had
/// existed with zero callers.
///
/// <para>Every field is <b>tri-state</b>, the repo's standing convention: omit the key to leave the value alone,
/// send <c>null</c>/<c>""</c> to clear it. Plain nullability cannot express « clear the description », which is
/// the whole reason the mechanism exists (<c>UpdatePatientCommand</c>).</para>
/// </summary>
public class UpdatePatientFileCommand : IRequest<Result<PatientFileDto>>
{
    [JsonIgnore]
    public Guid PatientId { get; set; }

    [JsonIgnore]
    public Guid FileId { get; set; }

    /// <summary>The new <b>base</b> name, without extension — the extension is the stored one (AC-4.1).</summary>
    public string? FileName
    {
        get => _fileName;
        set { _fileName = value; FileNameSpecified = true; }
    }
    private string? _fileName;

    [JsonIgnore]
    public bool FileNameSpecified { get; private set; }

    public string? Description
    {
        get => _description;
        set { _description = value; DescriptionSpecified = true; }
    }
    private string? _description;

    [JsonIgnore]
    public bool DescriptionSpecified { get; private set; }

    /// <summary>The destination folder; an explicit <c>null</c> moves the file back to the patient's root.</summary>
    public Guid? FolderId
    {
        get => _folderId;
        set { _folderId = value; FolderIdSpecified = true; }
    }
    private Guid? _folderId;

    [JsonIgnore]
    public bool FolderIdSpecified { get; private set; }
}

public class UpdatePatientFileCommandHandler : IRequestHandler<UpdatePatientFileCommand, Result<PatientFileDto>>
{
    private readonly IPatientFileRepository _fileRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public UpdatePatientFileCommandHandler(
        IPatientFileRepository fileRepository,
        IPatientFolderRepository folderRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _fileRepository = fileRepository;
        _folderRepository = folderRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFileDto>> Handle(UpdatePatientFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // PatientFile carries no ClinicId and is outside the global query filter, so this is the only
            // tenant guard there is (AC-4.5).
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

            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null || file.PatientId != request.PatientId)
            {
                return Result<PatientFileDto>.Failure("Fichier introuvable.");
            }

            if (request.FileNameSpecified)
            {
                var baseName = FileNameSanitizer.SanitizeBaseName(
                    request.FileName, FileNameSanitizer.ExtensionOf(file.FileName));

                if (baseName.Length == 0)
                {
                    return Result<PatientFileDto>.Failure("Le nom du fichier est requis.");
                }

                file.Rename(baseName);
            }

            if (request.DescriptionSpecified)
            {
                var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                file.UpdateDescription(description);
            }

            if (request.FolderIdSpecified && request.FolderId != file.FolderId)
            {
                if (request.FolderId.HasValue)
                {
                    // A folder of another patient is refused here, not by a FK: the two rows are both reachable
                    // to this caller's clinic, so nothing below would notice (AC-4.5).
                    var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                    if (folder == null || folder.PatientId != request.PatientId)
                    {
                        return Result<PatientFileDto>.Failure("Dossier introuvable ou n'appartenant pas à ce patient.");
                    }
                }

                file.MoveToFolder(request.FolderId);
            }

            await _fileRepository.UpdateAsync(file, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new PatientFileDto
            {
                Id = file.Id,
                PatientId = file.PatientId,
                FolderId = file.FolderId,
                FileName = file.FileName,
                ContentType = file.ContentType,
                FileSize = file.FileSize,
                FileType = file.FileType.ToString(),
                Description = file.Description,
                UploadedAt = file.UploadedAt,
                UploadedBy = file.UploadedBy
            };

            return Result<PatientFileDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFileDto>.Failure($"Error updating file: {ex.Message}");
        }
    }
}
