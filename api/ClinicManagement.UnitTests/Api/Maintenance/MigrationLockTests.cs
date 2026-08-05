using ClinicManagement.API.Startup;
using ClinicManagement.UnitTests.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Api.Maintenance;

/// <summary>
/// The advisory lock serialising the startup migrate-and-backfill block across instances (multi-tenant-cloud US-6).
///
/// <para><b>What can actually go wrong here is not "does it lock".</b> Nothing in this project touches a database,
/// so acquiring a real lock is out of reach. What <i>is</i> reachable — and what a mistake here would look like —
/// is the two properties the whole mechanism rests on: both statements must name the <b>same fixed</b> key (two
/// instances naming different numbers serialise nothing, and the failure is invisible until two containers migrate
/// at once), and the lock must be <b>session-level</b> rather than transaction-level, because
/// <c>pg_advisory_xact_lock</c> would be released at the first commit <i>inside</i> the migration — leaving the
/// rest of it unprotected while looking correct.</para>
///
/// <para>The third property — that the migration is actually <i>inside</i> the lock — is asserted against
/// <c>Program.cs</c>'s own source, because a lock the startup path forgot to wrap is exactly as broken as no lock
/// and nothing else in the build can see it.</para>
/// </summary>
public class MigrationLockTests
{
    private static string ProgramSource() =>
        File.ReadAllText(Path.Combine(
            SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

    [Fact]
    public void Both_statements_name_the_same_key()
    {
        var key = MigrationLock.LockKey.ToString();

        Assert.Contains(key, MigrationLock.AcquireSql, StringComparison.Ordinal);
        Assert.Contains(key, MigrationLock.ReleaseSql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lock_is_session_level_and_not_transaction_level()
    {
        // pg_advisory_xact_lock releases at the first COMMIT inside the migration — every statement after it would
        // run unprotected, and the symptom is a half-applied schema on the losing instance.
        Assert.Contains("pg_advisory_lock(", MigrationLock.AcquireSql, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_unlock(", MigrationLock.ReleaseSql, StringComparison.Ordinal);
        Assert.DoesNotContain("xact", MigrationLock.AcquireSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xact", MigrationLock.ReleaseSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_startup_migration_runs_inside_the_lock()
    {
        var program = ProgramSource();

        var lockCall = program.IndexOf($"{nameof(MigrationLock)}.{nameof(MigrationLock.RunExclusivelyAsync)}",
            StringComparison.Ordinal);
        var migrate = program.IndexOf("Database.MigrateAsync(", StringComparison.Ordinal);

        Assert.True(lockCall >= 0, "Program.cs no longer calls MigrationLock.RunExclusivelyAsync.");
        Assert.True(migrate >= 0, "Program.cs no longer migrates on startup.");

        // The migrate call must come AFTER the lock is taken. An unwrapped Migrate() compiles, starts, and races.
        Assert.True(
            migrate > lockCall,
            "Database.MigrateAsync is no longer inside MigrationLock.RunExclusivelyAsync — two instances starting "
            + "together would apply the same migrations concurrently.");
    }

    [Fact]
    public void The_synchronous_Migrate_overload_is_not_used_on_the_startup_path()
    {
        var program = ProgramSource();

        // `Database.Migrate()` cannot be awaited inside the lock's async body, so its reappearance would mean the
        // wrap had been undone rather than merely moved.
        Assert.DoesNotContain("Database.Migrate()", program, StringComparison.Ordinal);
    }
}
