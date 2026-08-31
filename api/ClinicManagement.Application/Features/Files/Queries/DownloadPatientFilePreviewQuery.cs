using MediatR;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

/// <summary>
/// Serves the small stand-in image for a coffre original, for the machines that cannot reach the coffre.
///
/// <para>⚠️ <b>Its absence is ordinary, not a fault.</b> Nothing renders a preview of an STL yet, and one that came
/// out too big was dropped on purpose — so a missing preview is « we have no picture of this », never « something
/// went wrong ». The caller shows a typed placeholder.</para>
/// </summary>
public class DownloadPatientFilePreviewQuery : IRequest<Result<FileDownloadDto>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
}

public class DownloadPatientFilePreviewQueryHandler
    : IRequestHandler<DownloadPatientFilePreviewQuery, Result<FileDownloadDto>>
{
    private readonly IPatientFileRepository _fileRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _auditActor;
    private readonly IUnitOfWork _unitOfWork;

    public DownloadPatientFilePreviewQueryHandler(
        IPatientFileRepository fileRepository,
        IPatientRepository patientRepository,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider auditActor,
        IUnitOfWork unitOfWork)
    {
        _fileRepository = fileRepository;
        _patientRepository = patientRepository;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
        _auditEntries = auditEntries;
        _auditActor = auditActor;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<FileDownloadDto>> Handle(
        DownloadPatientFilePreviewQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<FileDownloadDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null || file.PatientId != request.PatientId)
            {
                return Result<FileDownloadDto>.Failure("Fichier introuvable.");
            }

            // The same three checks the original's download makes, in the same order — a preview is a picture of
            // a patient's imaging, and is exactly as much theirs as the study it stands for.
            var patient = await _patientRepository.GetByIdAsync(file.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<FileDownloadDto>.Failure("Fichier introuvable.");
            }

            if (string.IsNullOrEmpty(file.PreviewStorageKey))
            {
                return Result<FileDownloadDto>.Failure("Aucun aperçu n'est disponible pour ce fichier.");
            }

            // ⚠️ A preview is the SAME patient content by a second door — a picture of the study, streamed to
            // whoever asks. Auditing the download alone would leave an unrecorded path to the same bytes, which
            // is this repository's own « fixes don't propagate » shape. `PatientFileAccessCoverageTests` is what
            // stops a third door being added without one.
            try
            {
                await PatientRecordAccessLedger.RecordAsync(
                    _auditEntries, _unitOfWork, _auditActor.Current, clinicResult.Value,
                    PatientRecordAccessLedger.FileEntityType, file.PatientId, file.Id,
                    "Aperçu d'une radiographie ou pièce jointe", DateTime.UtcNow, cancellationToken);
            }
            catch (Exception ledgerFailure) when (ledgerFailure is not ConflictException)
            {
                return Result<FileDownloadDto>.Failure(
                    PatientRecordAccessLedger.UnrecordableMessage,
                    PatientRecordAccessLedger.UnrecordableCode);
            }

            var stream = await _fileStorage.DownloadAsync(file.PreviewStorageKey, cancellationToken);

            var dto = new FileDownloadDto
            {
                FileStream = stream,
                FileName = file.FileName,
                ContentType = PreviewContentType(file.PreviewStorageKey)
            };

            return Result<FileDownloadDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<FileDownloadDto>.Failure($"Error downloading preview: {ex.Message}");
        }
    }

    // Derived from the key the registration composed rather than stored beside it: the extension is already the
    // record of what was written, and a second column could only ever disagree with it.
    private static string PreviewContentType(string previewStorageKey)
    {
        var extension = FileNameSanitizer.ExtensionOf(previewStorageKey);

        return FileTypeCatalog.TryGet(extension)?.ContentType ?? FileTypeCatalog.Jpeg.ContentType;
    }
}
