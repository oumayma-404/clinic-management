using System.Diagnostics;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IBackupService"/> implementation for Local (offline) installs (US-8 / FR-G). Produces a
/// consistent backup by (1) dumping the PostgreSQL database with the bundled <c>pg_dump.exe</c> in custom
/// format and (2) recursively copying the file-storage folder — both into a timestamped
/// <c>clinic-backup-&lt;yyyyMMdd-HHmmss&gt;</c> subfolder of the destination (R-3: DB first, then files).
/// </summary>
/// <remarks>
/// This is the first <see cref="Process"/> shell-out in the codebase (R-7): the child is launched with an
/// argument <em>list</em> (never a shell string, so no injection surface), the DB password is passed via the
/// <c>PGPASSWORD</c> environment variable (never on the command line / in logs), and the run is bounded by a
/// timeout that kills the process tree. Every foreseeable failure — missing <c>pg_dump</c>, an unwritable
/// destination, insufficient disk space, a non-zero <c>pg_dump</c> exit — is thrown as an
/// <see cref="InvalidOperationException"/> with a clear operator-facing message (AC-8.2 / AC-8.3); the
/// command handler maps it to a <c>Result.Failure</c>, so a backup never fails silently.
/// </remarks>
public sealed class PgDumpBackupService : IBackupService
{
    private const long FreeSpaceMarginBytes = 128L * 1024 * 1024; // headroom above the file-copy estimate
    private const int DefaultTimeoutSeconds = 1800; // 30 min — a large DB dump can take a while

    // Win32 disk-full error codes (low word of HResult) — map an IOException during the copy to a
    // distinct "disk full" message even if the pre-check passed (files grew, or another writer ate space).
    private const int ErrorDiskFull = 0x70;
    private const int ErrorHandleDiskFull = 0x27;

    private readonly IConfiguration _configuration;
    private readonly ILogger<PgDumpBackupService> _logger;
    private readonly DirectoryAclHardener _aclHardener;
    private readonly PostgresToolLocator.FileSystem _toolFileSystem;

    /// <summary>The DI constructor. Tool discovery reads the real filesystem.</summary>
    public PgDumpBackupService(
        IConfiguration configuration,
        ILogger<PgDumpBackupService> logger,
        DirectoryAclHardener aclHardener)
        : this(configuration, logger, aclHardener, PostgresToolLocator.FileSystem.Real)
    {
    }

    /// <summary>
    /// Test seam for <b>tool discovery only</b>.
    ///
    /// <para>⚠️ It exists because discovery made « this machine has no <c>pg_dump</c> » untestable through
    /// configuration alone: pointing <c>Backup:PgDumpPath</c> at a non-existent file now correctly falls through
    /// to PATH, and both a developer Windows box and GitHub's ubuntu runner <i>have</i> the client installed — so
    /// the refusal test would have passed or failed depending on the machine, which is worse than not having it.
    /// A separate constructor rather than an optional parameter because the DI container picks the greediest
    /// constructor it can satisfy, and a defaulted one it cannot resolve is a startup failure.</para>
    /// </summary>
    public PgDumpBackupService(
        IConfiguration configuration,
        ILogger<PgDumpBackupService> logger,
        DirectoryAclHardener aclHardener,
        PostgresToolLocator.FileSystem toolFileSystem)
    {
        _configuration = configuration;
        _logger = logger;
        _aclHardener = aclHardener;
        _toolFileSystem = toolFileSystem;
    }

    public async Task<BackupResultDto> CreateBackupAsync(string? destinationFolder, CancellationToken cancellationToken = default)
    {
        // --- Resolve inputs & fail loud on anything missing (no partial/silent backup) ---
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion à la base de données est introuvable (ConnectionStrings:DefaultConnection).");
        }

        // Resolved, not configured: `PostgresToolLocator` looks beside the application, on PATH and in the
        // well-known per-version install directories before giving up, so every deployment that *ships* the
        // client tools works with no operator setting at all. An explicit `Backup:PgDumpPath` still wins.
        var pgDumpPath = PostgresToolLocator.LocatePgDump(_configuration, _toolFileSystem);
        if (pgDumpPath == null)
        {
            throw new InvalidOperationException(
                "L'outil pg_dump est introuvable sur ce serveur. Il est fourni avec PostgreSQL ; installez le client "
                + "PostgreSQL ou indiquez son emplacement dans le paramètre 'Backup:PgDumpPath'.");
        }

        // L4b — resolved, never refused. This used to throw when both the argument and
        // `Backup:DefaultDestination` were empty, and the installer writes that key as `""` — so the documented
        // path ("leave the field blank to use the server's default folder") failed on every fresh install.
        var destinationRoot = ResolveDestinationRoot(destinationFolder);

        var conn = new NpgsqlConnectionStringBuilder(connectionString);
        var filesPath = ResolveFileStorageBasePath();

        // --- Pre-checks: destination writable + enough free space (distinct errors, AC-8.2/8.3) ---
        // Free-space estimate now factors in the database dump size (Finding 9), not just the file copy,
        // so a large DB but small file store still fails with the recognizable "espace disque insuffisant"
        // message rather than mid-dump via the generic pg_dump error path.
        EnsureDestinationWritable(destinationRoot);
        var dbSizeEstimate = await TryGetDatabaseSizeBytesAsync(conn, cancellationToken);
        EnsureSufficientFreeSpace(destinationRoot, filesPath, dbSizeEstimate);

        // --- Create a UNIQUE timestamped backup folder ---
        // The name has whole-second granularity, so two backups in the same second would otherwise resolve
        // to the same folder and clobber each other (Finding 8). Disambiguate with a counter suffix.
        var timestamp = DateTime.UtcNow;
        // The prefix comes from the shared constant the pruner matches on: a literal here is how a renamed
        // folder becomes invisible to retention and the destination grows for ever.
        var baseFolder = Path.Combine(destinationRoot, $"{BackupFolderPrefix}{timestamp:yyyyMMdd-HHmmss}");
        var backupFolder = baseFolder;
        var attempt = 1;
        while (Directory.Exists(backupFolder))
        {
            backupFolder = $"{baseFolder}-{++attempt}";
        }
        Directory.CreateDirectory(backupFolder);

        // If the dump or the file copy fails (or is cancelled), remove the partial folder before rethrowing
        // so an operator never sees a half-written backup that looks complete and restores from it
        // (Finding 1 — the opposite of the "no silent partial success" intent, AC-8.2/8.3).
        try
        {
            // --- (0) Restrict the folder BEFORE anything is written into it (US-14 / AC-14.2) ---
            // A backup is a full dump of every patient record plus a copy of the entire file store, so it
            // gets the same posture as the live data. Hardening the folder AFTER writing would leave a window
            // in which the dump sits readable by every local account — which is exactly the exposure the
            // install-level hardening closes, reopened by one click.
            //
            // On a destination whose ACLs cannot be relied on (USB stick, network share) we do not pretend:
            // the backup proceeds and the admin is told plainly (AC-14.3). An ACL failure on a local fixed
            // disk, by contrast, throws — and the catch below deletes the partial folder (AC-14.4), so a
            // backup is never left both incomplete and unprotected.
            // Two warnings can apply at once (an unprotectable destination that is also the live volume), so they
            // accumulate rather than overwrite — a single `warning = ...` silently dropped whichever came first.
            var warnings = new List<string>();
            var driveType = BackupProtectionPolicy.ResolveDriveType(backupFolder);

            if (BackupProtectionPolicy.CanProtect(driveType))
            {
                if (_aclHardener.Harden(backupFolder) == AclHardeningOutcome.SkippedNotWindows)
                {
                    warnings.Add(BackupProtectionPolicy.UnprotectableDestinationWarning);
                }
            }
            else
            {
                warnings.Add(BackupProtectionPolicy.UnprotectableDestinationWarning);
                _logger.LogWarning(
                    "Backup destination {Folder} is on a {DriveType} drive — NTFS permissions cannot be " +
                    "relied on, so the backup is not access-restricted.",
                    backupFolder,
                    driveType);
            }

            // L4b — the same-volume warning. A backup on the disk that dies with the database is not a backup,
            // and the default destination is necessarily install-relative, so this is the *normal* case on a
            // fresh install rather than an exotic misconfiguration. Said out loud, prominently, because it is the
            // one thing about a backup an owner can act on in five minutes (plug in a USB disk).
            if (IsOnTheSameVolumeAsTheLiveData(backupFolder, filesPath))
            {
                warnings.Add(SameVolumeWarning);
                _logger.LogWarning(
                    "Backup destination {Folder} is on the same volume as the live data — a single disk failure "
                        + "would take both.", backupFolder);
            }

            // --- (1) Database dump (R-3: DB first) ---
            var dumpFile = Path.Combine(backupFolder, "database.dump");
            await RunPgDumpAsync(pgDumpPath, conn, dumpFile, cancellationToken);

            // --- (1b) L4c — VERIFY the dump is readable, before anything reports success ---
            // A failed verification is a failed backup: the catch below deletes the partial folder, so an
            // unreadable dump never sits in the destination looking like protection.
            var verifiedObjectCount = await VerifyDumpAsync(pgDumpPath, dumpFile, cancellationToken);

            // --- (2) File-storage copy ---
            if (Directory.Exists(filesPath))
            {
                try
                {
                    CopyDirectory(filesPath, Path.Combine(backupFolder, "files"));
                }
                catch (IOException ex) when (IsDiskFull(ex))
                {
                    throw new InvalidOperationException(
                        "Espace disque insuffisant pour copier les fichiers pendant la sauvegarde.", ex);
                }
            }
            else
            {
                _logger.LogInformation("File-storage folder {Path} does not exist yet — backing up the database only.", filesPath);
            }

            var sizeBytes = DirectorySize(backupFolder);
            _logger.LogInformation(
                "Backup completed at {Folder} ({Size} bytes, {Objects} objects verified).",
                backupFolder, sizeBytes, verifiedObjectCount);

            return new BackupResultDto
            {
                DestinationPath = backupFolder,
                SizeBytes = sizeBytes,
                TimestampUtc = timestamp,
                VerifiedObjectCount = verifiedObjectCount,
                Warning = warnings.Count == 0 ? null : string.Join(" ", warnings)
            };
        }
        catch
        {
            TryDeleteDirectory(backupFolder);
            throw;
        }
    }

    /// <summary>
    /// The install-relative default destination (L4b). A sibling of <c>Files/</c> and <c>logs/</c>, resolved
    /// through <see cref="LocalInstallPaths"/> because a Windows service's CWD is <c>System32</c> — a relative
    /// "Backups" would otherwise write patient data into a system folder.
    /// </summary>
    private const string DefaultDestinationFolderName = "Backups";

    /// <summary>The prefix the pruner matches on. Shared with the folder writer so the two cannot drift.</summary>
    private const string BackupFolderPrefix = "clinic-backup-";

    internal const string SameVolumeWarning =
        "La sauvegarde est enregistrée sur le même disque que les données de la clinique : une panne de ce "
        + "disque ferait perdre les deux. Choisissez un disque externe ou un dossier réseau.";

    /// <inheritdoc />
    public string ResolveDestinationRoot(string? destinationFolder)
    {
        if (!string.IsNullOrWhiteSpace(destinationFolder))
        {
            return destinationFolder.Trim();
        }

        var configured = _configuration["Backup:DefaultDestination"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        // Fall back rather than throw. The installer writes `Backup:DefaultDestination` as an empty string, so
        // the *documented* default path failed on every fresh install with « Aucun dossier de destination » —
        // a message about a setting the operator was told not to fill in.
        return LocalInstallPaths.Resolve(DefaultDestinationFolderName);
    }

    /// <inheritdoc />
    public Task<int> PruneOldBackupsAsync(
        string? destinationFolder, int keepCount, CancellationToken cancellationToken = default)
    {
        var root = ResolveDestinationRoot(destinationFolder);
        if (!Directory.Exists(root))
        {
            return Task.FromResult(0);
        }

        // Only OUR folders. An operator's own « Sauvegardes 2025 » sitting in the same destination is not the
        // pruner's business, and a retention pass that deletes an unrecognised folder is unrecoverable.
        var ours = Directory.EnumerateDirectories(root, $"{BackupFolderPrefix}*")
            .Select(path => new DirectoryInfo(path))
            // Oldest first, by NAME. The name embeds a UTC timestamp (`yyyyMMdd-HHmmss[-N]`), which sorts
            // lexicographically in chronological order and — unlike CreationTimeUtc — survives being copied to
            // another disk, which is precisely what an operator does with backups.
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        var keep = Math.Max(1, keepCount);

        // Never empty the folder. The floor is one surviving backup whatever the count says — an operator who
        // types 0, or a corrupt setting, must not be able to leave the practice with nothing.
        var deletable = Math.Min(Math.Max(0, ours.Count - keep), Math.Max(0, ours.Count - 1));

        var deleted = 0;
        for (var i = 0; i < deletable; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ours[i].Delete(recursive: true);
                deleted++;
                _logger.LogInformation("Pruned old backup folder {Folder}.", ours[i].FullName);
            }
            catch (Exception ex)
            {
                // Skipped, not fatal: a locked folder must not fail the backup that just succeeded.
                _logger.LogWarning(ex, "Could not prune the backup folder {Folder}.", ours[i].FullName);
            }
        }

        return Task.FromResult(deleted);
    }

    /// <summary>
    /// L4c — reads the dump's table of contents back with <c>pg_restore --list</c> and returns how many objects
    /// it names. An empty TOC, an unreadable file or a non-zero exit is a <b>failed backup</b>.
    ///
    /// <para><c>pg_restore --list</c> and not a trial restore: it is fast, read-only and needs no target
    /// database, so it can run on every backup — which is the only kind of verification that gets run.</para>
    ///
    /// <para><c>pg_restore.exe</c> is looked for beside <c>pg_dump.exe</c> (they ship together in
    /// PostgreSQL's <c>bin/</c>) with an explicit <c>Backup:PgRestorePath</c> override. If it genuinely is not
    /// there the backup <b>fails</b> rather than reporting an unverified success: a success that means less than
    /// it says is what L4c exists to remove.</para>
    /// </summary>
    private async Task<int> VerifyDumpAsync(string pgDumpPath, string dumpFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(dumpFile) || new FileInfo(dumpFile).Length == 0)
        {
            throw new InvalidOperationException(
                "La sauvegarde de la base de données est vide — le fichier database.dump ne contient rien.");
        }

        var pgRestorePath = ResolvePgRestorePath(pgDumpPath);
        if (pgRestorePath == null)
        {
            throw new InvalidOperationException(
                "L'outil pg_restore est introuvable, la sauvegarde n'a donc pas pu être vérifiée. Vérifiez le "
                + "paramètre 'Backup:PgRestorePath' (pg_restore.exe est fourni avec PostgreSQL, à côté de pg_dump.exe).");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pgRestorePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--list");
        startInfo.ArgumentList.Add(dumpFile);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Impossible de vérifier la sauvegarde (pg_restore n'a pas pu être lancé) : {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        // The TOC read is orders of magnitude faster than the dump, so it gets its own short bound rather than
        // the dump's 30 minutes: a pg_restore that hangs here has nothing left to be waiting for.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(VerifyTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"La vérification de la sauvegarde a dépassé le délai de {VerifyTimeoutSeconds}s.");
            }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"La sauvegarde créée est illisible (pg_restore code {process.ExitCode}). {stderr}".Trim());
        }

        var objectCount = CountTocEntries(stdout);
        if (objectCount == 0)
        {
            throw new InvalidOperationException(
                "La sauvegarde créée ne contient aucun objet — elle est inutilisable pour une restauration.");
        }

        return objectCount;
    }

    private const int VerifyTimeoutSeconds = 120;

    /// <summary>
    /// Counts the TOC entries in <c>pg_restore --list</c> output: every non-empty line that is not a
    /// <c>;</c> comment. Deliberately a count of *lines* rather than a parse — the shape of a TOC line is
    /// PostgreSQL's business and varies by version, while « is it empty and roughly how big is it » is all the
    /// disaster-detection here needs.
    /// </summary>
    private static int CountTocEntries(string listOutput) =>
        listOutput.Split('\n')
            .Select(line => line.Trim())
            .Count(line => line.Length > 0 && !line.StartsWith(';'));

    /// <summary>
    /// <c>pg_restore</c>: the explicit override, else the sibling of the <c>pg_dump</c> in hand, else a discovered
    /// copy — all through the one <see cref="PostgresToolLocator"/> the <c>restore-backup</c> verb also uses, so
    /// the two cannot disagree about where this machine's PostgreSQL tools are. Null when there are none, which
    /// the caller turns into a failed backup.
    /// </summary>
    private string? ResolvePgRestorePath(string pgDumpPath) =>
        PostgresToolLocator.LocatePgRestore(_configuration, pgDumpPath, _toolFileSystem);

    /// <summary>
    /// Is the destination on the same volume as the live data? Compared on the path <b>root</b>, which is what a
    /// disk failure takes. Unknown (a UNC share, an unusual path shape) reads as « not the same volume »: a
    /// network destination is genuinely elsewhere, and warning about it would train the operator to ignore the
    /// warning that matters.
    /// </summary>
    private static bool IsOnTheSameVolumeAsTheLiveData(string destination, string filesPath)
    {
        try
        {
            var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destination));
            var liveRoot = Path.GetPathRoot(Path.GetFullPath(filesPath));
            return !string.IsNullOrEmpty(destinationRoot)
                   && !string.IsNullOrEmpty(liveRoot)
                   && string.Equals(destinationRoot, liveRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Best-effort estimate of the database size (via <c>pg_database_size</c>) so the free-space pre-check
    /// accounts for the dump, not only the file-storage copy (Finding 9). The custom-format dump is
    /// compressed, so the live DB size is a conservative over-estimate — fine for a pre-check. Any failure
    /// (DB briefly unreachable, permissions) returns 0, leaving the file-copy estimate + fixed margin.
    /// </summary>
    private static async Task<long> TryGetDatabaseSizeBytesAsync(NpgsqlConnectionStringBuilder conn, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(conn.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_database_size(current_database())", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is long size ? size : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up the partial backup folder {Folder} after a failed backup.", path);
        }
    }

    private async Task RunPgDumpAsync(string pgDumpPath, NpgsqlConnectionStringBuilder conn, string dumpFile, CancellationToken cancellationToken)
    {
        var timeoutSeconds = _configuration.GetValue<int?>("Backup:TimeoutSeconds") ?? DefaultTimeoutSeconds;

        var startInfo = new ProcessStartInfo
        {
            FileName = pgDumpPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        // Argument LIST, never a shell string (R-7) — no quoting/injection surface.
        startInfo.ArgumentList.Add("--host"); startInfo.ArgumentList.Add(conn.Host ?? "localhost");
        startInfo.ArgumentList.Add("--port"); startInfo.ArgumentList.Add((conn.Port == 0 ? 5432 : conn.Port).ToString());
        startInfo.ArgumentList.Add("--username"); startInfo.ArgumentList.Add(conn.Username ?? string.Empty);
        startInfo.ArgumentList.Add("--dbname"); startInfo.ArgumentList.Add(conn.Database ?? string.Empty);
        startInfo.ArgumentList.Add("--format"); startInfo.ArgumentList.Add("custom"); // -Fc → restorable with pg_restore
        startInfo.ArgumentList.Add("--file"); startInfo.ArgumentList.Add(dumpFile);
        startInfo.ArgumentList.Add("--no-password"); // never prompt interactively — fail fast if auth is needed
        // Password via env, never on the command line / in logs.
        startInfo.Environment["PGPASSWORD"] = conn.Password ?? string.Empty;

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Impossible de lancer pg_dump : {ex.Message}", ex);
        }

        // Read stderr concurrently so a full pipe buffer can't deadlock the wait.
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"La sauvegarde de la base de données a dépassé le délai de {timeoutSeconds}s et a été interrompue.");
            }
            throw; // caller-requested cancellation — propagate
        }

        var stderr = (await stderrTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Échec de la sauvegarde de la base de données (pg_dump code {process.ExitCode}). {stderr}".Trim());
        }
    }

    /// <summary>
    /// Resolves <c>FileStorage:BasePath</c> to an absolute path against the install directory (R-6) — the
    /// same resolution the Local-mode storage registration uses — so the backup copies exactly the folder
    /// the storage writes to, whether launched from a console or as a Windows service.
    /// </summary>
    private string ResolveFileStorageBasePath()
    {
        var basePath = _configuration["FileStorage:BasePath"];
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = "Files";
        }
        return LocalInstallPaths.Resolve(basePath);
    }

    private static void EnsureDestinationWritable(string destinationRoot)
    {
        try
        {
            Directory.CreateDirectory(destinationRoot);
            var probe = Path.Combine(destinationRoot, $".backup-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"Le dossier de destination n'est pas accessible en écriture : {destinationRoot}", ex);
        }
    }

    private static void EnsureSufficientFreeSpace(string destinationRoot, string filesPath, long dbSizeBytes)
    {
        long estimated;
        try
        {
            estimated = DirectorySize(filesPath) + dbSizeBytes + FreeSpaceMarginBytes;
        }
        catch
        {
            estimated = dbSizeBytes + FreeSpaceMarginBytes; // couldn't size the files — still require the DB estimate + margin
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destinationRoot));
            if (string.IsNullOrEmpty(root))
            {
                return; // best-effort — can't identify the drive (e.g. some UNC paths)
            }

            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < estimated)
            {
                throw new InvalidOperationException(
                    $"Espace disque insuffisant sur '{root}' pour la sauvegarde " +
                    $"(disponible : {drive.AvailableFreeSpace / (1024 * 1024)} Mo, requis : ~{estimated / (1024 * 1024)} Mo).");
            }
        }
        catch (InvalidOperationException)
        {
            throw; // our own disk-full error — surface it
        }
        catch
        {
            // Any other DriveInfo problem (unsupported path shape) — skip the pre-check rather than
            // blocking a legitimate backup; a genuine disk-full still surfaces during the copy.
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            total += new FileInfo(file).Length;
        }
        return total;
    }

    private static bool IsDiskFull(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is ErrorDiskFull or ErrorHandleDiskFull;
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill the pg_dump process after a timeout.");
        }
    }
}
