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

/// <summary>
/// Records a file whose bytes stay in the cabinet's coffre.
///
/// <para>⚠️ <b>This is a registration, not an upload.</b> The original never crosses the wire — the browser wrote
/// it to the coffre and hashed it on the way past — so what arrives is a description plus, when one could be made,
/// a small preview. That is the whole point: at Tunisia's median uplink a CBCT study takes hours to leave the
/// practice, and the deployment would then hold it once live, once per nightly tarball, and once more off-site.</para>
///
/// <para>⚠️ <b><see cref="FileId"/> comes from the client, deliberately.</b> The coffre path is derived from it, so
/// the browser has to know it before it writes the bytes; minting a second id here would name a file that is not
/// on the disk. It is therefore treated as untrusted — refused if it is empty or already taken.</para>
/// </summary>
public class RegisterVaultFileCommand : IRequest<Result<PatientFileDto>>
{
    public Guid PatientId { get; set; }

    /// <summary>The id the browser already used to name the file inside the coffre.</summary>
    public Guid FileId { get; set; }

    public Guid? FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>The original's length, as measured on the cabinet's own disk.</summary>
    public long FileSize { get; set; }

    /// <summary>Lower-case hex SHA-256 of the original, computed in the same pass that wrote it.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? UploadedBy { get; set; }

    /// <summary>A small stand-in image, or null when none could be rendered. Never blocks the registration.</summary>
    public Stream? PreviewStream { get; set; }

    public string? PreviewFileName { get; set; }

    /// <summary>ASP.NET's count of the parsed preview part, used to drop an oversized one before reading it.</summary>
    public long PreviewSize { get; set; }
}

public class RegisterVaultFileCommandHandler : IRequestHandler<RegisterVaultFileCommand, Result<PatientFileDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IFileResidencyPolicy _residencyPolicy;
    private readonly ILogger<RegisterVaultFileCommandHandler> _logger;

    public RegisterVaultFileCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IFileResidencyPolicy residencyPolicy,
        ILogger<RegisterVaultFileCommandHandler> logger)
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

    public async Task<Result<PatientFileDto>> Handle(RegisterVaultFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // The controller does not publish this route where there is no coffre; this is the defence behind it.
            if (!_residencyPolicy.VaultAvailable)
            {
                return Result<PatientFileDto>.Failure(
                    FileResidencyRefusals.Unavailable(), FileResidencyRefusals.UnavailableCode);
            }

            if (request.FileId == Guid.Empty)
            {
                return Result<PatientFileDto>.Failure("L'identifiant du fichier est requis.");
            }

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

            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<PatientFileDto>.Failure("Dossier introuvable ou n'appartenant pas à ce patient.");
                }
            }

            // A repeat of an id already recorded would point a second row at one file on the disk, and deleting
            // either would strand the other.
            var existing = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (existing != null)
            {
                return Result<PatientFileDto>.Failure("Ce fichier a déjà été enregistré.");
            }

            var name = FileNameSanitizer.Sanitize(request.FileName);
            var resolved = FileUploadValidator.ResolveEntry(FileUploadProfile.PatientFile, name);
            if (resolved.IsFailure)
            {
                return Result<PatientFileDto>.FailureFrom(resolved);
            }

            var entry = resolved.Value!;

            if (request.FileSize <= 0)
            {
                return Result<PatientFileDto>.Failure(FileUploadValidator.EmptyFileMessage);
            }

            // The catalog decides residency, so a file the server would gladly hold is sent back to the door that
            // holds it — otherwise the coffre would slowly acquire every small scan too.
            if (_residencyPolicy.Decide(entry, request.FileSize) != FileResidency.Vault)
            {
                return Result<PatientFileDto>.Failure(FileResidencyRefusals.BelongsOnTheServer());
            }

            if (request.FileSize > entry.VaultMaxBytes)
            {
                return Result<PatientFileDto>.Failure(
                    FileResidencyRefusals.TooLarge(entry.VaultMaxBytes), FileResidencyRefusals.TooLargeCode);
            }

            if (!IsSha256Hex(request.ContentHash))
            {
                return Result<PatientFileDto>.Failure("L'empreinte du fichier est absente ou invalide.");
            }

            var previewKey = await PatientFilePreviewStore.StoreAsync(
                _fileStorage,
                _logger,
                request.FileId,
                patient.ClinicId,
                request.PreviewStream,
                request.PreviewFileName,
                request.PreviewSize,
                cancellationToken);

            try
            {
                var file = PatientFile.RegisterInVault(
                    request.FileId,
                    request.PatientId,
                    patient.ClinicId,
                    name,
                    entry.ContentType,
                    request.FileSize,
                    entry.Category,
                    request.ContentHash,
                    previewKey,
                    request.FolderId,
                    request.Description,
                    request.UploadedBy);

                await _fileRepository.AddAsync(file, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<PatientFileDto>.Success(file.ToDto());
            }
            catch
            {
                if (previewKey != null)
                {
                    try { await _fileStorage.DeleteAsync(previewKey, cancellationToken); }
                    catch { /* best-effort orphan cleanup: don't mask the original failure */ }
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error registering vault file for patient {PatientId}", request.PatientId);
            return Result<PatientFileDto>.Failure("Erreur lors de l'enregistrement du fichier.");
        }
    }

    private static bool IsSha256Hex(string? value)
    {
        if (value == null || value.Length != 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
