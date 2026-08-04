namespace ClinicManagement.Domain.Enums;

public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,

    /// <summary>
    /// Enqueued, but <b>not sendable</b> for a reason no retry can change on its own: the channel was switched
    /// off after the row was created, its credentials are missing, or nothing implements it at all.
    ///
    /// <para><b>Non-terminal, and that is the point.</b> Before this status existed such a row was deliberately
    /// left <see cref="Pending"/> — "so it sends once the operator configures the channel" — while the purge
    /// deliberately never deleted a <c>Pending</c> row. Both intentions were right on their own and together
    /// they starved the queue: unsendable rows sort to the <i>front</i> of the due scan (oldest first) and, past
    /// the batch size, consumed every tick for ever. <c>Blocked</c> keeps both — the row survives and records
    /// why (<c>ErrorMessage</c>) — while leaving the dispatch scan, which reads <c>Pending</c> only.</para>
    ///
    /// <para>Never purged (retention drops <see cref="Sent"/>/<see cref="Failed"/> only), and returned to
    /// <c>Pending</c> by <c>Notification.Unblock()</c> once the channel becomes sendable again.</para>
    /// </summary>
    Blocked = 4
}



