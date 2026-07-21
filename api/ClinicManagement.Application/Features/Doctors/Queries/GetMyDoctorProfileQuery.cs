using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Doctors.Queries;

/// <summary>Returns the current user's own doctor profile (ordre number + cachet presence) for pre-fill.</summary>
public class GetMyDoctorProfileQuery : IRequest<Result<DoctorProfileDto>>
{
}

public class GetMyDoctorProfileQueryHandler : IRequestHandler<GetMyDoctorProfileQuery, Result<DoctorProfileDto>>
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;

    public GetMyDoctorProfileQueryHandler(IDoctorRepository doctorRepository, IClinicContext clinicContext)
    {
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<DoctorProfileDto>> Handle(GetMyDoctorProfileQuery request, CancellationToken cancellationToken)
    {
        var userId = _clinicContext.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result<DoctorProfileDto>.Failure("Utilisateur non authentifié.");
        }

        var doctor = await _doctorRepository.GetByUserIdAsync(userId, cancellationToken);
        if (doctor == null)
        {
            return Result<DoctorProfileDto>.Failure("Aucun profil praticien n'est associé à votre compte.");
        }

        return Result<DoctorProfileDto>.Success(new DoctorProfileDto
        {
            Id = doctor.Id,
            FullName = doctor.FullName,
            Specialty = doctor.Specialty,
            OrdreNumberCnomdt = doctor.OrdreNumberCnomdt,
            HasCachet = doctor.CachetStorageKey != null,
            CachetContentType = doctor.CachetContentType
        });
    }
}
