using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Notification : Entity<Guid>
{
    /// <summary>
    /// The clinic that owns this reminder, set by the scheduler at enqueue so the dispatcher can resolve
    /// that clinic's channel credentials at send time. Nullable: legacy/global rows enqueued before per-clinic
    /// settings existed keep it null and fall back to the per-install config.
    /// </summary>
    public Guid? ClinicId { get; private set; }
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

    /// <summary>
    /// Why this row is parked, machine-readably — the counterpart to <see cref="ErrorMessage"/>'s French sentence
    /// (<c>clinic-subscription</c> FR-8). It exists so the un-park review can interrogate the <i>reason</i> rather
    /// than the prose: a row parked because the cabinet's entitlement lapsed otherwise passes all three of the
    /// channel checks and is released on the next tick.
    ///
    /// <para>⚠️ <b>Written by nothing yet, and that is deliberate.</b> The column lands with Part A's migration
    /// because the model and the schema have to agree in one step — a column the snapshot does not know about
    /// makes every later migration re-add it. <c>MarkAsBlocked</c> starts writing it in Part G, together with the
    /// review term that reads it; shipping the parking without the un-park releases every parked reminder within
    /// a minute on a cabinet that has not paid.</para>
    /// </summary>
    public OutboxBlockReason? BlockedReason { get; private set; }

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
        Guid? patientId = null,
        Guid? clinicId = null)
    {
        Id = id;
        Type = type;
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        ScheduledFor = scheduledFor;
        AppointmentId = appointmentId;
        PatientId = patientId;
        ClinicId = clinicId;
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

    /// <summary>
    /// Parks the row: it cannot be sent for a reason a retry cannot change (the channel was disabled after
    /// enqueue, its credentials are missing, or no sender implements it), so it leaves the dispatch scan
    /// instead of occupying the front of it for ever. <b>Not terminal</b> — retention never deletes it and
    /// <see cref="Unblock"/> puts it back.
    ///
    /// <para><paramref name="reason"/> is the French sentence the « Rappels » page shows beside the row: the
    /// whole defect this status fixes is that a starved queue said nothing at all. The retry count is
    /// deliberately <b>not</b> incremented — no attempt was made, and consuming the budget here would let a
    /// misconfiguration silently spend a reminder's retries.</para>
    /// </summary>
    public void MarkAsBlocked(string reason)
    {
        Status = NotificationStatus.Blocked;
        ErrorMessage = reason;
    }

    /// <summary>
    /// Returns a <see cref="NotificationStatus.Blocked"/> row to the dispatch queue, clearing the reason it
    /// carried. Called when the channel becomes sendable again — the row was kept precisely so that this could
    /// happen rather than the patient silently never being contacted.
    /// </summary>
    public bool Unblock()
    {
        if (Status != NotificationStatus.Blocked)
        {
            return false;
        }

        Status = NotificationStatus.Pending;
        ErrorMessage = null;
        return true;
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



