using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// « Changer de patient » — re-files a document under another patient of the same cabinet
/// (<c>features/camera-photo-import</c>). It is how a photo filed to the wrong person is corrected: deleting is
/// doctor/admin only and loses the capture facts, so without this a secretary had no way to fix her own mistake.
///
/// <para>⚠️ <b>A file in a default folder lands in the target's folder of the same name</b> (created if missing), any
/// other at the target's root — a folder belongs to one patient, so keeping the old <c>FolderId</c> would point the
/// file into somebody else's tree.</para>
/// </summary>
public class MovePatientFileToPatientCommand : IRequest<Result<PatientFileDto>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
    public Guid TargetPatientId { get; set; }

    /// <summary>The version the form read; a concurrent rename makes this a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }
}

public static class FileMoveRefusals
{
    public const string SamePatientCode = "file-move-same-patient";
    public const string TargetArchivedCode = "file-move-target-archived";
    public const string VaultCode = "file-move-vault";
    public const string LinkedDocumentCode = "file-move-linked-document";

    public static string SamePatient() => "Ce fichier est déjà dans le dossier de ce patient.";

    public static string TargetArchived() => "Ce patient est archivé : réactivez-le avant d'y ranger un fichier.";

    public static string Vault() =>
        "Un fichier conservé au cabinet ne peut pas changer de patient : son original est rangé sous ce patient.";

    public static string LinkedDocument() =>
        "Un document du dossier s'appuie sur ce fichier : il ne peut pas changer de patient.";
}

public class MovePatientFileToPatientCommandHandler
    : IRequestHandler<MovePatientFileToPatientCommand, Result<PatientFileDto>>
{
    private readonly IPatientRepository _patients;
    private readonly IPatientFileRepository _files;
    private readonly IPatientFolderRepository _folders;
    private readonly IMedicalDocumentRepository _documents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public MovePatientFileToPatientCommandHandler(
        IPatientRepository patients,
        IPatientFileRepository files,
        IPatientFolderRepository folders,
        IMedicalDocumentRepository documents,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _patients = patients;
        _files = files;
        _folders = folders;
        _documents = documents;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFileDto>> Handle(
        MovePatientFileToPatientCommand request, CancellationToken cancellationToken)
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

            if (request.TargetPatientId == file.PatientId)
            {
                return Result<PatientFileDto>.Failure(FileMoveRefusals.SamePatient(), FileMoveRefusals.SamePatientCode);
            }

            // A coffre original's path is derived from its patient (VaultPath), so moving the row would orphan it.
            if (file.Residency == FileResidency.Vault)
            {
                return Result<PatientFileDto>.Failure(FileMoveRefusals.Vault(), FileMoveRefusals.VaultCode);
            }

            var target = await _patients.GetByIdAsync(request.TargetPatientId, cancellationToken);
            if (target == null || target.ClinicId != clinic.Value)
            {
                return Result<PatientFileDto>.Failure("Patient introuvable.");
            }

            if (target.IsArchived)
            {
                return Result<PatientFileDto>.Failure(
                    FileMoveRefusals.TargetArchived(), FileMoveRefusals.TargetArchivedCode);
            }

            if (await _documents.AnyForFileAsync(clinic.Value, file.Id, cancellationToken))
            {
                return Result<PatientFileDto>.Failure(
                    FileMoveRefusals.LinkedDocument(), FileMoveRefusals.LinkedDocumentCode);
            }

            _unitOfWork.SetExpectedVersion(file, request.Version);

            var folderId = await TargetFolderAsync(file, target, cancellationToken);
            file.MoveToPatient(target.Id, folderId);

            await _files.UpdateAsync(file, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PatientFileDto>.Success(file.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFileDto>.Failure(ErrorMessages.Generic, ex);
        }
    }

    /// <summary>The target's folder of the same default name, created in this same save when missing; else its root.</summary>
    private async Task<Guid?> TargetFolderAsync(PatientFile file, Patient target, CancellationToken cancellationToken)
    {
        var sourceName = file.Folder?.Name;
        if (!DefaultPatientFolders.IsDefaultName(sourceName))
        {
            return null;
        }

        var roots = await _folders.GetRootFoldersByPatientIdAsync(target.Id, cancellationToken);
        var match = roots.FirstOrDefault(folder => string.Equals(folder.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match.Id;
        }

        var name = DefaultPatientFolders.All.First(folder =>
            string.Equals(folder.Name, sourceName, StringComparison.OrdinalIgnoreCase)).Name;
        var created = new PatientFolder(DefaultPatientFolders.IdFor(target.Id, name), target.Id, target.ClinicId, name);
        await _folders.AddAsync(created, cancellationToken);
        return created.Id;
    }
}
