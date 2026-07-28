namespace ClinicManagement.Application.DTOs;

/// <summary>Delivery-status values for a reminder outbox row (reliability-and-polish AC-3).</summary>
public static class ReminderDeliveryStatus
{
    public const string Sent = "sent";
    public const string Pending = "pending";
    public const string Failed = "failed";
}

/// <summary>
/// One recent reminder outbox row as shown on the admin delivery-status surface (AC-3): its channel, the
/// masked recipient, current state (+ failure reason), and the scheduled / sent timestamps. Read-only over
/// existing <c>Notification</c> rows — no schema change.
/// </summary>
public sealed record ReminderStatusDto
{
    public required Guid Id { get; init; }
    /// <summary>Channel name: "SMS" or "WhatsApp".</summary>
    public required string Channel { get; init; }
    /// <summary>The patient phone, masked (PII) — only the last digits are shown.</summary>
    public required string RecipientMasked { get; init; }
    /// <summary>
    /// The patient's name (AC-P3.9). Without it the row read « •••• 56 — Échec », which names nobody and so
    /// could not be acted on; the masked phone alone identifies the recipient to no-one. Null when the patient
    /// record is gone. Adding the name is what makes the row actionable — the phone stays masked (AC-P3.10).
    /// </summary>
    public string? PatientName { get; init; }
    /// <summary>
    /// The appointment this reminder is for (AC-P3.9), so a failed row says *which* visit is at risk. Null for
    /// a recall (« relance »), which carries no appointment.
    /// </summary>
    public DateTime? AppointmentAt { get; init; }
    /// <summary>
    /// True when the row is a recall rather than a booking reminder — derived from the absence of an
    /// appointment, the same discriminator the outbox and the dispatcher use.
    /// </summary>
    public required bool IsRecall { get; init; }
    /// <summary>One of <see cref="ReminderDeliveryStatus"/>: sent / pending / failed.</summary>
    public required string Status { get; init; }
    public string? FailureReason { get; init; }
    public required DateTime ScheduledAt { get; init; }
    public DateTime? SentAt { get; init; }
}
