using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The clinic-wide reminder counters shown above the delivery log.
///
/// <para><b>They deliberately ignore the log's filters.</b> Derived from the visible rows they would read « the
/// failures among these 25 » — the same defect the stock screen's chips document. They are facts about the
/// clinic, not about the current page.</para>
///
/// <para><see cref="FailedRecent"/> spans several days rather than today, on purpose: a send that failed at 23:00
/// would otherwise disappear from the counter at midnight, before anyone came in to see it.</para>
///
/// <para><see cref="Blocked"/> is unbounded by date for the same reason <see cref="Pending"/> is, and it is the
/// counter the whole <c>Blocked</c> status exists to make visible: a queue that silently stops sending is the
/// defect, so « N rappels bloqués » has to be a number on the page rather than a state only the database knows.</para>
/// </summary>
public record ReminderLogCounts(int SentToday, int Pending, int FailedRecent, int Blocked);

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Due <c>Pending</c> rows, oldest first, capped at <paramref name="batchSize"/> (AC-P4.31). The cap exists
    /// because one large backlog could otherwise make a single minutely tick run for minutes while holding the
    /// job's <c>[DisableConcurrentExecution]</c> lock.
    ///
    /// <para><b><paramref name="perClinicBound"/> is what stops one clinic starving the others</b> (L3a). The
    /// scan had no clinic dimension at all: on a shared install, one practice with more due rows than the batch
    /// size owned every tick and nobody else's reminders ever left the queue. Clinics are served
    /// <b>oldest-due-first</b> and each may contribute at most this many rows per tick, so the fair share is a
    /// property of the read rather than of whichever clinic happened to enqueue first.</para>
    ///
    /// <para>Only <c>Pending</c> is scanned. A row that cannot be sent for a reason a retry cannot change is
    /// moved to <see cref="NotificationStatus.Blocked"/> by the dispatcher and so leaves this query — which is
    /// the other half of the same starvation defect.</para>
    /// </summary>
    Task<IReadOnlyList<Notification>> GetDueForDispatchAsync(
        int batchSize, int perClinicBound, CancellationToken cancellationToken = default);

    /// <summary>
    /// A bounded page of <see cref="NotificationStatus.Blocked"/> rows, oldest-due first, for the dispatcher to
    /// re-evaluate. Without this read the status would be a one-way door: the row was kept precisely so that it
    /// sends once the channel is configured, and nothing else in the system ever looks at it.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetBlockedForReviewAsync(
        int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes **terminal** rows (<c>Sent</c> / <c>Failed</c>) older than <paramref name="olderThanUtc"/> and
    /// returns how many went (AC-P4.32). Nothing has ever purged this table, so it grows forever.
    ///
    /// <b>Neither a <c>Pending</c> nor a <c>Blocked</c> row is ever deleted</b> (AC-P4.34) — suppressing an
    /// unsent reminder is <c>VoidUnsentAsync</c>'s job, and retention silently dropping one would mean a patient
    /// never contacted with no trace of why. That is exactly why <c>Blocked</c> had to be a *new*, non-terminal
    /// status rather than a flavour of <c>Failed</c>.
    /// </summary>
    Task<int> PurgeTerminalOlderThanAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
    Task<IEnumerable<Notification>> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The <paramref name="take"/> most-recent reminder rows for a clinic (newest first), with the patient
    /// loaded so the recipient phone can be masked for the admin delivery-status surface (AC-3).
    /// </summary>
    Task<IEnumerable<Notification>> GetRecentByClinicIdAsync(Guid clinicId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a clinic's reminder log — newest first — with every filter applied <b>in SQL</b>.
    ///
    /// <para>Replaces <see cref="GetRecentByClinicIdAsync"/> for the « Rappels » page, which needs to answer
    /// « pourquoi ce patient n'a pas reçu son SMS la semaine dernière ? ». That read took only a <c>take</c>, so
    /// the answer lived past the end of the twenty rows it returned.</para>
    ///
    /// <para><b>Every filter is a parameter here rather than a client-side predicate.</b> Filtering an
    /// already-cut window answers a different question — it becomes « the failures among the newest 20 » — which
    /// is exactly what the paging work removed from the catalogs, the patients list and the lab orders. A null
    /// filter means "not applied", never "match nothing".</para>
    ///
    /// <para><paramref name="toUtcInclusive"/> is inclusive on both ends, matching the money reads: an exclusive
    /// upper bound built from a local midnight counts a row in two adjacent windows.</para>
    /// </summary>
    Task<PagedResult<Notification>> GetClinicLogAsync(
        Guid clinicId,
        NotificationStatus? status,
        NotificationType? channel,
        DateTime? fromUtc,
        DateTime? toUtcInclusive,
        PageRequest? paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The three clinic-wide counters above the log. Bounds are supplied by the caller because only the
    /// Application layer knows the clinic's local day (<c>ClinicClock</c>) — a UTC day would move the boundary by
    /// an hour and file an early-morning send into yesterday.
    /// </summary>
    Task<ReminderLogCounts> GetClinicLogCountsAsync(
        Guid clinicId,
        DateTime todayFromUtc,
        DateTime todayToUtcInclusive,
        DateTime failedSinceUtc,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// The recall rows of one « Relancer » click: every row for this patient that carries <b>no</b>
    /// appointment and the same <paramref name="scheduledFor"/> instant (the scheduler stamps one value across
    /// all channels of a send). Lets the dispatcher decide the patient's post-failure state only once every
    /// channel of that batch has resolved, instead of un-snoozing on the first channel to fail (AC-P3.6).
    /// </summary>
    Task<IEnumerable<Notification>> GetRecallBatchAsync(
        Guid patientId, DateTime scheduledFor, CancellationToken cancellationToken = default);

    Task<Notification> AddAsync(Notification notification, CancellationToken cancellationToken = default);
    Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default);
    Task RemoveAsync(Notification notification, CancellationToken cancellationToken = default);
}



