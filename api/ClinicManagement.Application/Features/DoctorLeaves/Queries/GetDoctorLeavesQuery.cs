using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DoctorLeaves.Queries;

/// <summary>Every congé of one practitioner, earliest first. Readable by every role, like the agenda.</summary>
public class GetDoctorLeavesQuery : IRequest<Result<List<DoctorLeaveDto>>>
{
    public Guid DoctorId { get; set; }
}

public class GetDoctorLeavesQueryHandler : IRequestHandler<GetDoctorLeavesQuery, Result<List<DoctorLeaveDto>>>
{
    private readonly IDoctorLeaveRepository _leaves;
    private readonly IDoctorRepository _doctors;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetDoctorLeavesQueryHandler(
        IDoctorLeaveRepository leaves,
        IDoctorRepository doctors,
        ICurrentClinicResolver clinicResolver)
    {
        _leaves = leaves;
        _doctors = doctors;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<List<DoctorLeaveDto>>> Handle(GetDoctorLeavesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<List<DoctorLeaveDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var doctor = await _doctors.GetByIdAsync(request.DoctorId, cancellationToken);
            if (doctor == null || doctor.ClinicId != clinic.Value)
                return Result<List<DoctorLeaveDto>>.Failure(DoctorLeaveRules.PractitionerNotFound, DoctorLeaveRules.NotFoundCode);

            var leaves = await _leaves.GetByDoctorAsync(clinic.Value, doctor.Id, cancellationToken);
            return Result<List<DoctorLeaveDto>>.Success(leaves.Select(l => l.ToDto(doctor.FullName)).ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<DoctorLeaveDto>>.Failure("Erreur lors de la récupération des congés.", ex);
        }
    }
}
