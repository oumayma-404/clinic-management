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

    public DownloadPatientFilePreviewQueryHandler(
        IPatientFileRepository fileRepository,
        IPatientRepository patientRepository,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver)
    {
        _fileRepository = fileRepository;
        _patientRepository = patientRepository;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
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

            // ⚠️ **Deliberately NOT recorded in the access ledger, unlike the download beside it**, and the
            // reason is volume rather than sensitivity. This route serves the thumbnail behind every tile in a
            // patient's file list and the in-app viewer — so recording it writes a row per tile scrolled past,
            // and the journal's whole job is to answer « who took a copy of this patient's file? ». Hundreds of
            // « consulté » rows a day bury the handful that matter, which is the argument that already keeps
            // `Notification` off the audit interceptor.
            //
            // What IS recorded is the original leaving: `DownloadPatientFileQuery`, and the dossier export. A
            // preview is a downscaled stand-in served inside the application to somebody who already has the
            // patient's file open. `PatientFileAccessCoverageTests` carries this as a named exemption, so the
            // decision is stated rather than inferred from an absence.
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
