namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of one outbound document email. <see cref="Sent"/> and <see cref="Failed"/> are terminal;
/// <see cref="Queued"/> is what the dispatcher scans for.
///
/// <para><b><see cref="Blocked"/> is non-terminal, and it exists for the same reason
/// <c>NotificationStatus.Blocked</c> does</b> (review finding 5). A row whose clinic has no usable SMTP settings was
/// left <see cref="Queued"/> and returned to the queue <i>without consuming an attempt</i> — individually correct,
/// since restoring the settings should resume it — but the dispatch scan is « queued, oldest first, take 20 », so
/// unsendable rows accumulate at the <b>front</b> and, past the batch size, consume every minutely tick for ever.
/// One clinic that never configures SMTP then stops « Envoyer par email » for <i>every</i> clinic while the job logs
/// a clean run. Blocked keeps the row and its reason while taking it out of the scan, and
/// <c>DocumentEmailJob.ReviewBlockedRowsAsync</c> returns it the moment the channel is sendable again — so it is not
/// a one-way door.</para>
/// </summary>
public enum DocumentEmailStatus
{
    Queued = 1,
    Sent = 2,
    Failed = 3,

    /// <summary>
    /// Parked: this clinic cannot send today (SMTP unconfigured or removed after queueing). Survives with its
    /// reason, is invisible to the dispatch scan, and is returned to <see cref="Queued"/> by the review pass.
    /// </summary>
    Blocked = 4
}
