using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Appointments.Commands;

public class UpdateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid Id { get; set; }
    public DateTime? AppointmentDateTime { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public string? Status { get; set; }
}

public class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGoogleCalendarSyncService _googleCalendarSyncService;

    public UpdateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        IGoogleCalendarSyncService googleCalendarSyncService)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _googleCalendarSyncService = googleCalendarSyncService;
    }

    public async Task<Result<AppointmentDto>> Handle(UpdateAppointmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var appointment = await _appointmentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (appointment == null)
            {
                return Result<AppointmentDto>.Failure("Appointment not found");
            }

            // Update appointment date/time if provided
            if (request.AppointmentDateTime.HasValue)
            {
                var appointmentDateTime = request.AppointmentDateTime.Value;
                if (appointmentDateTime.Kind == DateTimeKind.Unspecified)
                {
                    appointmentDateTime = DateTime.SpecifyKind(appointmentDateTime, DateTimeKind.Utc);
                }
                else if (appointmentDateTime.Kind == DateTimeKind.Local)
                {
                    appointmentDateTime = appointmentDateTime.ToUniversalTime();
                }

                if (appointment.AppointmentDateTime != appointmentDateTime)
                {
                    appointment.Reschedule(appointmentDateTime);
                }
            }

            // Update duration if provided
            if (request.DurationMinutes.HasValue && request.DurationMinutes.Value > 0)
            {
                var newDuration = TimeSpan.FromMinutes(request.DurationMinutes.Value);
                if (appointment.Duration != newDuration)
                {
                    appointment.UpdateDuration(newDuration);
                }
            }

            // Update doctor name if provided
            if (request.DoctorName != null)
            {
                appointment.UpdateDoctorName(request.DoctorName);
            }

            // Update notes if provided
            if (request.Notes != null)
            {
                appointment.UpdateNotes(request.Notes);
            }

            // Update status if provided and different from current
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (Enum.TryParse<AppointmentStatus>(request.Status, true, out var newStatus))
                {
                    // Only update if status is different
                    if (appointment.Status != newStatus)
                    {
                        switch (newStatus)
                        {
                            case AppointmentStatus.Scheduled:
                                // If currently cancelled, we need to reschedule to reactivate
                                if (appointment.Status == AppointmentStatus.Cancelled)
                                {
                                    appointment.Reschedule(appointment.AppointmentDateTime);
                                }
                                // If status is already scheduled, no change needed
                                break;
                            case AppointmentStatus.Confirmed:
                                if (appointment.Status != AppointmentStatus.Confirmed)
                                {
                                    appointment.Confirm();
                                }
                                break;
                            case AppointmentStatus.Completed:
                                if (appointment.Status == AppointmentStatus.InProgress)
                                {
                                    appointment.Complete();
                                }
                                // Note: Can't directly set to Completed from other states
                                break;
                            case AppointmentStatus.Cancelled:
                                if (appointment.Status != AppointmentStatus.Cancelled && 
                                    appointment.Status != AppointmentStatus.Completed)
                                {
                                    appointment.Cancel();
                                }
                                break;
                            case AppointmentStatus.InProgress:
                                if (appointment.Status == AppointmentStatus.Confirmed || 
                                    appointment.Status == AppointmentStatus.Scheduled)
                                {
                                    appointment.Start();
                                }
                                break;
                            case AppointmentStatus.NoShow:
                                if (appointment.Status != AppointmentStatus.Completed && 
                                    appointment.Status != AppointmentStatus.Cancelled)
                                {
                                    appointment.MarkAsNoShow();
                                }
                                break;
                        }
                    }
                }
            }

            await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                PatientId = appointment.PatientId,
                PatientName = appointment.Patient.GetFullName(),
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
                catch
                {
                    // Log error but don't fail the appointment update
                }
            }, cancellationToken);

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<AppointmentDto>.Failure($"Error updating appointment: {ex.Message}");
        }
    }
}

