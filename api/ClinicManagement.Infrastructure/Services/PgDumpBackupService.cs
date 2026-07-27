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

    public PgDumpBackupService(
        IConfiguration configuration,
        ILogger<PgDumpBackupService> logger,
        DirectoryAclHardener aclHardener)
    {
        _configuration = configuration;
        _logger = logger;
        _aclHardener = aclHardener;
    }

    public async Task<BackupResultDto> CreateBackupAsync(string? destinationFolder, CancellationToken cancellationToken = default)
    {
        // --- Resolve inputs & fail loud on anything missing (no partial/silent backup) ---
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion à la base de données est introuvable (ConnectionStrings:DefaultConnection).");
        }

        var pgDumpPath = _configuration["Backup:PgDumpPath"];
        if (string.IsNullOrWhiteSpace(pgDumpPath) || !File.Exists(pgDumpPath))
        {
            throw new InvalidOperationException(
                "L'outil pg_dump est introuvable. Vérifiez le paramètre 'Backup:PgDumpPath' (chemin vers pg_dump.exe fourni avec PostgreSQL).");
        }

        var destinationRoot = string.IsNullOrWhiteSpace(destinationFolder)
            ? _configuration["Backup:DefaultDestination"]
            : destinationFolder;
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new InvalidOperationException(
                "Aucun dossier de destination pour la sauvegarde. Indiquez un dossier ou configurez 'Backup:DefaultDestination'.");
        }

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
        var baseFolder = Path.Combine(destinationRoot, $"clinic-backup-{timestamp:yyyyMMdd-HHmmss}");
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
            string? warning = null;
            var driveType = BackupProtectionPolicy.ResolveDriveType(backupFolder);

            if (BackupProtectionPolicy.CanProtect(driveType))
            {
                if (_aclHardener.Harden(backupFolder) == AclHardeningOutcome.SkippedNotWindows)
                {
                    warning = BackupProtectionPolicy.UnprotectableDestinationWarning;
                }
            }
            else
            {
                warning = BackupProtectionPolicy.UnprotectableDestinationWarning;
                _logger.LogWarning(
                    "Backup destination {Folder} is on a {DriveType} drive — NTFS permissions cannot be " +
                    "relied on, so the backup is not access-restricted.",
                    backupFolder,
                    driveType);
            }

            // --- (1) Database dump (R-3: DB first) ---
            var dumpFile = Path.Combine(backupFolder, "database.dump");
            await RunPgDumpAsync(pgDumpPath, conn, dumpFile, cancellationToken);

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
            _logger.LogInformation("Backup completed at {Folder} ({Size} bytes).", backupFolder, sizeBytes);

            return new BackupResultDto
            {
                DestinationPath = backupFolder,
                SizeBytes = sizeBytes,
                TimestampUtc = timestamp,
                Warning = warning
            };
        }
        catch
        {
            TryDeleteDirectory(backupFolder);
            throw;
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
