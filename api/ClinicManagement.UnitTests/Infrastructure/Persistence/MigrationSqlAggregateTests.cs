using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL ships no <c>min</c>/<c>max</c> aggregate for <c>uuid</c>, and a migration that uses one is a
/// <b>startup crash on every deployment</b> rather than a wrong answer.
///
/// <para>This exists because it already happened: <c>BackfillDentalRecordAppointmentLinks</c> shipped with
/// <c>MIN(a."Id")</c> and took the hosted API down with
/// <c>42883: function min(uuid) does not exist</c> — the container exited 139 in a crash loop, and every layer
/// above had reported the change fine. Nothing in this suite touches a database, so a migration is the one class
/// of change unit tests structurally cannot verify; a text check is what is available, and for this defect it is
/// enough, because the broken form is visible in the SQL itself.</para>
///
/// <para><b>Scope, stated honestly.</b> It matches an aggregate over a column whose name ends in <c>Id</c>, which
/// is the realistic shape (`MIN(a."Id")`, `MAX("PatientId")`) and is what actually shipped. A uuid column named
/// something else would slip through — the real gate for that is running the migration against a database, which
/// <c>follow-up/archive-restore-real-database-checks.md</c> already proposes a console verb for. A partial check
/// that names its own limit beats no check; it does not pretend to be the database.</para>
///
/// <para>Legitimate neighbours must keep passing, and do: <c>MAX("Sequence")</c> (integer) in
/// <c>AddAuditChain</c> and <c>MIN("AppointmentDateTime")</c> (timestamptz) in
/// <c>AddPractitionerAttribution</c> are both aggregates this rule has no quarrel with.</para>
/// </summary>
public class MigrationSqlAggregateTests
{
    /// <summary>
    /// <c>MIN(a."Id")</c> / <c>MAX("PatientId")</c> inside a C# verbatim string, where quotes are doubled.
    ///
    /// <para>Requiring the doubled quotes is what keeps prose out of the results: a <c>//</c> comment discussing
    /// <c>MIN("AppointmentDateTime")</c> writes single quotes and is correctly ignored.</para>
    /// </summary>
    private static readonly Regex UuidAggregate = new(
        @"\b(MIN|MAX)\s*\(\s*[A-Za-z_][A-Za-z0-9_]*\s*\.\s*""""[A-Za-z0-9_]*Id""""\s*\)"
        + @"|\b(MIN|MAX)\s*\(\s*""""[A-Za-z0-9_]*Id""""\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoMigrationAggregatesAUuidColumn()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(MigrationsDirectory(), "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = UuidAggregate.Match(WithoutComments(lines[i]));
                if (match.Success)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {match.Value.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "PostgreSQL has no min/max aggregate for `uuid`, so each of these is `42883: function min(uuid) does "
            + "not exist` — the API crash-loops on startup and the deployment never comes up.\n"
            + "Where a GROUP BY is already guarded by `HAVING COUNT(*) = 1`, the intent is to unwrap the single "
            + "row: write `(array_agg(x))[1]`, which says so and has no such gap.\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Everything from a <c>--</c> or <c>//</c> comment marker to end of line.
    ///
    /// <para>Needed rather than fastidious: the fix for the original defect <b>names the broken form in an SQL
    /// comment</b> so the next reader knows why the code looks as it does, and without this the guard reports
    /// that comment as the defect it is warning about. A check that cannot tell prose from SQL makes documenting
    /// a trap impossible, which is the opposite of what it is for.</para>
    ///
    /// <para>No migration in this repo has a string literal containing <c>--</c>, and stripping affects only what
    /// is <i>inspected</i> — never the SQL that runs.</para>
    /// </summary>
    private static string WithoutComments(string line)
    {
        var cut = line.Length;
        foreach (var marker in new[] { "--", "//" })
        {
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0 && at < cut) cut = at;
        }
        return line[..cut];
    }

    /// <summary>The guard must actually match the form that shipped, or it is green for the wrong reason.</summary>
    [Fact]
    public void TheGuardRecognisesTheFormThatBrokeProduction()
    {
        Assert.Matches(UuidAggregate, @"MIN(a.""""Id"""")           AS """"AppointmentId""""");
        Assert.Matches(UuidAggregate, @"max(""""PatientId"""")");
    }

    /// <summary>…and must leave the legitimate aggregates alone, or it is a check nobody can keep green.</summary>
    [Fact]
    public void TheGuardIgnoresAggregatesOverOtherTypes()
    {
        Assert.DoesNotMatch(UuidAggregate, @"COALESCE(MAX(""""Sequence""""), 0)");
        Assert.DoesNotMatch(UuidAggregate, @"MIN(a.""""AppointmentDateTime"""")");
        // Prose in a `//` comment uses single quotes and must not be read as SQL.
        Assert.DoesNotMatch(UuidAggregate, @"// ⚠️ `MIN(""Id"")` picks the earliest such visit");
        // The replacement itself.
        Assert.DoesNotMatch(UuidAggregate, @"(array_agg(a.""""Id""""))[1]");
    }

    /// <summary>
    /// An SQL comment naming the broken form is documentation, not a defect.
    ///
    /// <para>This is not hypothetical: the fix for the original crash does exactly this, and the first version of
    /// this guard reported that comment as the bug.</para>
    /// </summary>
    [Fact]
    public void AnSqlCommentNamingTheBrokenFormIsNotADefect()
    {
        const string comment = @"-- ⚠️ NOT `MIN(a.""""Id"""")`: PostgreSQL ships no min/max aggregate for uuid.";
        Assert.Matches(UuidAggregate, comment);              // the raw line does contain it…
        Assert.DoesNotMatch(UuidAggregate, WithoutComments(comment)); // …and the inspected line does not.
    }

    /// <summary>
    /// Locates the migrations folder from this source file's own compile-time path — not
    /// <c>AppContext.BaseDirectory</c>, which is routinely outside the repository (the Smart App Control
    /// workaround builds to a temp path), so a walk up from the binary would find nothing.
    /// </summary>
    private static string MigrationsDirectory([CallerFilePath] string thisFile = "")
    {
        const string relative = "api/ClinicManagement.Infrastructure/Migrations";
        var native = relative.Replace('/', Path.DirectorySeparatorChar);

        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fail loudly. A check that skips when it cannot find its subject reports green while guarding nothing.
        throw new DirectoryNotFoundException(
            $"Could not locate '{relative}' walking up from '{thisFile}'. Migration SQL cannot be verified.");
    }
}
