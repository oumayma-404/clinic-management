using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

public class DownloadPatientFileQuery : IRequest<Result<FileDownloadDto>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
}

public class FileDownloadDto
{
    public Stream FileStream { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// How many bytes the store holds, when it could say — the response's <c>Content-Length</c>.
    ///
    /// <para>⚠️ <b>It exists because the download stream stopped being seekable.</b> ASP.NET derives
    /// <c>Content-Length</c> from a seekable stream's own length, so buffering the whole object in memory used to
    /// supply it as a side effect; streaming does not, and without this a browser downloading a study reports
    /// « unknown size » with no progress bar — on a slow connection, exactly when somebody needs one.</para>
    ///
    /// <para>⚠️ <b>Asked of the store, never read off <c>PatientFile.FileSize</c>.</b> That column looks like
    /// the same number and is the <i>client's claim</i> for any row written before upload validation existed; a
    /// wrong <c>Content-Length</c> truncates or hangs a response rather than merely misreporting it. Null when
    /// the backend could not say, which is an ordinary answer and simply omits the header.</para>
    /// </summary>
    public long? Length { get; set; }
}

public class DownloadPatientFileQueryHandler : IRequestHandler<DownloadPatientFileQuery, Result<FileDownloadDto>>
{
    private readonly IPatientFileRepository _fileRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _auditActor;
    private readonly IUnitOfWork _unitOfWork;

    public DownloadPatientFileQueryHandler(
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

    public async Task<Result<FileDownloadDto>> Handle(DownloadPatientFileQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<FileDownloadDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null)
            {
                return Result<FileDownloadDto>.Failure("Fichier introuvable.");
            }

            if (file.PatientId != request.PatientId)
            {
                return Result<FileDownloadDto>.Failure("Ce fichier n'appartient pas à ce patient.");
            }

            // Verify the owning patient belongs to the caller's clinic before streaming any bytes (AC-1).
            var patient = await _patientRepository.GetByIdAsync(file.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<FileDownloadDto>.Failure("Fichier introuvable.");
            }

            // A coffre original was never transmitted here, so there is nothing to stream — and saying so names
            // where the file is instead of reporting a failure the practice would go looking for.
            if (file.Residency != FileResidency.Hosted || file.StorageKey == null)
            {
                return Result<FileDownloadDto>.Failure(FileResidencyRefusals.OriginalIsAtTheCabinet());
            }

            // Recorded BEFORE the bytes are fetched — this is the row that answers « qui a sorti la
            // radiographie de ce patient ? », which nothing in the product could answer at all. Not
            // best-effort: see PatientRecordAccessLedger on why refusing here cannot strand a practitioner.
            //
            // ⚠️ Outside the outer catch on purpose. That one turns any exception into
            // `Result.Failure($"Error downloading file: {ex.Message}")`, which would report an unrecordable
            // access as an ordinary download failure — and leak the exception text with it.
            try
            {
                await PatientRecordAccessLedger.RecordAsync(
                    _auditEntries, _unitOfWork, _auditActor.Current, clinicResult.Value,
                    PatientRecordAccessLedger.FileEntityType, file.PatientId, file.Id,
                    "Radiographie ou pièce jointe", DateTime.UtcNow, cancellationToken);
            }
            catch (Exception ledgerFailure) when (ledgerFailure is not ConflictException)
            {
                return Result<FileDownloadDto>.Failure(
                    PatientRecordAccessLedger.UnrecordableMessage,
                    PatientRecordAccessLedger.UnrecordableCode);
            }

            // Before the stream, so a store that cannot answer costs nothing and one that can does not have the
            // question asked while a read is already open against it.
            var length = await _fileStorage.GetLengthAsync(file.StorageKey, cancellationToken);
            var fileStream = await _fileStorage.DownloadAsync(file.StorageKey, cancellationToken);

            var dto = new FileDownloadDto
            {
                FileStream = fileStream,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Length = length
            };

            return Result<FileDownloadDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<FileDownloadDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
