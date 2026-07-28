using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

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



