using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

public class CreateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid PatientId { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public int DurationMinutes { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGoogleCalendarSyncService _googleCalendarSyncService;

    public CreateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        IGoogleCalendarSyncService googleCalendarSyncService)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _googleCalendarSyncService = googleCalendarSyncService;
    }

    public async Task<Result<AppointmentDto>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Validate required fields
            if (request.PatientId == Guid.Empty)
            {
                return Result<AppointmentDto>.Failure("Patient ID is required");
            }

            if (request.DurationMinutes <= 0)
            {
                return Result<AppointmentDto>.Failure("Duration must be greater than 0");
            }

            // Normalize AppointmentDateTime to UTC
            var appointmentDateTime = request.AppointmentDateTime;
            if (appointmentDateTime.Kind == DateTimeKind.Unspecified)
            {
                appointmentDateTime = DateTime.SpecifyKind(appointmentDateTime, DateTimeKind.Utc);
            }
            else if (appointmentDateTime.Kind == DateTimeKind.Local)
            {
                appointmentDateTime = appointmentDateTime.ToUniversalTime();
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<AppointmentDto>.Failure("Patient not found");
            }

            var duration = TimeSpan.FromMinutes(request.DurationMinutes);

            var appointment = new Appointment(
                Guid.NewGuid(),
                request.PatientId,
                appointmentDateTime,
                duration,
                request.DoctorName,
                request.Notes);

            await _appointmentRepository.AddAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                PatientId = appointment.PatientId,
                PatientName = patient.GetFullName(),
                AppointmentDateTime = appointment.AppointmentDateTime.Kind == DateTimeKind.Utc 
                    ? appointment.AppointmentDateTime 
                    : DateTime.SpecifyKind(appointment.AppointmentDateTime, DateTimeKind.Utc),
                Duration = appointment.Duration,
                DoctorName = appointment.DoctorName,
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                CreatedAt = appointment.CreatedAt.Kind == DateTimeKind.Utc 
                    ? appointment.CreatedAt 
                    : DateTime.SpecifyKind(appointment.CreatedAt, DateTimeKind.Utc)
            };

            // Sync to Google Calendar (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _googleCalendarSyncService.SyncAppointmentToGoogleCalendarAsync(appointment.Id, cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
                {
                    // Silently ignore if Google Calendar is not configured
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the appointment creation
                    // Note: We can't use ILogger here as we're in a background task
                    Console.WriteLine($"Error syncing appointment {appointment.Id} to Google Calendar: {ex.Message}");
                }
            }, cancellationToken);

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<AppointmentDto>.Failure($"Error creating appointment: {ex.Message}");
        }
    }
}


