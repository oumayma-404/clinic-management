using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Serialises the startup migrate-and-backfill block across instances with a PostgreSQL <b>advisory lock</b>
/// (multi-tenant-cloud US-6).
///
/// <para><b>Why.</b> EF Core 8 takes no lock of its own around <c>Database.Migrate()</c>. One instance is fine;
/// a hosted deployment that scales to two, or simply redeploys before the old container has exited, has two
/// processes reading « these migrations are pending » at the same moment and both applying them. The second one
/// fails part-way — a duplicate index, a column that already exists — and what it leaves behind is a database
/// that is neither the old schema nor the new one. The per-clinic backfills are inside the lock for the same
/// reason: both are check-then-insert and idempotent only against themselves, not against a concurrent twin.</para>
///
/// <para><b>Advisory, not a table.</b> A lock row would need its own migration to exist — the thing being
/// protected — and a crashed holder would leave it set forever. A session-level advisory lock is released by
/// PostgreSQL when the connection drops, so a killed container cannot wedge the next deploy.</para>
///
/// <para><b>Blocking on purpose.</b> The loser waits rather than skipping: it then finds nothing pending and
/// continues, which is the behaviour a rolling deploy needs. Skipping would let it serve requests against a
/// half-migrated schema.</para>
///
/// <para>⚠️ Not used by <see cref="DeferredStartupService"/>: that path exists only where the app is a single
/// Windows service on the clinic's own PC, so there is no second instance to race, and the lock would also span
/// its pre-migration <c>pg_dump</c>.</para>
/// </summary>
public static class MigrationLock
{
    /// <summary>
    /// The advisory-lock key. Arbitrary but <b>fixed</b> — every instance must name the same number or the lock
    /// serialises nothing. Scoped to this application's own use of the database; PostgreSQL keeps advisory locks
    /// in a namespace of their own, so it cannot collide with a row or table lock.
    /// </summary>
    public const long LockKey = 5_314_072_026_000_001;

    // static readonly, not const: an interpolated string is only a compile-time constant when every hole is
    // itself a string constant, and LockKey is a long.
    public static readonly string AcquireSql = $"SELECT pg_advisory_lock({LockKey})";
    public static readonly string ReleaseSql = $"SELECT pg_advisory_unlock({LockKey})";

    /// <summary>
    /// Runs <paramref name="work"/> while holding the lock, on the context's own connection.
    /// </summary>
    public static async Task RunExclusivelyAsync(
        DatabaseFacade database,
        ILogger logger,
        Func<Task> work,
        CancellationToken cancellationToken = default)
    {
        // Opened explicitly so the lock, the work and the release all run on ONE session — an advisory lock is
        // held by the session that took it, and a connection returned to the pool in between would release it.
        await database.OpenConnectionAsync(cancellationToken);

        try
        {
            logger.LogInformation("Acquiring the startup migration lock...");
            await database.ExecuteSqlRawAsync(AcquireSql, cancellationToken);

            try
            {
                await work();
            }
            finally
            {
                // Not strictly required — closing the connection releases it — but an explicit release keeps the
                // lock held for the shortest possible window rather than until the pool reclaims the session.
                await database.ExecuteSqlRawAsync(ReleaseSql, CancellationToken.None);
            }
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }
}
