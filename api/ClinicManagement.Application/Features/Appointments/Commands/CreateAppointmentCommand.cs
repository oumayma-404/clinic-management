using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

public class CreateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public int DurationMinutes { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public Guid? ProcedureTypeId { get; set; }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public CreateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AppointmentDto>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<AppointmentDto>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<AppointmentDto>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            // If patient is provided, verify it exists
            Patient? patient = null;
            if (request.PatientId.HasValue)
            {
                patient = await _patientRepository.GetByIdAsync(request.PatientId.Value, cancellationToken);
                if (patient == null || patient.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Patient not found");
                }
            }

            // Get procedure type if specified
            Guid? procedureTypeId = request.ProcedureTypeId;
            int? procedureDurationMinutes = null;
            string? procedureColorHex = null;
            string? procedureTypeName = null;

            if (procedureTypeId.HasValue)
            {
                var procedureType = await _procedureTypeRepository.GetByIdAsync(procedureTypeId.Value, cancellationToken);
                if (procedureType == null || procedureType.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Procedure type not found");
                }
                if (!procedureType.IsActive)
                {
                    return Result<AppointmentDto>.Failure("Selected procedure type is not active");
                }
                procedureDurationMinutes = procedureType.DefaultDurationMinutes;
                procedureColorHex = procedureType.Color.Value;
                procedureTypeName = procedureType.Name;
                // Use procedure duration if not specified
                if (request.DurationMinutes == 0)
                {
                    request.DurationMinutes = procedureType.DefaultDurationMinutes;
                }
            }

            var duration = TimeSpan.FromMinutes(request.DurationMinutes);
            var appointment = new Appointment(
                Guid.NewGuid(),
                clinicId,
                request.PatientId,
                request.DoctorId,
                request.AppointmentDateTime,
                duration,
                request.DoctorName,
                request.Notes,
                null, // recurringAppointmentId
                procedureTypeId,
                procedureDurationMinutes,
                procedureColorHex);

            await _appointmentRepository.AddAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Real-time "appointments changed" is broadcast centrally by RealtimeBroadcastBehavior after
            // this command returns success (i.e. after the commit above) — no per-handler broadcast here.

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                ClinicId = appointment.ClinicId,
                PatientId = appointment.PatientId,
                PatientName = patient?.GetFullName() ?? "Occupé",
                DoctorId = appointment.DoctorId,
                DoctorName = appointment.DoctorName,
                AppointmentDateTime = appointment.AppointmentDateTime,
                Duration = appointment.Duration,
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                ProcedureTypeId = appointment.ProcedureTypeId,
                ProcedureTypeName = procedureTypeName,
                ProcedureColorHex = appointment.ProcedureColorHex,
                CreatedAt = appointment.CreatedAt,
                IsSyncedToGoogle = appointment.GoogleCalendarEventId != null
            };

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<AppointmentDto>.Failure($"Error creating appointment: {ex.Message}");
        }
    }
}
