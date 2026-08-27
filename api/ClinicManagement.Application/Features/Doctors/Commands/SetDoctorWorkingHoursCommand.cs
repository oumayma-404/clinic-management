using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
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
    private readonly ILogger<SetDoctorWorkingHoursCommandHandler> _logger;

    public SetDoctorWorkingHoursCommandHandler(
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ILogger<SetDoctorWorkingHoursCommandHandler> logger)
    {
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
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

            // AC-P1.23 + AC-P1.26. Two things were wrong here:
            //  1. The payload was round-tripped through Normalize, whose only failure mode was a JsonException
            //     — and the input had just been produced by JsonSerializer.Serialize of a typed list, so it
            //     could never fail. The "validation" was pure ceremony; garbage times persisted.
            //  2. The result was never null-checked (unlike UpdateClinicCommand), so a payload that *did* fail
            //     silently CLEARED the practitioner's override instead of reporting an error.
            // An empty/absent list still means "clear the override" — that is the documented contract (AC-P1.26)
            // — but it is now the only way to clear it.
            string? json = null;
            if (request.WorkingHours is { Count: > 0 })
            {
                var validated = WorkingHoursSerializer.Validate(request.WorkingHours);
                if (validated.IsFailure)
                {
                    return Result<List<WorkingDayDto>>.Failure(validated.Error ?? "Horaires de travail invalides.");
                }

                json = JsonSerializer.Serialize(validated.Value);
            }

            doctor.SetWorkingHours(json);
            _doctorRepository.Update(doctor);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var hours = WorkingHoursSerializer.Parse(doctor.WorkingHoursJson) ?? new List<WorkingDayDto>();
            return Result<List<WorkingDayDto>>.Success(hours);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-10: the raw exception was interpolated into a clinic-facing message.
            _logger.LogError(ex, "Unhandled failure saving working hours for doctor {DoctorId}", request.DoctorId);
            return Result<List<WorkingDayDto>>.Failure("Erreur lors de l'enregistrement des horaires du praticien. Veuillez réessayer.");
        }
    }
}
