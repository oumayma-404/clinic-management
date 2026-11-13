using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Cameras.Queries;

/// <summary>The cabinet's paired cameras — a handful of rows, never paged.</summary>
public class ListClinicCamerasQuery : IRequest<Result<IReadOnlyList<ClinicCameraDto>>>
{
}

public class ListClinicCamerasQueryHandler : IRequestHandler<ListClinicCamerasQuery, Result<IReadOnlyList<ClinicCameraDto>>>
{
    private readonly IClinicCameraRepository _cameras;
    private readonly ICurrentClinicResolver _clinicResolver;

    public ListClinicCamerasQueryHandler(IClinicCameraRepository cameras, ICurrentClinicResolver clinicResolver)
    {
        _cameras = cameras;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IReadOnlyList<ClinicCameraDto>>> Handle(
        ListClinicCamerasQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<IReadOnlyList<ClinicCameraDto>>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var cameras = await _cameras.ListAsync(clinic.Value, cancellationToken);
            IReadOnlyList<ClinicCameraDto> dtos = cameras.Select(ClinicCameraDto.From).ToList();
            return Result<IReadOnlyList<ClinicCameraDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IReadOnlyList<ClinicCameraDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
