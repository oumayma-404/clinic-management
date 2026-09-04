using System.Text.Json.Serialization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Give a marker its name (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>Only the label moves; the point never does.</b> A marker that could be renamed *and* relocated by
/// one call would let a stale viewer put somebody else's pin somewhere new, and there is no gesture in the
/// product that moves one — you delete it and drop another. Leaving the coordinates off the command is what
/// makes that impossible rather than merely unused.</para>
/// </summary>
public class RenameFileAnnotationCommand : IRequest<Result<PatientFileAnnotationDto>>
{
    [JsonIgnore]
    public Guid PatientId { get; set; }

    [JsonIgnore]
    public Guid FileId { get; set; }

    [JsonIgnore]
    public Guid AnnotationId { get; set; }

    public string Label { get; set; } = string.Empty;
}

public class RenameFileAnnotationCommandHandler
    : IRequestHandler<RenameFileAnnotationCommand, Result<PatientFileAnnotationDto>>
{
    private readonly IPatientFileAnnotationRepository _annotations;
    private readonly IPatientFileRepository _files;
    private readonly IPatientRepository _patients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public RenameFileAnnotationCommandHandler(
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
        RenameFileAnnotationCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var located = await FileAnnotationAccess.LocateAsync(
                _clinicResolver, _patients, _files, _annotations,
                request.PatientId, request.FileId, request.AnnotationId, cancellationToken);

            if (located.IsFailure)
            {
                return Result<PatientFileAnnotationDto>.Failure(located.Error!);
            }

            var annotation = located.Value!;
            annotation.Rename(request.Label, DateTime.UtcNow);

            await _annotations.UpdateAsync(annotation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PatientFileAnnotationDto>.Success(annotation.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFileAnnotationDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
