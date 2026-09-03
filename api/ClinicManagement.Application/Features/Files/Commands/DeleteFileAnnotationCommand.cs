using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Take a marker off a model (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>A hard delete, and it is the right one here.</b> The record this product refuses to destroy is
/// the clinical one — a fiche, a payment, a file. A marker is a reader's own annotation of a surface: somebody
/// put it there a minute ago, mis-placed, and wants it gone. Keeping a tombstone would put « Repère 3 » in a
/// list nobody can clear, and there is nothing to reconstruct from it.</para>
/// </summary>
public class DeleteFileAnnotationCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
    public Guid AnnotationId { get; set; }
}

public class DeleteFileAnnotationCommandHandler : IRequestHandler<DeleteFileAnnotationCommand, Result<bool>>
{
    private readonly IPatientFileAnnotationRepository _annotations;
    private readonly IPatientFileRepository _files;
    private readonly IPatientRepository _patients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public DeleteFileAnnotationCommandHandler(
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

    public async Task<Result<bool>> Handle(DeleteFileAnnotationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var located = await FileAnnotationAccess.LocateAsync(
                _clinicResolver, _patients, _files, _annotations,
                request.PatientId, request.FileId, request.AnnotationId, cancellationToken);

            if (located.IsFailure)
            {
                return Result<bool>.Failure(located.Error!);
            }

            await _annotations.DeleteAsync(located.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
