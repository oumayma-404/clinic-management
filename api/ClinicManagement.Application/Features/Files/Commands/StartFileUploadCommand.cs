using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>What a client needs to send the next chunk, and to know where it got to.</summary>
public class FileUploadSessionDto
{
    public Guid UploadId { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>The size of every part but the last — the unit the resume arithmetic is done in.</summary>
    public int ChunkSize { get; set; }

    public long DeclaredLength { get; set; }
    public int TotalParts { get; set; }
    public int ReceivedParts { get; set; }
    public long ReceivedBytes { get; set; }

    /// <summary>The only part this upload will accept next. What a resuming client asks for.</summary>
    public int NextPart { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// Opens a resumable upload: judges everything that can be judged from a name and a length, reserves a staging
/// area, and hands back the chunk size.
///
/// <para>⚠️ <b>The refusals happen here, before a single byte crosses the wire.</b> That is the point of asking
/// for the length up front — a 200 Mo file of a format this deployment does not take should cost the clinic
/// nothing, and a refusal after four minutes of uploading is the failure this whole feature exists to end.</para>
///
/// <para>⚠️ <b>The signature cannot be checked yet</b>, because the bytes are not here. It is checked on the
/// first chunk, which is where the header arrives — see <see cref="UploadFileChunkCommandHandler"/>.</para>
/// </summary>
public class StartFileUploadCommand : IRequest<Result<FileUploadSessionDto>>, IDoesNotBroadcast
{
    public Guid PatientId { get; set; }
    public Guid? FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? Description { get; set; }
    public string? UploadedBy { get; set; }
}

public class StartFileUploadCommandHandler
    : IRequestHandler<StartFileUploadCommand, Result<FileUploadSessionDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IFileUploadSessionRepository _sessions;
    private readonly IResumableUploadStore _uploadStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IFileResidencyPolicy _residencyPolicy;
    private readonly ClinicStorageAllowance _storage;
    private readonly ILogger<StartFileUploadCommandHandler> _logger;

    public StartFileUploadCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IFileUploadSessionRepository sessions,
        IResumableUploadStore uploadStore,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IFileResidencyPolicy residencyPolicy,
        ClinicStorageAllowance storage,
        ILogger<StartFileUploadCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _sessions = sessions;
        _uploadStore = uploadStore;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _residencyPolicy = residencyPolicy;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result<FileUploadSessionDto>> Handle(
        StartFileUploadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<FileUploadSessionDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<FileUploadSessionDto>.Failure("Patient introuvable.");
            }

            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<FileUploadSessionDto>.Failure(
                        "Dossier introuvable ou n'appartenant pas à ce patient.");
                }
            }

            var profile = FileUploadProfile.PatientFile;
            var name = FileNameSanitizer.Sanitize(request.FileName);

            // The half of the judgement that needs only a name — extension present, not deny-listed, known to
            // this door — asked in the same order and refused in the same words as an ordinary upload.
            var resolved = FileUploadValidator.ResolveEntry(profile, name);
            if (resolved.IsFailure)
            {
                return Result<FileUploadSessionDto>.FailureFrom(resolved);
            }

            var entry = resolved.Value!;

            if (request.FileSize <= 0)
            {
                return Result<FileUploadSessionDto>.Failure(FileUploadValidator.EmptyFileMessage);
            }

            var maxBytes = profile.CapFor(entry);
            if (request.FileSize > maxBytes)
            {
                return Result<FileUploadSessionDto>.Failure(FileUploadValidator.TooLargeMessage(maxBytes));
            }

            // The catalog decides where a file belongs, and this door only holds the ones the deployment keeps.
            if (_residencyPolicy.Decide(entry, request.FileSize) != FileResidency.Hosted)
            {
                return Result<FileUploadSessionDto>.Failure(FileResidencyRefusals.BelongsInTheVault());
            }

            // large-file-transfer Part 4 — refused HERE, before a byte is sent, which is the whole reason this
            // door asks for a length up front. « Vous n'avez plus d'espace » discovered after four minutes of a
            // clinic's uplink is exactly the failure Part 2 exists to end.
            //
            // ⚠️ The length is the client's CLAIM at this point, and that is sound in the direction that
            // matters: it is checked again against the measured total at completion, and a client understating
            // it only postpones its own refusal. What it buys is an honest « no » in the first request.
            var room = await _storage.EnsureRoomForAsync(patient.ClinicId, request.FileSize, cancellationToken);
            if (room.IsFailure)
            {
                return Result<FileUploadSessionDto>.FailureFrom(room);
            }

            var reference = await _uploadStore.BeginAsync(patient.ClinicId, cancellationToken);

            var session = new FileUploadSession(
                Guid.NewGuid(),
                patient.ClinicId,
                request.PatientId,
                name,
                entry.ContentType,
                request.FileSize,
                reference,
                (int)FileTypeCatalog.UploadChunkBytes,
                DateTime.UtcNow,
                request.FolderId,
                request.Description,
                request.UploadedBy);

            await _sessions.AddAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<FileUploadSessionDto>.Success(session.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error starting an upload for patient {PatientId}", request.PatientId);
            return Result<FileUploadSessionDto>.Failure("Erreur lors de l'ouverture de l'envoi.");
        }
    }
}

/// <summary>The one projection, so the four endpoints cannot describe the same session differently.</summary>
public static class FileUploadSessionMapping
{
    public static FileUploadSessionDto ToDto(this FileUploadSession session) => new()
    {
        UploadId = session.Id,
        FileName = session.FileName,
        ChunkSize = session.ChunkSize,
        DeclaredLength = session.DeclaredLength,
        TotalParts = session.TotalParts,
        ReceivedParts = session.ReceivedParts,
        ReceivedBytes = session.ReceivedBytes,
        NextPart = session.NextPart,
        ExpiresAtUtc = session.ExpiresAtUtc,
    };
}
