using System.Text.Json;
using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Doctors.Commands;

/// <summary>
/// Sets a dentist's per-practitioner working hours (AC-3.3). A null/empty list clears the override so the
/// clinic-wide hours apply again. Clinic-scoped: a doctor from another clinic reads as not found.
/// </summary>
public class SetDoctorWorkingHoursCommand : IRequest<Result<List<WorkingDayDto>>>
{
    public Guid DoctorId { get; set; }
    public List<WorkingDayDto>? WorkingHours { get; set; }
}

public class SetDoctorWorkingHoursCommandHandler : IRequestHandler<SetDoctorWorkingHoursCommand, Result<List<WorkingDayDto>>>
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public SetDoctorWorkingHoursCommandHandler(
        IDoctorRepository doctorRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _doctorRepository = doctorRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<WorkingDayDto>>> Handle(SetDoctorWorkingHoursCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<List<WorkingDayDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
            if (doctor == null || doctor.ClinicId != clinic.Value)
                return Result<List<WorkingDayDto>>.Failure("Praticien introuvable.");

            // Validate/canonicalize the incoming payload the same way the clinic-wide hours are stored.
            var json = request.WorkingHours is { Count: > 0 }
                ? WorkingHoursSerializer.Normalize(JsonSerializer.Serialize(request.WorkingHours))
                : null;

            doctor.SetWorkingHours(json);
            _doctorRepository.Update(doctor);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var hours = WorkingHoursSerializer.Parse(doctor.WorkingHoursJson) ?? new List<WorkingDayDto>();
            return Result<List<WorkingDayDto>>.Success(hours);
        }
        catch (Exception ex)
        {
            return Result<List<WorkingDayDto>>.Failure($"Erreur lors de l'enregistrement des horaires du praticien : {ex.Message}");
        }
    }
}
