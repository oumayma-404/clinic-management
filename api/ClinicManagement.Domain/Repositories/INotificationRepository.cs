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
/// </summary>
public record ReminderLogCounts(int SentToday, int Pending, int FailedRecent);

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Due <c>Pending</c> rows, oldest first, capped at <paramref name="take"/> (AC-P4.31). The cap exists
    /// because one large backlog could otherwise make a single minutely tick run for minutes while holding the
    /// job's <c>[DisableConcurrentExecution]</c> lock.
    /// </summary>
    Task<IEnumerable<Notification>> GetPendingNotificationsAsync(
        int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes **terminal** rows (<c>Sent</c> / <c>Failed</c>) older than <paramref name="olderThanUtc"/> and
    /// returns how many went (AC-P4.32). Nothing has ever purged this table, so it grows forever.
    ///
    /// <b>A <c>Pending</c> row is never deleted</b> (AC-P4.34) — suppressing an unsent reminder is
    /// <c>VoidUnsentAsync</c>'s job, and retention silently dropping one would mean a patient never contacted
    /// with no trace of why.
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



