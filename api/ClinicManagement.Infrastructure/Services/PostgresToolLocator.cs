using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The <b>single</b> answer to « where are <c>pg_dump</c> and <c>pg_restore</c> on this machine? », for every
/// deployment and with <b>no operator configuration required</b>.
///
/// <para><b>Why this exists.</b> Both tools used to be reached through <c>Backup:PgDumpPath</c> alone, read in two
/// places that had already drifted (<c>PgDumpBackupService</c> and the <c>restore-backup</c> verb), and the key is
/// written by exactly one of the four ways this product is deployed — the Windows installer. Everywhere else it is
/// the empty string that ships in <c>appsettings.json</c>, so the whole backup subsystem — the button, the hourly
/// job, the pre-migration safety dump and the restore verb — answered « L'outil pg_dump est introuvable » for the
/// life of the product on every Docker deployment, while reporting itself present at every other layer. A default
/// that only one packaging path fills in is not a default.</para>
///
/// <para><b>The resolution order.</b> Explicit configuration still wins — an operator who names a path means it,
/// and the installer keeps writing one. After that the tools are <i>discovered</i>: beside the application (the
/// bundled <c>postgres\bin</c> the Windows installer lays down), then on <c>PATH</c> (which is what makes the
/// container work, since the image now carries the client), then in the well-known per-version directories both
/// platforms install into. Nothing here throws or refuses: an unresolvable tool comes back <c>null</c> and the
/// caller words its own French refusal, because « no backup tool » and « the backup failed » are different
/// sentences to an operator.</para>
///
/// <para>⚠️ <b>The two tools are resolved as a pair, from the same directory, and that is not tidiness.</b> A dump
/// is only reported successful once <c>pg_restore --list</c> reads its table of contents back, so a
/// <c>pg_dump</c> from one installation verified by a <c>pg_restore</c> from another is a backup checked by a tool
/// that may not understand its format. Mixing them is possible only by naming both keys explicitly, which is an
/// operator saying so out loud.</para>
///
/// <para>⚠️ <b>Versioned directories are searched newest-first</b>, because <c>pg_dump</c> refuses a server whose
/// major version is <i>newer</i> than its own while the reverse works fine. Picking the oldest install on a
/// developer machine with three of them is how a dump starts failing with a version error that names neither the
/// tool that was chosen nor the one that should have been.</para>
/// </summary>
public static class PostgresToolLocator
{
    /// <summary>Configuration key naming <c>pg_dump</c> explicitly. Absent or unreadable ⇒ discover it.</summary>
    public const string PgDumpPathKey = "Backup:PgDumpPath";

    /// <summary>Configuration key naming <c>pg_restore</c> explicitly. Absent ⇒ the sibling of <c>pg_dump</c>.</summary>
    public const string PgRestorePathKey = "Backup:PgRestorePath";

    private const string PgDumpName = "pg_dump";
    private const string PgRestoreName = "pg_restore";

    /// <summary>
    /// The filesystem seam. Production passes <see cref="File.Exists(string)"/> and
    /// <see cref="Directory.EnumerateDirectories(string)"/>; the tests pass a fake tree, so the whole search order
    /// is assertable without installing PostgreSQL three times.
    /// </summary>
    public sealed record FileSystem(Func<string, bool> FileExists, Func<string, IEnumerable<string>> EnumerateDirectories)
    {
        public static readonly FileSystem Real = new(
            File.Exists,
            path =>
            {
                try
                {
                    return Directory.EnumerateDirectories(path);
                }
                catch
                {
                    // A probe directory that does not exist, or that this account may not list, is simply not a
                    // candidate. Discovery must never fail an operation — the caller's `null` branch is the one
                    // that words a refusal.
                    return Array.Empty<string>();
                }
            });
    }

    /// <summary>
    /// Resolves <c>pg_dump</c>: the configured path if it names a real file, else the first discovered copy.
    /// <c>null</c> when the machine genuinely has none.
    /// </summary>
    public static string? LocatePgDump(IConfiguration configuration, FileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? FileSystem.Real;

        var configured = configuration[PgDumpPathKey];
        if (!string.IsNullOrWhiteSpace(configured) && fs.FileExists(configured.Trim()))
        {
            return configured.Trim();
        }

        return LocatePair(fs)?.PgDump;
    }

    /// <summary>
    /// Resolves <c>pg_restore</c>: the configured path, else the sibling of <paramref name="pgDumpPath"/> (they
    /// ship together in PostgreSQL's <c>bin/</c>), else the first discovered copy. <c>null</c> when there is none.
    ///
    /// <para>The sibling is tried before discovery on purpose: whoever named <c>pg_dump</c> — an operator, or the
    /// installer — chose an installation, and the verification tool must come from that same one.</para>
    /// </summary>
    public static string? LocatePgRestore(
        IConfiguration configuration, string? pgDumpPath, FileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? FileSystem.Real;

        var configured = configuration[PgRestorePathKey];
        if (!string.IsNullOrWhiteSpace(configured) && fs.FileExists(configured.Trim()))
        {
            return configured.Trim();
        }

        var sibling = SiblingOf(pgDumpPath, PgRestoreName, fs);
        if (sibling != null)
        {
            return sibling;
        }

        return LocatePair(fs)?.PgRestore;
    }

    /// <summary>
    /// <paramref name="toolName"/> in the same directory as <paramref name="referenceToolPath"/>, carrying the
    /// reference's own extension so this works on Linux (no <c>.exe</c>) as well as on Windows.
    /// </summary>
    private static string? SiblingOf(string? referenceToolPath, string toolName, FileSystem fs)
    {
        if (string.IsNullOrWhiteSpace(referenceToolPath))
        {
            return null;
        }

        string? directory;
        string extension;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(referenceToolPath));
            extension = Path.GetExtension(referenceToolPath);
        }
        catch
        {
            return null; // an unusable path shape is not a directory to look in
        }

        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, $"{toolName}{extension}");
        return fs.FileExists(candidate) ? candidate : null;
    }

    /// <summary>
    /// The first probe directory holding <b>both</b> tools. Both, because a dump this product cannot verify is a
    /// failed backup — so a directory with only <c>pg_dump</c> in it cannot serve a backup and is not a candidate.
    /// </summary>
    private static (string PgDump, string PgRestore)? LocatePair(FileSystem fs)
    {
        var executableSuffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

        foreach (var directory in ProbeDirectories(fs))
        {
            var pgDump = Path.Combine(directory, $"{PgDumpName}{executableSuffix}");
            var pgRestore = Path.Combine(directory, $"{PgRestoreName}{executableSuffix}");

            if (fs.FileExists(pgDump) && fs.FileExists(pgRestore))
            {
                return (pgDump, pgRestore);
            }
        }

        return null;
    }

    /// <summary>
    /// Where to look, in priority order: beside the application, then <c>PATH</c>, then the well-known
    /// per-version installation directories of each platform.
    /// </summary>
    private static IEnumerable<string> ProbeDirectories(FileSystem fs)
    {
        // 1. Beside the application. `LocalInstallPaths` resolves against `AppContext.BaseDirectory` rather than
        //    the CWD, which is what makes this work for a Windows service (whose CWD is System32) — and
        //    `postgres/bin` is exactly where the server installer lays the bundled cluster down, so the Windows
        //    product keeps working even if it ever stops writing the configuration key.
        yield return LocalInstallPaths.Resolve(Path.Combine("postgres", "bin"));
        yield return LocalInstallPaths.Resolve("postgres");
        yield return AppContext.BaseDirectory;

        // 2. PATH. This is the one that makes the container work: `api/Dockerfile` installs the PostgreSQL client
        //    into the image, so the tools are on PATH with nothing configured anywhere.
        foreach (var entry in PathEntries())
        {
            yield return entry;
        }

        // 3. Well-known installation roots, newest version first.
        foreach (var directory in VersionedInstallDirectories(fs))
        {
            yield return directory;
        }

        foreach (var directory in FixedInstallDirectories())
        {
            yield return directory;
        }
    }

    private static IEnumerable<string> PathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim().Trim('"');
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>
    /// The per-version roots — <c>C:\Program Files\PostgreSQL\17\bin</c>, <c>/usr/lib/postgresql/16/bin</c> —
    /// enumerated and returned <b>newest first</b>. Sorted on the leading integer of the directory name rather
    /// than lexicographically, or « 9 » would outrank « 16 ».
    /// </summary>
    private static IEnumerable<string> VersionedInstallDirectories(FileSystem fs)
    {
        var roots = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(ProgramFiles(Environment.SpecialFolder.ProgramFiles), "PostgreSQL"),
                Path.Combine(ProgramFiles(Environment.SpecialFolder.ProgramFilesX86), "PostgreSQL")
            }
            : new[] { "/usr/lib/postgresql", "/usr/local/pgsql", "/opt/homebrew/opt" };

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var versioned = SafeEnumerateDirectories(fs, root)
                .Select(directory => (Directory: directory, Version: LeadingVersion(Path.GetFileName(directory))))
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => candidate.Directory, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in versioned)
            {
                yield return Path.Combine(candidate.Directory, "bin");
                yield return candidate.Directory;
            }
        }
    }

    /// <summary>
    /// Lists <paramref name="root"/>'s children, or nothing at all if that cannot be done.
    ///
    /// <para>The guard is <b>here, at the point of use</b>, and not only inside
    /// <see cref="FileSystem.Real"/>: discovery is a best-effort search over directories that mostly do not exist,
    /// so a probe root that is missing, unlistable or on a dead network drive must cost nothing — whatever
    /// <see cref="FileSystem"/> implementation is in play. Putting it only in the production lambda made that a
    /// property of one caller rather than of the search.</para>
    /// </summary>
    private static IReadOnlyList<string> SafeEnumerateDirectories(FileSystem fs, string root)
    {
        try
        {
            return fs.EnumerateDirectories(root).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string ProgramFiles(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The leading integer of a version-shaped directory name (<c>16</c>, <c>16.9</c>, <c>postgresql@16</c>);
    /// <c>-1</c> when there is none, so an unrelated sibling folder sorts last rather than throwing.
    /// </summary>
    private static int LeadingVersion(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return -1;
        }

        var digits = new string(directoryName.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var version) ? version : -1;
    }

    private static IEnumerable<string> FixedInstallDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield break; // Windows installs are all versioned; there is no conventional flat location.
        }

        yield return "/usr/bin";
        yield return "/usr/local/bin";
        yield return "/opt/homebrew/bin";
    }
}
