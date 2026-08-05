namespace ClinicManagement.Application.DTOs;

/// <summary>
/// How deep the clinic's three background queues are (multi-tenant-cloud US-6, <c>GET /api/outbox</c>).
///
/// <para><b>Three named sections rather than one uniform row per queue</b>, because the queues genuinely differ:
/// only reminders can be <c>Blocked</c>, only the e-invoice outbox backs off between attempts, and a document
/// email has no scheduled instant at all. A common shape would mean zeros standing in for concepts that do not
/// exist, and « Blocked 0 » about a queue with no blocked state is not a true statement — it is a field the
/// operator has to know to ignore.</para>
/// </summary>
public class OutboxDepthDto
{
    public required ReminderOutboxDepthDto Reminders { get; init; }
    public required EInvoiceOutboxDepthDto EInvoices { get; init; }
    public required DocumentEmailOutboxDepthDto DocumentEmails { get; init; }

    /// <summary>
    /// The instant every « due » figure below was measured against — the same one the dispatchers scan with.
    /// Present so a reading can be compared with the one taken five minutes ago, which is what turns a depth
    /// into « draining » or « stuck ».
    /// </summary>
    public DateTime MeasuredAtUtc { get; init; }
}

/// <summary>
/// The reminder outbox. <see cref="Due"/> is the figure to watch: a <see cref="Pending"/> row scheduled for next
/// week is not a backlog, and only a row whose send time has passed says the dispatcher is behind.
/// </summary>
public class ReminderOutboxDepthDto
{
    public int Pending { get; init; }

    /// <summary>Pending <b>and</b> due — what the next dispatch tick would take.</summary>
    public int Due { get; init; }

    /// <summary>
    /// Enqueued but not sendable (the channel is off or unconfigured). Non-terminal: it returns to the queue
    /// once the channel can send. A number here is an operator action, not a failure.
    /// </summary>
    public int Blocked { get; init; }

    /// <summary>Terminally failed within <see cref="FailedSinceUtc"/>.</summary>
    public int FailedRecent { get; init; }

    /// <summary>
    /// The start of the window <see cref="FailedRecent"/> covers. Carried on the DTO so the figure describes its
    /// own scope — a bare « 4 failed » cannot say whether that is today or since the install.
    /// </summary>
    public DateTime FailedSinceUtc { get; init; }

    /// <summary>
    /// When the oldest due row was supposed to go out, or null if nothing is due. <b>This is the reading that
    /// distinguishes a queue from a stoppage</b>: minutes old is a queue draining, hours old is a job not running.
    /// </summary>
    public DateTime? OldestDueScheduledForUtc { get; init; }
}

/// <summary>The El Fatoora e-invoice outbox. A rejected note backs off, so Queued &gt; Due is normal.</summary>
public class EInvoiceOutboxDepthDto
{
    public int Queued { get; init; }
    public int Due { get; init; }
    public int Failed { get; init; }
    public DateTime? OldestDueNextAttemptUtc { get; init; }
}

/// <summary>
/// The document-email outbox. No « due » figure — a queued row has no scheduled instant, so every one of them is
/// due and <see cref="OldestQueuedUtc"/> is the whole signal.
/// </summary>
public class DocumentEmailOutboxDepthDto
{
    public int Queued { get; init; }
    public int Failed { get; init; }
    public DateTime? OldestQueuedUtc { get; init; }
}
