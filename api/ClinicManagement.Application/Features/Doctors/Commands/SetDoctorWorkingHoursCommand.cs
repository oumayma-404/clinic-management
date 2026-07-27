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
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetDoctorWorkingHoursCommandHandler(
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork)
    {
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<WorkingDayDto>>> Handle(SetDoctorWorkingHoursCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Result<List<WorkingDayDto>>.Failure("Session invalide, veuillez vous reconnecter.");

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
                return Result<List<WorkingDayDto>>.Failure("Utilisateur introuvable.");

            var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
            // Cross-clinic (or missing) targets read as "not found" — no existence disclosure.
            if (doctor == null || doctor.ClinicId != user.ClinicId)
                return Result<List<WorkingDayDto>>.Failure("Praticien introuvable.");

            // Own-or-admin, checked BEFORE any mutation. Previously this verified same-clinic only, so any
            // staff member — including another doctor or a secretary — could rewrite a practitioner's
            // availability and have patients booked outside it (audit § 2, finding 9). Copied from the sibling
            // UpdateDoctorProfileCommand, which already got this right.
            var isOwnRecord = doctor.UserId != null && doctor.UserId == user.Id;
            if (!user.IsAdmin() && !isOwnRecord)
                return Result<List<WorkingDayDto>>.Failure("Vous ne pouvez modifier que votre propre profil.");

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
