using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

public class NotificationJob
{
    private readonly INotificationRepository _notificationRepository;
    private readonly INotificationService _notificationService;
    private readonly IPatientRepository _patientRepository;
    private readonly ILogger<NotificationJob> _logger;

    public NotificationJob(
        INotificationRepository notificationRepository,
        INotificationService notificationService,
        IPatientRepository patientRepository,
        ILogger<NotificationJob> logger)
    {
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
        _patientRepository = patientRepository;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task ProcessPendingNotifications()
    {
        _logger.LogInformation("Processing pending notifications");

        var pendingNotifications = await _notificationRepository.GetPendingNotificationsAsync();

        foreach (var notification in pendingNotifications)
        {
            try
            {
                var patient = notification.PatientId.HasValue
                    ? await _patientRepository.GetByIdAsync(notification.PatientId.Value)
                    : null;

                if (patient == null)
                {
                    _logger.LogWarning("Patient not found for notification {NotificationId}", notification.Id);
                    notification.MarkAsFailed("Patient not found");
                    await _notificationRepository.UpdateAsync(notification);
                    continue;
                }

                var success = await _notificationService.SendNotificationAsync(
                    patient.Email.Value,
                    patient.PhoneNumber.Value,
                    notification.Type,
                    notification.Subject,
                    notification.Message);

                if (success)
                {
                    notification.MarkAsSent();
                }
                else
                {
                    notification.MarkAsFailed("Failed to send notification");
                }

                await _notificationRepository.UpdateAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification {NotificationId}", notification.Id);
                notification.MarkAsFailed(ex.Message);
                await _notificationRepository.UpdateAsync(notification);
            }
        }

        _logger.LogInformation("Finished processing pending notifications");
    }
}

