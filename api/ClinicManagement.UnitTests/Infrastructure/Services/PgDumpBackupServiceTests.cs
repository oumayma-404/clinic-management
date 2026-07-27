using ClinicManagement.Infrastructure.Security;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The pg_dump-backed backup service (US-8 / AC-8.2, AC-8.3). Covers the fail-loud pre-checks that run
/// <b>before</b> any process is started — missing connection string, missing pg_dump, and missing
/// destination each surface a distinct, non-silent error. (The actual dump requires a real pg_dump.exe +
/// PostgreSQL, exercised by the operator per packaging/README.md — Smart App Control blocks running
/// freshly-built test DLLs here anyway, R-1.)
///
/// Also covers the backup-output hardening (security-hardening US-14): the destination folder must be
/// access-restricted <b>before</b> the dump is written, or one click hands out an unprotected copy of
/// everything the install-level hardening protects.
/// </summary>
public sealed class PgDumpBackupServiceTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Every icacls invocation the service caused, in order.</summary>
    private readonly List<IReadOnlyList<string>> _aclInvocations = new();

    public PgDumpBackupServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    /// <summary>A hardener over a recording fake, so no real ACL is touched by the suite.</summary>
    private DirectoryAclHardener RecordingHardener(int exitCode = 0) => new(args =>
    {
        _aclInvocations.Add(args);
        return new AclCommandResult(exitCode, exitCode == 0 ? string.Empty : "Accès refusé.");
    });

    private PgDumpBackupService Service(Dictionary<string, string?> settings, DirectoryAclHardener? hardener = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new PgDumpBackupService(
            configuration,
            NullLogger<PgDumpBackupService>.Instance,
            hardener ?? RecordingHardener());
    }

    /// <summary>
    /// Config that gets past every pre-check so execution reaches the backup folder. The dummy pg_dump is a
    /// real file (so the existence check passes) but not a real executable, so the dump itself fails — which
    /// is fine: the hardening happens before it, and the failure exercises the partial-cleanup path.
    /// </summary>
    private Dictionary<string, string?> ReachesBackupFolder()
    {
        var dummyPgDump = Path.Combine(_dir, "pg_dump.exe");
        File.WriteAllText(dummyPgDump, "dummy");

        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=clinic;Username=u;Password=p",
            ["Backup:PgDumpPath"] = dummyPgDump,
        };
    }

    private static string? CreatedBackupFolder(string destination) =>
        Directory.EnumerateDirectories(destination, "clinic-backup-*").FirstOrDefault();

    [Fact]
    public async Task Missing_connection_string_fails_loud()
    {
        var service = Service(new Dictionary<string, string?>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBackupAsync(_dir));
        Assert.Contains("connexion", ex.Message);
    }

    [Fact]
    public async Task Missing_pg_dump_fails_loud()
    {
        var service = Service(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=clinic;Username=u;Password=p",
            ["Backup:PgDumpPath"] = Path.Combine(_dir, "does-not-exist-pg_dump.exe"),
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBackupAsync(_dir));
        Assert.Contains("pg_dump", ex.Message);
    }

    [Fact]
    public async Task Missing_destination_fails_loud()
    {
        // A real (dummy) pg_dump file so the pg_dump check passes and we reach the destination check.
        var settings = ReachesBackupFolder();
        // No destination folder passed and no Backup:DefaultDestination configured.
        var service = Service(settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBackupAsync(null));
        Assert.Contains("destination", ex.Message);
    }

    [Fact]
    public async Task Backup_folder_is_restricted_before_the_dump_is_written() // [AC-14.1] [AC-14.2]
    {
        var service = Service(ReachesBackupFolder());

        // The dummy pg_dump is not a real executable, so the dump fails — but the hardening runs first, so
        // recording it here IS the proof of ordering. If it ran after the dump it would never be recorded.
        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateBackupAsync(_dir));

        Assert.NotEmpty(_aclInvocations);

        // Same policy as the install directories: grant, drop inheritance, remove Users/Everyone.
        Assert.Contains("/grant:r", _aclInvocations[0]);
        Assert.Contains("/inheritance:r", _aclInvocations[1]);
        Assert.Contains(DirectoryAclHardener.SidUsers, _aclInvocations[2]);
        Assert.Contains(DirectoryAclHardener.SidEveryone, _aclInvocations[2]);

        // Applied to the timestamped backup folder, not the destination root the admin chose.
        var target = _aclInvocations[0][0];
        Assert.StartsWith(_dir, target, StringComparison.Ordinal);
        Assert.Contains("clinic-backup-", target, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_acl_failure_on_a_fixed_disk_fails_loud_and_leaves_no_partial_backup() // [AC-14.4]
    {
        var service = Service(ReachesBackupFolder(), RecordingHardener(exitCode: 5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBackupAsync(_dir));

        // The operator sees why, and no half-written folder is left to be mistaken for a usable backup.
        Assert.Contains("Accès refusé.", ex.Message);
        Assert.Null(CreatedBackupFolder(_dir));
    }

    [Fact]
    public async Task An_acl_failure_does_not_fall_back_to_an_unprotected_backup() // [AC-14.4]
    {
        var service = Service(ReachesBackupFolder(), RecordingHardener(exitCode: 5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBackupAsync(_dir));

        // Only the failing grant was attempted — the service must not proceed to dump into a folder it
        // could not secure on a disk where it should have been able to.
        Assert.Single(_aclInvocations);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
