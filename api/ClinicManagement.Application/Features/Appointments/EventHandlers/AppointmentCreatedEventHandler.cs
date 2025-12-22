using MediatR;
using ClinicManagement.Domain.Events;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Appointments.EventHandlers;

public class AppointmentCreatedEventHandler : INotificationHandler<AppointmentCreatedEvent>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IPatientRepository _patientRepository;

    public AppointmentCreatedEventHandler(
        INotificationRepository notificationRepository,
        IPatientRepository patientRepository)
    {
        _notificationRepository = notificationRepository;
        _patientRepository = patientRepository;
    }

    public async Task Handle(AppointmentCreatedEvent notification, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByIdAsync(notification.PatientId, cancellationToken);
        if (patient == null)
            return;

        // Create reminder notification for 24 hours before appointment
        var reminderTime = notification.AppointmentDateTime.AddHours(-24);
        if (reminderTime > DateTime.UtcNow)
        {
            var reminderNotification = new Notification(
                Guid.NewGuid(),
                NotificationType.Both,
                "Appointment Reminder",
                $"You have an appointment scheduled for {notification.AppointmentDateTime:yyyy-MM-dd HH:mm}",
                reminderTime,
                notification.AppointmentId,
                notification.PatientId);

            await _notificationRepository.AddAsync(reminderNotification, cancellationToken);
        }
    }
}

