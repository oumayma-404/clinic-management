using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Records that a clinic PC kept a camera photo's full-size original (<c>features/camera-photo-import</c>, storage
/// « deux maisons »): its SHA-256, its size and the PC's name. The bytes never reach the server — on a hosted
/// cabinet the file's own bytes are a 2 560 px copy, and this is what lets the screen say « Original : PC ACCUEIL ».
///
/// <para>⚠️ <b>Sent by the page on the Windows app's behalf</b>: the page owns the session, and the shell's only
/// credential is refused outside <c>[AcceptsScopedToken]</c>. A re-report after a restart is a no-op.</para>
/// </summary>
public class RecordFileOriginalCommand : IRequest<Result<PatientFileDto>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string HeldBy { get; set; } = string.Empty;
}

public class RecordFileOriginalCommandHandler : IRequestHandler<RecordFileOriginalCommand, Result<PatientFileDto>>
{
    private readonly IPatientFileRepository _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public RecordFileOriginalCommandHandler(
        IPatientFileRepository files,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _files = files;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFileDto>> Handle(RecordFileOriginalCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<PatientFileDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var file = await _files.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null || file.ClinicId != clinic.Value || file.PatientId != request.PatientId)
            {
                return Result<PatientFileDto>.Failure("Fichier introuvable.");
            }

            if (file.Origin != FileOrigin.Camera)
            {
                return Result<PatientFileDto>.Failure(
                    CameraImportRefusals.OriginalNotCamera(), CameraImportRefusals.OriginalNotCameraCode);
            }

            var holder = request.HeldBy?.Trim() ?? string.Empty;
            if (!PhotoCaptureFacts.IsCaptureKey(request.Sha256)
                || request.SizeBytes <= 0
                || holder.Length == 0
                || holder.Length > PatientFile.MaxOriginalHolderLength)
            {
                return Result<PatientFileDto>.Failure(
                    CameraImportRefusals.OriginalInvalid(), CameraImportRefusals.OriginalInvalidCode);
            }

            if (file.RecordOriginal(request.Sha256, request.SizeBytes, holder, DateTime.UtcNow))
            {
                await _files.UpdateAsync(file, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<PatientFileDto>.Success(file.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFileDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
