namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of one queued OS push send. Mirrors <see cref="NotificationStatus"/> on purpose — including its
/// non-terminal <see cref="Blocked"/> — because this outbox is drained by the same oldest-first, batch-capped
/// scan and would starve in exactly the same way.
/// </summary>
public enum PushDeliveryStatus
{
    /// <summary>Queued and waiting for its <c>SendNotBefore</c> to pass.</summary>
    Pending = 1,

    /// <summary>Accepted by FCM/APNs. Terminal.</summary>
    Sent = 2,

    /// <summary>Given up on. Terminal.</summary>
    Failed = 3,

    /// <summary>
    /// Queued but <b>not sendable</b> for a reason no retry can change on its own: this deployment cannot push
    /// to the row's platform, or its credentials are absent.
    ///
    /// <para><b>Non-terminal, and that is the point (AC-50).</b> The lesson is `adoption-qa-l` L3's, arriving
    /// here before the defect does: a row left <see cref="Pending"/> « so it sends once the operator configures
    /// the channel » sorts to the <i>front</i> of an oldest-first, <c>.Take(N)</c> scan and, past the batch size,
    /// consumes every tick for ever. <c>Blocked</c> keeps the row and its reason while leaving the scan, and
    /// <c>PushDelivery.Unblock()</c> returns it once the platform becomes sendable.</para>
    ///
    /// <para>Never purged — retention drops <see cref="Sent"/>/<see cref="Failed"/> only.</para>
    /// </summary>
    Blocked = 4
}
