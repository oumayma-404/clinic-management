using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One queued OS push: this event, to this device. The outbox <c>PushDispatchJob</c> drains.
///
/// <para><b>What it deliberately does not hold is a message.</b> There is a <see cref="Category"/>, a
/// <see cref="Label"/> that is a <i>fixed</i> French phrase for that category, and routing ids — and no patient
/// name, act, tooth, amount or free text of any kind (AC-47). A lock screen is read by whoever is holding the
/// phone, so what reaches it is « Nouveau rendez-vous » and nothing more. The rendered body a staff member sees
/// stays in <see cref="StaffNotification"/>, behind the app's own authentication, and the row here is the
/// doorbell for it.</para>
///
/// <para><see cref="RecipientUserId"/> is stored rather than read off the device at send time, and that is what
/// makes the dispatch-time re-check possible: a shared tablet's token can be rebound to a colleague between
/// enqueue and dispatch (AC-41), and delivering this row to whoever holds the device *now* would push one user's
/// notifications to another. A mismatch fails the row instead.</para>
/// </summary>
public class PushDelivery : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }

    /// <summary>The device this send is addressed to.</summary>
    public Guid DeviceRegistrationId { get; private set; }

    /// <summary>Who this was queued for. Compared against the device's current binding at dispatch.</summary>
    public string RecipientUserId { get; private set; } = string.Empty;

    public NotificationCategory Category { get; private set; }

    /// <summary>The fixed French category phrase — the whole of what a lock screen may show.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The record the tap opens (AC-48). An opaque routing key, never content.</summary>
    public Guid? AppointmentId { get; private set; }

    /// <summary>
    /// Earliest moment this may be sent — how the clinic-local quiet-hours floor is applied (AC-46).
    ///
    /// <para>Set <b>once, at enqueue</b>, deliberately: re-testing the clock on every scan would mean a row's
    /// send time depended on when the job happened to look at it, so an outage over 21:00 would release a
    /// backlog of banners at 03:00 — the exact thing quiet hours exist to prevent.</para>
    /// </summary>
    public DateTime SendNotBefore { get; private set; }

    public PushDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Why this row is parked, machine-readably — <see cref="FailureReason"/>'s French sentence is what an operator
    /// reads, this is what the un-park review interrogates (<c>clinic-subscription</c> FR-8). Same shape and same
    /// reason as <c>Notification.BlockedReason</c>.
    /// </summary>
    public OutboxBlockReason? BlockedReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private PushDelivery() { } // For EF Core

    public static PushDelivery Create(
        Guid clinicId,
        Guid deviceRegistrationId,
        string recipientUserId,
        NotificationCategory category,
        string label,
        Guid? appointmentId,
        DateTime sendNotBeforeUtc,
        DateTime nowUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        if (string.IsNullOrWhiteSpace(recipientUserId))
        {
            throw new ArgumentException("Le destinataire est obligatoire.", nameof(recipientUserId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Le libellé est obligatoire.", nameof(label));
        }

        return new PushDelivery
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            DeviceRegistrationId = deviceRegistrationId,
            RecipientUserId = recipientUserId,
            Category = category,
            Label = label,
            AppointmentId = appointmentId,
            SendNotBefore = sendNotBeforeUtc,
            Status = PushDeliveryStatus.Pending,
            CreatedAt = nowUtc
        };
    }

    public void MarkAsSent(DateTime nowUtc)
    {
        Status = PushDeliveryStatus.Sent;
        AttemptCount++;
        FailureReason = null;
        SentAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    /// <summary>Terminal failure — nothing will be retried.</summary>
    public void MarkAsFailed(string? reason, DateTime nowUtc)
    {
        Status = PushDeliveryStatus.Failed;
        AttemptCount++;
        FailureReason = reason;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// A transient failure: stays <see cref="PushDeliveryStatus.Pending"/> and crosses to
    /// <see cref="PushDeliveryStatus.Failed"/> only once <paramref name="maxAttempts"/> is reached.
    /// </summary>
    public void RecordFailedAttempt(string? reason, int maxAttempts, DateTime nowUtc)
    {
        AttemptCount++;
        FailureReason = reason;
        UpdatedAt = nowUtc;

        if (AttemptCount >= maxAttempts)
        {
            Status = PushDeliveryStatus.Failed;
        }
    }

    /// <summary>
    /// Parks a row that cannot be sent for a reason no retry changes, recording the French sentence an operator
    /// reads (AC-50) and the machine-readable <paramref name="reason"/> the un-park review interrogates (FR-8).
    /// Non-terminal: never purged, and <see cref="Unblock"/> returns it once the platform is sendable.
    /// </summary>
    public void MarkAsBlocked(OutboxBlockReason reason, string sentence, DateTime nowUtc)
    {
        Status = PushDeliveryStatus.Blocked;
        BlockedReason = reason;
        FailureReason = sentence;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Returns a parked row to the queue. Returns false when it was not parked, so the reviewer can skip the
    /// write — the same shape as <c>Notification.Unblock</c>, and for the same reason: without this the status
    /// would be a one-way door and the operator who finally supplies the credentials would see nothing arrive.
    /// </summary>
    public bool Unblock(DateTime nowUtc)
    {
        if (Status != PushDeliveryStatus.Blocked)
        {
            return false;
        }

        Status = PushDeliveryStatus.Pending;
        BlockedReason = null;
        FailureReason = null;
        UpdatedAt = nowUtc;
        return true;
    }
}
