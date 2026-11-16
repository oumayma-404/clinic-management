using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Finds one of a patient's default folders, creating it when it is missing
/// (<c>features/camera-photo-import</c>: a camera photo lands in « Photographie »).
///
/// <para>⚠️ <b>Why not <c>InitializeDefaultFoldersCommand</c>:</b> that creates every missing default folder, so it
/// would bring back « Lettres de Liaisons » for a cabinet that deleted it — on every photo import, silently. The one
/// exception is a patient with no folder at all, who gets the whole set (<see cref="DefaultPatientFolders.EnsureAsync"/>).</para>
///
/// <para>⚠️ Found by id first, then by name, and created under <see cref="DefaultPatientFolders.IdFor"/> — a renamed
/// « Photographie » is still the one photos go to, and two imports reaching the same new patient at once collide on the
/// primary key; the loser re-reads and returns the winner's folder.</para>
/// </summary>
public class EnsureDefaultPatientFolderCommand : IRequest<Result<PatientFolderDto>>
{
    public Guid PatientId { get; set; }
    public string Key { get; set; } = string.Empty;
}

public class EnsureDefaultPatientFolderCommandHandler
    : IRequestHandler<EnsureDefaultPatientFolderCommand, Result<PatientFolderDto>>
{
    private readonly IPatientRepository _patients;
    private readonly IPatientFolderRepository _folders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public EnsureDefaultPatientFolderCommandHandler(
        IPatientRepository patients,
        IPatientFolderRepository folders,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _patients = patients;
        _folders = folders;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFolderDto>> Handle(
        EnsureDefaultPatientFolderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var name = DefaultPatientFolders.NameFor(request.Key);
            if (name is null)
            {
                return Result<PatientFolderDto>.Failure(
                    CameraImportRefusals.FolderKeyUnknown(), CameraImportRefusals.FolderKeyUnknownCode);
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<PatientFolderDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
            {
                return Result<PatientFolderDto>.Failure("Patient introuvable.");
            }

            var folder = await DefaultPatientFolders.EnsureAsync(_folders, _unitOfWork, patient, name, cancellationToken);
            return Result<PatientFolderDto>.Success(DefaultPatientFolders.ToDto(folder));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFolderDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
