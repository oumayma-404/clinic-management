using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Doctors.Queries;

/// <summary>
/// Returns a dentist's per-practitioner working hours (AC-3.3). Empty list = no per-dentist override
/// (the clinic-wide hours remain the fallback). Clinic-scoped: a doctor from another clinic reads as not found.
/// </summary>
public class GetDoctorWorkingHoursQuery : IRequest<Result<List<WorkingDayDto>>>
{
    public Guid DoctorId { get; set; }
}

public class GetDoctorWorkingHoursQueryHandler : IRequestHandler<GetDoctorWorkingHoursQuery, Result<List<WorkingDayDto>>>
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetDoctorWorkingHoursQueryHandler(
        IDoctorRepository doctorRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _doctorRepository = doctorRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<List<WorkingDayDto>>> Handle(GetDoctorWorkingHoursQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<List<WorkingDayDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
            if (doctor == null || doctor.ClinicId != clinic.Value)
                return Result<List<WorkingDayDto>>.Failure("Praticien introuvable.");

            var hours = WorkingHoursSerializer.Parse(doctor.WorkingHoursJson) ?? new List<WorkingDayDto>();
            return Result<List<WorkingDayDto>>.Success(hours);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<WorkingDayDto>>.Failure($"Erreur lors de la récupération des horaires du praticien : {ex.Message}");
        }
    }
}
