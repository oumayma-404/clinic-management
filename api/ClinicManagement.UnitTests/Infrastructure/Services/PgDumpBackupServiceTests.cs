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
/// </summary>
public sealed class PgDumpBackupServiceTests : IDisposable
{
    private readonly string _dir;

    public PgDumpBackupServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private static PgDumpBackupService Service(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new PgDumpBackupService(configuration, NullLogger<PgDumpBackupService>.Instance);
    }

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
        var dummyPgDump = Path.Combine(_dir, "pg_dump.exe");
        await File.WriteAllTextAsync(dummyPgDump, "dummy");

        var service = Service(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=clinic;Username=u;Password=p",
            ["Backup:PgDumpPath"] = dummyPgDump,
            // No destination folder passed and no Backup:DefaultDestination configured.
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBackupAsync(null));
        Assert.Contains("destination", ex.Message);
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
