using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.PhotoImport.Queries;

public sealed record ImportedCaptureDto(string CaptureKey, Guid FileId, Guid PatientId, string PatientName);

/// <summary>
/// Which of a card's shots this cabinet has already filed — read before anything is uploaded, so re-inserting a card
/// costs one round trip and the screen can say « 112 déjà importées » (<c>features/camera-photo-import</c>).
///
/// <para>⚠️ A <b>Query</b>, so it broadcasts nothing; and it lives outside <c>Features/Files</c> only because its
/// namespace names the screen it serves — no realtime key is derived from a query.</para>
/// </summary>
public class GetImportedCaptureKeysQuery : IRequest<Result<IReadOnlyList<ImportedCaptureDto>>>
{
    public IReadOnlyList<string> CaptureKeys { get; set; } = Array.Empty<string>();
}

public class GetImportedCaptureKeysQueryHandler
    : IRequestHandler<GetImportedCaptureKeysQuery, Result<IReadOnlyList<ImportedCaptureDto>>>
{
    private readonly IPatientFileRepository _files;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetImportedCaptureKeysQueryHandler(IPatientFileRepository files, ICurrentClinicResolver clinicResolver)
    {
        _files = files;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IReadOnlyList<ImportedCaptureDto>>> Handle(
        GetImportedCaptureKeysQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var keys = (request.CaptureKeys ?? Array.Empty<string>())
                .Where(PhotoCaptureFacts.IsCaptureKey)
                .Select(key => key.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (keys.Count > CameraImportRefusals.MaxKeysPerRequest)
            {
                return Result<IReadOnlyList<ImportedCaptureDto>>.Failure(
                    CameraImportRefusals.TooManyKeys(), CameraImportRefusals.TooManyKeysCode);
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<IReadOnlyList<ImportedCaptureDto>>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var rows = await _files.GetImportedCapturesAsync(clinic.Value, keys, cancellationToken);

            IReadOnlyList<ImportedCaptureDto> dtos = rows
                .Select(row => new ImportedCaptureDto(row.CaptureKey, row.FileId, row.PatientId, row.PatientName))
                .ToList();

            return Result<IReadOnlyList<ImportedCaptureDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IReadOnlyList<ImportedCaptureDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
