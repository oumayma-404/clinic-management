using System.Text.Json.Serialization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Drop a marker on the surface of a 3D model (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>Three commands rather than one « replace the whole set », and that is a data-loss decision, not a
/// style one.</b> Replacing the set is much less code and the viewer edits locally, so it was the obvious shape
/// — but two people looking at the same model would then silently overwrite each other's markers, with the last
/// save winning and nothing anywhere to say a marker had ever existed. Per-marker writes merge on their own:
/// two dentists adding pins both keep them, and the only thing either can lose is the label of the one marker
/// they were both renaming.</para>
/// </summary>
public class CreateFileAnnotationCommand : IRequest<Result<PatientFileAnnotationDto>>
{
    [JsonIgnore]
    public Guid PatientId { get; set; }

    [JsonIgnore]
    public Guid FileId { get; set; }

    /// <summary>Who is dropping it — from the token, never from the body.</summary>
    [JsonIgnore]
    public string? CreatedBy { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }

    public string Label { get; set; } = string.Empty;
}

public class CreateFileAnnotationCommandHandler
    : IRequestHandler<CreateFileAnnotationCommand, Result<PatientFileAnnotationDto>>
{
    /// <summary>
    /// ⚠️ A ceiling, because nothing else bounds this table. A model carries a handful of markers; a script, or
    /// a stuck finger on a touch screen, carries as many as it likes. It refuses the two-hundredth rather than
    /// letting one file grow a table nobody would think to look at.
    /// </summary>
    public const int MaxPerFile = 200;

    private readonly IPatientFileAnnotationRepository _annotations;
    private readonly IPatientFileRepository _files;
    private readonly IPatientRepository _patients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public CreateFileAnnotationCommandHandler(
        IPatientFileAnnotationRepository annotations,
        IPatientFileRepository files,
        IPatientRepository patients,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _annotations = annotations;
        _files = files;
        _patients = patients;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFileAnnotationDto>> Handle(
        CreateFileAnnotationCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientFileAnnotationDto>.Failure(
                    clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientFileAnnotationDto>.Failure("Patient introuvable.");
            }

            var file = await _files.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null || file.PatientId != request.PatientId)
            {
                return Result<PatientFileAnnotationDto>.Failure("Fichier introuvable.");
            }

            var existing = await _annotations.GetForFileAsync(request.FileId, cancellationToken);
            if (existing.Count >= MaxPerFile)
            {
                return Result<PatientFileAnnotationDto>.Failure(
                    $"Ce fichier porte déjà {MaxPerFile} repères. Supprimez-en un avant d'en ajouter un autre.");
            }

            var annotation = new PatientFileAnnotation(
                Guid.NewGuid(),
                file.Id,
                // From the FILE, not from the resolver: the file is the row whose clinic this marker belongs to,
                // and the two are already known to agree because the file was read through the tenant filter.
                file.ClinicId,
                request.X,
                request.Y,
                request.Z,
                request.NormalX,
                request.NormalY,
                request.NormalZ,
                request.Label,
                // An audit instant, not a clinic-local day: `ClinicClock` exists for the boundary arithmetic that
                // money and agenda reads do, and there is none here.
                DateTime.UtcNow,
                request.CreatedBy);

            await _annotations.AddAsync(annotation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PatientFileAnnotationDto>.Success(annotation.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFileAnnotationDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
