namespace ClinicManagement.Application.DTOs;

/// <summary>Delivery-status values for a reminder outbox row (reliability-and-polish AC-3).</summary>
public static class ReminderDeliveryStatus
{
    public const string Sent = "sent";
    public const string Pending = "pending";
    public const string Failed = "failed";

    /// <summary>
    /// Queued but not sendable — the channel is off, unconfigured or unimplemented (L3a). Its own value rather
    /// than a flavour of <c>pending</c> or <c>failed</c>: a blocked row is not waiting its turn (nothing will
    /// change on its own) and it has not failed (nothing was attempted, and it will send once the channel works).
    /// A silent queue was the whole defect, so the state has to be nameable on screen.
    /// </summary>
    public const string Blocked = "blocked";
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
    /// Whose reminder this was, so the name can be the way to their fiche. Sent alongside
    /// <see cref="PatientName"/> because a name that identifies someone the reader then cannot reach is only
    /// half of « actionable ». Null with the name, when the patient record is gone.
    /// </summary>
    public Guid? PatientId { get; init; }
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
    /// <summary>One of <see cref="ReminderDeliveryStatus"/>: sent / pending / failed / blocked.</summary>
    public required string Status { get; init; }
    /// <summary>
    /// Why it failed — or, for a <see cref="ReminderDeliveryStatus.Blocked"/> row, why it cannot be sent. Both
    /// come off the row's one <c>ErrorMessage</c>, and both are the only thing that makes the row actionable.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// <b>Why</b> a blocked row is blocked, machine-readably — the <c>OutboxBlockReason</c> member's own name
    /// (<c>vendor-whatsapp-messaging-quota</c> AC-4.9). Null on every non-blocked row.
    ///
    /// <para>⚠️ <b>Beside <see cref="FailureReason"/>, not instead of it.</b> That field is the French sentence a
    /// secretary reads; this is what the <i>screen</i> branches on. Without it the log could tell an allowance hold from
    /// a subscription hold only by matching French prose — the <c>Contains("déjà facturée")</c> practice this repo
    /// deleted in <c>adoption-gaps-remediation</c>, where rewording a message silently changes behaviour.</para>
    /// </summary>
    public string? BlockReason { get; init; }

    public required DateTime ScheduledAt { get; init; }
    public DateTime? SentAt { get; init; }
}
