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
    /// calendar sync, the reminder and e-invoice jobs. Making it mandatory would break them for no gain;
    /// they are not two people editing one form.
    /// </para>
    /// </summary>
    void SetExpectedVersion(object entity, uint expectedVersion);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}



