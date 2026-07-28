using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

public class GetAppointmentsQuery : IRequest<Result<IEnumerable<AppointmentDto>>>
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>Optional per-practitioner filter — only appointments assigned to this doctor.</summary>
    public Guid? DoctorId { get; set; }
}

public class GetAppointmentsQueryHandler : IRequestHandler<GetAppointmentsQuery, Result<IEnumerable<AppointmentDto>>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetAppointmentsQueryHandler(
        IAppointmentRepository appointmentRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _appointmentRepository = appointmentRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<AppointmentDto>>> Handle(GetAppointmentsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<AppointmentDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<AppointmentDto>>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            var appointments = await _appointmentRepository.GetByClinicIdAsync(
                clinicId,
                request.StartDate,
                request.EndDate,
                request.DoctorId,
                cancellationToken);

            var dtos = appointments.Select(a => new AppointmentDto
            {
                Id = a.Id,
                ClinicId = a.ClinicId,
                PatientId = a.PatientId,
                PatientName = a.Patient?.GetFullName() ?? "Occupé",
                DoctorId = a.DoctorId,
                DoctorName = a.DoctorName,
                AppointmentDateTime = a.AppointmentDateTime,
                Duration = a.Duration,
                Notes = a.Notes,
                Status = a.Status.ToString(),
                AllowedNextStatuses = Appointment.NextStatusesFrom(a.Status).Select(s => s.ToString()).ToList(),
                ProcedureTypeId = a.ProcedureTypeId,
                ProcedureTypeName = a.ProcedureType?.Name,
                ProcedureColorHex = a.ProcedureColorHex,
                TreatmentPlanItemId = a.TreatmentPlanItemId,
                CreatedAt = a.CreatedAt,
                Version = a.Version,
                IsSyncedToGoogle = a.GoogleCalendarEventId != null
            });

            return Result<IEnumerable<AppointmentDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<AppointmentDto>>.Failure($"Error retrieving appointments: {ex.Message}");
        }
    }
}
