using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Queries;

/// <summary>
/// Every marker on one file (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>Unpaged, and that is the first-class unpaged case rather than an oversight.</b> The viewer draws
/// all of them at once — a marker that existed but was on page two would be a pin missing from the model, which
/// is indistinguishable from one that was never saved. `CreateFileAnnotationCommand.MaxPerFile` is what bounds
/// this read instead.</para>
/// </summary>
public class GetFileAnnotationsQuery : IRequest<Result<List<PatientFileAnnotationDto>>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
}

public class GetFileAnnotationsQueryHandler
    : IRequestHandler<GetFileAnnotationsQuery, Result<List<PatientFileAnnotationDto>>>
{
    private readonly IPatientFileAnnotationRepository _annotations;
    private readonly IPatientFileRepository _files;
    private readonly IPatientRepository _patients;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetFileAnnotationsQueryHandler(
        IPatientFileAnnotationRepository annotations,
        IPatientFileRepository files,
        IPatientRepository patients,
        ICurrentClinicResolver clinicResolver)
    {
        _annotations = annotations;
        _files = files;
        _patients = patients;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<List<PatientFileAnnotationDto>>> Handle(
        GetFileAnnotationsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<PatientFileAnnotationDto>>.Failure(
                    clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<List<PatientFileAnnotationDto>>.Failure("Patient introuvable.");
            }

            var file = await _files.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null || file.PatientId != request.PatientId)
            {
                return Result<List<PatientFileAnnotationDto>>.Failure("Fichier introuvable.");
            }

            var annotations = await _annotations.GetForFileAsync(request.FileId, cancellationToken);

            return Result<List<PatientFileAnnotationDto>>.Success(
                annotations.Select(a => a.ToDto()).ToList());
        }
        catch (Exception ex)
        {
            return Result<List<PatientFileAnnotationDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
