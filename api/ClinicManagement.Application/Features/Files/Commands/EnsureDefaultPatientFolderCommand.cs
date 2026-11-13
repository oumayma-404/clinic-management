using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Finds one of a patient's default folders, creating it — and only it — when it is missing
/// (<c>features/camera-photo-import</c>: a camera photo lands in « Photographie »).
///
/// <para>⚠️ <b>Why not <c>InitializeDefaultFoldersCommand</c>:</b> that creates every missing default folder, so it
/// would bring back « Lettres de Liaisons » for a cabinet that deleted it — on every photo import, silently.</para>
///
/// <para>⚠️ The folder's id is <see cref="DefaultPatientFolders.IdFor"/>, so two imports reaching the same new
/// patient at once collide on the primary key; the loser re-reads and returns the winner's folder.</para>
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

            var existing = await FindRootFolderAsync(request.PatientId, name, cancellationToken);
            if (existing is not null)
            {
                return Result<PatientFolderDto>.Success(DefaultPatientFolders.ToDto(existing));
            }

            var folder = new PatientFolder(
                DefaultPatientFolders.IdFor(request.PatientId, name), request.PatientId, patient.ClinicId, name);

            try
            {
                await _folders.AddAsync(folder, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
            {
                // Another request created it a moment ago under the same deterministic id.
                _unitOfWork.StopTracking(folder);
                var winner = await FindRootFolderAsync(request.PatientId, name, cancellationToken);
                if (winner is not null)
                {
                    return Result<PatientFolderDto>.Success(DefaultPatientFolders.ToDto(winner));
                }

                throw;
            }

            return Result<PatientFolderDto>.Success(DefaultPatientFolders.ToDto(folder));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFolderDto>.Failure(ErrorMessages.Generic, ex);
        }
    }

    private async Task<PatientFolder?> FindRootFolderAsync(Guid patientId, string name, CancellationToken cancellationToken)
    {
        var roots = await _folders.GetRootFoldersByPatientIdAsync(patientId, cancellationToken);
        return roots.FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
