using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Where <c>pg_dump</c>/<c>pg_restore</c> come from, for every deployment and with <b>no operator configuration</b>.
///
/// <para><b>Why this class exists.</b> Both tools used to be reached through <c>Backup:PgDumpPath</c> alone, and
/// that key is written by exactly one of the four ways this product is deployed — the Windows installer.
/// Everywhere else it is the empty string that ships in <c>appsettings.json</c>, so « Sauvegarder maintenant », the
/// hourly <c>BackupJob</c>, the pre-migration safety dump and the <c>restore-backup</c> verb all answered
/// « L'outil pg_dump est introuvable » for the life of the product, while every other layer reported the feature
/// present. Discovery is what makes the container and a developer machine work, so it is the discovery order these
/// tests are about.</para>
///
/// <para><b>The filesystem is a seam</b> (<see cref="PostgresToolLocator.FileSystem"/>), so the search order is
/// asserted against a fake tree rather than by installing PostgreSQL three times — and these tests therefore hold
/// on the ubuntu CI runner as well as on Windows.</para>
/// </summary>
public class PostgresToolLocatorTests
{
    // ⚠️ Platform-NATIVE absolute paths, built rather than written as POSIX literals. `SiblingOf` normalises
    // through `Path.GetFullPath`, so on Windows a literal "/opt/pg/bin/pg_dump" comes back as
    // "C:\opt\pg\bin\pg_dump" and no longer matches the fake tree — a fixture artifact that reads exactly like a
    // resolution bug. Anything compared against a resolved path has to survive that normalisation.
    private static readonly string ConfiguredBin =
        Path.Combine(Path.GetTempPath(), "clinic-locator-tests", "pg", "bin");

    private static readonly string ConfiguredDump = Path.Combine(ConfiguredBin, ExecutableName("pg_dump"));
    private static readonly string ConfiguredRestore = Path.Combine(ConfiguredBin, ExecutableName("pg_restore"));

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    /// <summary>A fake tree: only the named paths exist, and no directory has any children.</summary>
    private static PostgresToolLocator.FileSystem Tree(params string[] existingFiles) =>
        new(path => existingFiles.Contains(path, StringComparer.Ordinal), _ => Array.Empty<string>());

    /// <summary>A fake tree whose directory listings are scripted, for the versioned-install cases.</summary>
    private static PostgresToolLocator.FileSystem Tree(
        IEnumerable<string> existingFiles, Dictionary<string, string[]> directories) =>
        new(
            path => existingFiles.Contains(path, StringComparer.Ordinal),
            path => directories.TryGetValue(path, out var children) ? children : Array.Empty<string>());

    // ---- Explicit configuration still wins -------------------------------------------------------------------

    /// <summary>
    /// A configured path is honoured — the Windows installer writes one, and an operator who names a path means it.
    /// </summary>
    [Fact]
    public void A_configured_pg_dump_path_wins()
    {
        var resolved = PostgresToolLocator.LocatePgDump(
            Configuration((PostgresToolLocator.PgDumpPathKey, ConfiguredDump)),
            Tree(ConfiguredDump, ConfiguredRestore));

        Assert.Equal(ConfiguredDump, resolved);
    }

    /// <summary>
    /// Surrounding whitespace is trimmed. An installer writing a path into JSON, or a hand-edited
    /// <c>appsettings.Production.json</c>, is exactly where a stray space comes from — and the old code compared it
    /// straight to <see cref="File.Exists(string)"/>, so it read as « the tool is missing ».
    /// </summary>
    [Fact]
    public void A_configured_path_is_trimmed()
    {
        var resolved = PostgresToolLocator.LocatePgDump(
            Configuration((PostgresToolLocator.PgDumpPathKey, $"  {ConfiguredDump}  ")),
            Tree(ConfiguredDump, ConfiguredRestore));

        Assert.Equal(ConfiguredDump, resolved);
    }

    /// <summary>
    /// ⚠️ A configured path naming a file that <b>does not exist</b> falls through to discovery rather than
    /// failing. That is the packaged-install-upgrade case: the installer wrote a path for the PostgreSQL it
    /// bundled, and if that moves, a working tool on PATH is a better answer than a refusal quoting a stale key.
    /// </summary>
    [Fact]
    public void A_configured_path_that_does_not_exist_falls_through_to_discovery()
    {
        // A real PATH entry, so the assertion is about the production search order rather than about a directory
        // this test invented (and which on Windows would not be searched at all).
        var onPath = Path.Combine(FirstPathEntry()!, ExecutableName("pg_dump"));
        var fs = Tree(onPath, Path.Combine(FirstPathEntry()!, ExecutableName("pg_restore")));

        var resolved = PostgresToolLocator.LocatePgDump(
            Configuration((PostgresToolLocator.PgDumpPathKey, Path.Combine(ConfiguredBin, "gone"))), fs);

        Assert.Equal(onPath, resolved);
    }

    // ---- Discovery: the case the whole change is for ---------------------------------------------------------

    /// <summary>
    /// [The container case] With <b>nothing configured at all</b>, the tools are found on <c>PATH</c> — which is
    /// where <c>api/Dockerfile</c>'s <c>postgresql-client-16</c> puts them (<c>/usr/bin/pg_dump</c>, verified by
    /// building the image). This single assertion is the difference between backup working out of the box on every
    /// Docker deployment and not existing there.
    /// </summary>
    [Fact]
    public void With_nothing_configured_the_tools_are_found_on_PATH()
    {
        var directory = FirstPathEntry();
        Assert.NotNull(directory); // the harness needs a PATH to look in; every OS this runs on has one

        var pgDump = Path.Combine(directory!, ExecutableName("pg_dump"));
        var pgRestore = Path.Combine(directory!, ExecutableName("pg_restore"));

        var configuration = Configuration();
        var fs = Tree(pgDump, pgRestore);

        Assert.Equal(pgDump, PostgresToolLocator.LocatePgDump(configuration, fs));
        Assert.Equal(pgRestore, PostgresToolLocator.LocatePgRestore(configuration, pgDump, fs));
    }

    /// <summary>
    /// A machine with neither a configured path nor a discoverable copy resolves to <c>null</c> — it does not
    /// throw. The caller words its own French refusal, because « this server has no backup tool » and « the backup
    /// failed » are different sentences to an operator.
    /// </summary>
    [Fact]
    public void A_machine_with_no_tools_resolves_to_null_rather_than_throwing()
    {
        var configuration = Configuration();

        Assert.Null(PostgresToolLocator.LocatePgDump(configuration, Tree()));
        Assert.Null(PostgresToolLocator.LocatePgRestore(configuration, pgDumpPath: null, Tree()));
    }

    /// <summary>
    /// ⚠️ A directory holding <b>only</b> <c>pg_dump</c> is not a candidate. A dump is reported successful only
    /// once <c>pg_restore --list</c> reads its table of contents back, so a lone <c>pg_dump</c> cannot serve a
    /// backup — and resolving it would turn « no tools » into the later, more confusing « la sauvegarde n'a pas pu
    /// être vérifiée ».
    /// </summary>
    [Fact]
    public void A_directory_with_only_pg_dump_is_not_a_candidate()
    {
        var directory = FirstPathEntry()!;
        var fs = Tree(Path.Combine(directory, ExecutableName("pg_dump")));

        Assert.Null(PostgresToolLocator.LocatePgDump(Configuration(), fs));
    }

    // ---- pg_restore resolution ------------------------------------------------------------------------------

    /// <summary>
    /// <c>pg_restore</c> is taken from beside the <c>pg_dump</c> in hand before anything is discovered: whoever
    /// named that <c>pg_dump</c> chose an installation, and the tool that <i>verifies</i> the dump must come from
    /// the same one. A newer copy earlier on PATH must not win here.
    /// </summary>
    [Fact]
    public void Pg_restore_comes_from_beside_the_pg_dump_in_hand()
    {
        var elsewhere = Path.Combine(FirstPathEntry()!, ExecutableName("pg_restore"));
        var fs = Tree(ConfiguredDump, ConfiguredRestore, elsewhere);

        var resolved = PostgresToolLocator.LocatePgRestore(
            Configuration((PostgresToolLocator.PgDumpPathKey, ConfiguredDump)), ConfiguredDump, fs);

        Assert.Equal(ConfiguredRestore, resolved);
    }

    /// <summary>
    /// An explicit <c>Backup:PgRestorePath</c> beats the sibling — mixing the two installations is possible only
    /// by an operator saying so out loud, which is the point of it being a separate key.
    /// </summary>
    [Fact]
    public void A_configured_pg_restore_path_beats_the_sibling()
    {
        const string chosen = "/somewhere/else/pg_restore";
        var fs = Tree(ConfiguredDump, ConfiguredRestore, chosen);

        var resolved = PostgresToolLocator.LocatePgRestore(
            Configuration(
                (PostgresToolLocator.PgDumpPathKey, ConfiguredDump),
                (PostgresToolLocator.PgRestorePathKey, chosen)),
            ConfiguredDump,
            fs);

        Assert.Equal(chosen, resolved);
    }

    /// <summary>
    /// The sibling carries the reference tool's own extension, so this works on Linux (no <c>.exe</c>) as well as
    /// on the Windows install the feature was written for.
    /// </summary>
    [Fact]
    public void The_sibling_keeps_the_reference_tools_extension()
    {
        const string windowsDump = @"C:\Program Files\Clinic Management\postgres\bin\pg_dump.exe";
        var windowsRestore = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(windowsDump))!, "pg_restore.exe");

        var resolved = PostgresToolLocator.LocatePgRestore(
            Configuration((PostgresToolLocator.PgDumpPathKey, windowsDump)),
            windowsDump,
            Tree(windowsDump, windowsRestore));

        Assert.Equal(windowsRestore, resolved);
    }

    // ---- Versioned installations ----------------------------------------------------------------------------

    /// <summary>
    /// ⚠️ Several PostgreSQL installations resolve to the <b>newest</b>, and the version is compared as an integer
    /// rather than as text — <c>9</c> sorts above <c>16</c> lexicographically. It matters because <c>pg_dump</c>
    /// refuses a server whose major version is <i>newer</i> than its own while the reverse works fine, so picking
    /// the oldest install on a developer machine with three of them produces a version error naming neither the
    /// tool that was chosen nor the one that should have been.
    /// </summary>
    [Fact]
    public void The_newest_versioned_installation_wins()
    {
        // Only exercised where the versioned roots are the Linux ones; on Windows the equivalent roots are under
        // Program Files and the same ordering code runs over them.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string root = "/usr/lib/postgresql";
        var directories = new Dictionary<string, string[]>
        {
            [root] = new[] { $"{root}/9", $"{root}/16", $"{root}/15" }
        };
        var files = new[]
        {
            $"{root}/9/bin/pg_dump", $"{root}/9/bin/pg_restore",
            $"{root}/15/bin/pg_dump", $"{root}/15/bin/pg_restore",
            $"{root}/16/bin/pg_dump", $"{root}/16/bin/pg_restore"
        };

        var resolved = PostgresToolLocator.LocatePgDump(Configuration(), Tree(files, directories));

        Assert.Equal($"{root}/16/bin/pg_dump", resolved);
    }

    /// <summary>
    /// A directory listing that throws (a root that does not exist, or one this account may not list) is simply
    /// not a candidate — discovery must never fail an operation.
    /// </summary>
    [Fact]
    public void A_directory_listing_that_throws_is_not_fatal()
    {
        var fs = new PostgresToolLocator.FileSystem(
            _ => false,
            _ => throw new UnauthorizedAccessException("nope"));

        Assert.Null(PostgresToolLocator.LocatePgDump(Configuration(), fs));
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static string ExecutableName(string tool) =>
        OperatingSystem.IsWindows() ? $"{tool}.exe" : tool;

    /// <summary>
    /// The first usable <c>PATH</c> entry, so the PATH cases assert against the real environment variable the
    /// production code reads rather than against a value this test invents.
    /// </summary>
    private static string? FirstPathEntry() =>
        Environment.GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().Trim('"'))
            .FirstOrDefault(entry => entry.Length > 0);
}
