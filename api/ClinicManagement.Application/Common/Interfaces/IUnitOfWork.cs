namespace ClinicManagement.Application.Common.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check the save against the version the <b>user</b> was editing, not the one the server just loaded.
    ///
    /// <para>
    /// Without this the conflict check is worthless: the handler re-reads the row microseconds before saving,
    /// so its token always matches and a peer's edit made ten minutes ago is overwritten just the same. The
    /// caller round-trips <c>Version</c> from the read DTO and hands it back here.
    /// </para>
    /// <para>
    /// <b>Zero means "not supplied" and skips the check.</b> Real rows never carry an <c>xmin</c> of 0, and
    /// several writers legitimately have no user-held version — the AI action dispatcher, the Google→App
    /// calendar sync, the reminder job. Making it mandatory would break them for no gain;
    /// they are not two people editing one form.
    /// </para>
    /// </summary>
    void SetExpectedVersion(object entity, uint expectedVersion);

    /// <summary>
    /// Stop tracking an entity that has already been committed — for a handler that saves <b>many times in one
    /// request</b>.
    ///
    /// <para><b>Written for the CSV import (L5), and needed by nothing else today.</b> That import saves once per
    /// row on purpose: the spec requires an import to be « all-or-nothing per <i>row</i>, never a silent partial
    /// commit », and one save for the whole file cannot give that — a single refused row would take the other 2 999
    /// with it. But every committed row stays in the change tracker, and EF re-scans every tracked entry on each
    /// subsequent save, so a 3 000-row file does ~4.5 million property comparisons for work that is finished.
    /// Detaching each row after its own commit keeps the loop linear.</para>
    ///
    /// <para>⚠️ <b>Only ever call this on an entity whose save has succeeded.</b> Detaching an <c>Added</c> entry
    /// before its commit discards the insert silently — no exception, no row, and a report that says
    /// « créé ». Ordinary single-save handlers must not use this at all.</para>
    /// </summary>
    void StopTracking(object entity);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}



