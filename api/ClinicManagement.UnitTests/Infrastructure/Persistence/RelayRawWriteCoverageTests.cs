using System.Reflection;
using System.Text.RegularExpressions;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The writes the change log cannot see (<c>clinic-pc-copy</c> D1). Capture hooks <c>SaveChangesAsync</c>, so a bulk
/// <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> or raw SQL reaches the database with no <c>ClinicChange</c> row and the PC de
/// secours never hears of it. Derived from the sources: every bulk write is resolved to its table, and one on a table
/// the PC carries must be reviewed below with what repairs it; raw SQL is reviewed per file.
/// </summary>
public class RelayRawWriteCoverageTests
{
    /// <summary>Bulk writes on a carried table, keyed <c>File.cs:Entity</c>, with what keeps the PC right anyway.</summary>
    private static readonly Dictionary<string, string> ReviewedCarriedBulkWrites = new(StringComparer.Ordinal)
    {
        // Terminal reminders past retention, purged every minute. The PC keeps them until the hourly check (D25)
        // finds the Notification table differs and re-copies it; a delete of history the cloud no longer shows is
        // the one difference whose late repair costs nothing.
        ["NotificationRepository.cs:Notification"] = "terminal rows past retention; repaired by the hourly digest",
    };

    /// <summary>Files holding raw SQL that writes, each with why the PC needs no change row for it.</summary>
    private static readonly Dictionary<string, string> ReviewedRawSqlFiles = new(StringComparer.Ordinal)
    {
        ["RehashAuditChainCommand.cs"] = "AuditEntry only — the journal is each side's own and never carried",
        ["HealthChecks.cs"] = "SELECT 1",
        ["MigrationLock.cs"] = "an advisory lock, no row",
        ["AuditChainAppender.cs"] = "an advisory lock, no row",
        ["ClinicPurge.cs"] = "deletes the whole cabinet, its ClinicRelay row included — the PC is then refused as unknown",
        ["ClinicChangeCapture.cs"] = "the change log's own cursor",
        ["ClinicRelayRowStore.cs"] = "the relay's own applier and reader; it writes on the PC, where nothing is captured",
        ["RestoreBackupCommand.cs"] = "a whole-database restore, app stopped; the PC's continuity check (D12) then refuses the older cloud",
        ["SchemaVerificationReader.cs"] = "catalog reads only",
        ["PgDumpBackupService.cs"] = "pg_database_size, a read",
    };

    private static readonly Regex BulkWrite = new(@"\.Execute(Delete|Update)(Async)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex RawSql = new(
        @"\.ExecuteSql(Raw|Interpolated)?(Async)?\s*\(|\.Execute(NonQuery|Scalar)(Async)?\s*\(", RegexOptions.Compiled);

    private static IEnumerable<(string File, string Source)> ProductionSources()
    {
        var root = SolutionSources.Root();
        foreach (var project in new[] { "ClinicManagement.API", "ClinicManagement.Application", "ClinicManagement.Infrastructure" })
        {
            foreach (var path in SolutionSources.CsFiles(new DirectoryInfo(Path.Combine(root.FullName, project))))
            {
                if (path.Replace('\\', '/').Contains("/Migrations/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (Path.GetFileName(path), SolutionSources.WithoutComments(File.ReadAllText(path)));
            }
        }
    }

    /// <summary>DbSet property name → entity CLR name, read off the context itself.</summary>
    private static Dictionary<string, string> DbSets() =>
        typeof(ApplicationDbContext).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToDictionary(p => p.Name, p => p.PropertyType.GetGenericArguments()[0].Name, StringComparer.Ordinal);

    /// <summary>Every bulk write as <c>File.cs:Entity</c>, or <c>File.cs:?</c> when the statement names no DbSet.</summary>
    private static List<string> BulkWrites(IEnumerable<(string File, string Source)> sources)
    {
        var sets = DbSets();
        var found = new List<string>();
        foreach (var (file, source) in sources)
        {
            foreach (Match match in BulkWrite.Matches(source))
            {
                var start = source.LastIndexOfAny(new[] { ';', '{', '}' }, match.Index) + 1;
                var statement = source[start..match.Index];
                var entity = Regex.Matches(statement, @"\.\s*(\w+)")
                    .Select(m => m.Groups[1].Value)
                    .Where(sets.ContainsKey)
                    .Select(name => sets[name])
                    .FirstOrDefault();
                found.Add($"{file}:{entity ?? "?"}");
            }
        }

        return found;
    }

    private static ClinicRelayPlan Plan()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none").Options, null);
        return ClinicRelayScope.Resolve(db.Model);
    }

    // Non-vacuity: the scan reaches the purges this repository is known to have.
    [Fact]
    public void The_Scan_Finds_The_Known_Bulk_Writes()
    {
        var writes = BulkWrites(ProductionSources());

        Assert.Contains("NotificationRepository.cs:Notification", writes);
        Assert.Contains("SessionFamilyRepository.cs:SessionFamily", writes);
    }

    [Fact]
    public void Every_Bulk_Write_On_A_Carried_Table_Is_Reviewed()
    {
        var plan = Plan();
        var unreviewed = BulkWrites(ProductionSources())
            .Where(w => w.EndsWith(":?", StringComparison.Ordinal) || plan.Find(w[(w.IndexOf(':') + 1)..]) is not null)
            .Where(w => !ReviewedCarriedBulkWrites.ContainsKey(w))
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        Assert.True(unreviewed.Count == 0,
            "Bulk write(s) the PC de secours would never hear of: " + string.Join(", ", unreviewed)
            + ". Save through the change tracker, or review the write here with what repairs the PC.");
    }

    [Fact]
    public void Every_Reviewed_Bulk_Write_Still_Exists()
    {
        var writes = BulkWrites(ProductionSources()).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(ReviewedCarriedBulkWrites.Keys.Where(k => !writes.Contains(k)));
    }

    [Fact]
    public void Every_File_With_Raw_Sql_Is_Reviewed_And_Every_Review_Still_Applies()
    {
        var files = ProductionSources()
            .Where(s => RawSql.IsMatch(s.Source))
            .Select(s => s.File)
            .Distinct()
            .OrderBy(f => f)
            .ToList();

        Assert.Equal(ReviewedRawSqlFiles.Keys.OrderBy(f => f), files);
    }

    // Red proof: a purge of a carried table written tomorrow is caught, and one of an excluded table is not.
    [Fact]
    public void The_Guard_Resolves_A_New_Bulk_Write_To_Its_Table()
    {
        var probe = new[]
        {
            ("Probe.cs", "public Task<int> PurgeAsync() => _context.Patients.Where(p => p.IsArchived).ExecuteDeleteAsync(ct);"),
            ("Other.cs", "{ await _context.SessionFamilies.Where(f => true).ExecuteUpdateAsync(s => s, ct); }"),
        };

        Assert.Equal(new[] { "Probe.cs:Patient", "Other.cs:SessionFamily" }, BulkWrites(probe));
        Assert.NotNull(Plan().Find("Patient"));
        Assert.Null(Plan().Find("SessionFamily"));
    }
}
