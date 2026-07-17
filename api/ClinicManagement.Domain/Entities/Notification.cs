using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Notification : Entity<Guid>
{
    public Guid? AppointmentId { get; private set; }
    public Guid? PatientId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Subject { get; private set; }
    public string Message { get; private set; }
    public NotificationStatus Status { get; private set; }
    public DateTime ScheduledFor { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation properties
    public Appointment? Appointment { get; private set; }
    public Patient? Patient { get; private set; }

    private Notification() { } // For EF Core

    public Notification(
        Guid id,
        NotificationType type,
        string subject,
        string message,
        DateTime scheduledFor,
        Guid? appointmentId = null,
        Guid? patientId = null)
    {
        Id = id;
        Type = type;
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        ScheduledFor = scheduledFor;
        AppointmentId = appointmentId;
        PatientId = patientId;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsSent()
    {
        Status = NotificationStatus.Sent;
        SentAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string? errorMessage = null)
    {
        Status = NotificationStatus.Failed;
        ErrorMessage = errorMessage;
        RetryCount++;
    }

    /// <summary>
    /// Records a transient send failure: increments <see cref="RetryCount"/> and stores the error, but
    /// keeps the notification <see cref="NotificationStatus.Pending"/> so a later dispatch tick retries it —
    /// only crossing to <see cref="NotificationStatus.Failed"/> once the attempt count reaches
    /// <paramref name="maxRetries"/>. Distinct from <see cref="MarkAsFailed"/> (a terminal, no-retry failure).
    /// </summary>
    public void RecordFailedAttempt(string? errorMessage, int maxRetries)
    {
        RetryCount++;
        ErrorMessage = errorMessage;
        if (RetryCount >= maxRetries)
        {
            Status = NotificationStatus.Failed;
        }
    }

    public void Retry()
    {
        if (Status == NotificationStatus.Failed)
        {
            Status = NotificationStatus.Pending;
            ErrorMessage = null;
        }
    }
}



