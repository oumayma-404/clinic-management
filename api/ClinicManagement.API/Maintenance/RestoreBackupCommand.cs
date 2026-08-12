using System.Diagnostics;
using System.Net.NetworkInformation;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Npgsql;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Restores a backup folder produced by « Sauvegarder maintenant » or the nightly job (L4g).
///
///   ClinicManagement.API.exe restore-backup &lt;dossier&gt; [--force]
///
/// <para><b>Why a console verb and not an endpoint.</b> A restore runs with the application <b>stopped</b> — it
/// drops and recreates every table the application is holding open — so an HTTP endpoint inside the app being
/// replaced is the wrong shape entirely. It joins <c>reset-admin-password</c>, <c>reconcile-money</c> and
/// <c>verify-schema</c>, and it is the answer to <c>packaging/README.md</c>'s « There is no in-app restore. »</para>
///
/// <para><b>The order of operations is the whole design</b>, and it is: validate everything, refuse if the app is
/// running, take a safety dump, and only then touch the live database. Every refusal happens before anything is
/// destroyed, so a mistyped folder or a dump from another product costs nothing.</para>
///
/// <para>Exit codes match the other report verbs so a script can treat them identically:
/// <c>0</c> restored · <c>1</c> refused or failed.</para>
/// </summary>
public static class RestoreBackupCommand
{
    public const string CommandName = "restore-backup";

    /// <summary>The file the backup writer produces. A folder without it is not a backup of this product.</summary>
    private const string DumpFileName = "database.dump";

    /// <summary>The file-storage copy inside a backup folder.</summary>
    private const string FilesFolderName = "files";

    /// <summary>
    /// The safety dump's folder prefix — deliberately <b>not</b> <c>clinic-backup-</c>. The retention pruner
    /// matches only its own prefix, so a pre-restore snapshot can never be deleted by a later nightly run: it is
    /// the one copy whose whole purpose is to survive a decision the operator may regret.
    /// </summary>
    private const string SafetyDumpPrefix = "clinic-pre-restore-";

    private const int RestoreTimeoutSeconds = 3600;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            var force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(folder))
            {
                Console.Error.WriteLine($"Usage : ClinicManagement.API.exe {CommandName} <dossier> [--force]");
                Console.Error.WriteLine(
                    "  <dossier>  le dossier « clinic-backup-... » à restaurer (celui qui contient database.dump)");
                Console.Error.WriteLine(
                    "  --force    autorise la restauration des fichiers par-dessus un dossier Files non vide");
                return 1;
            }

            var configuration = InstallConfiguration.BuildForConsoleVerb();

            // ⚠️ The ONLY verb of this family that keeps a deployment-profile gate, and deliberately so (M3
            // ungated its three siblings). Two reasons, and the second is the load-bearing one:
            //   1. pg_restore is not on the box outside a local install;
            //   2. step 2 below — « refuse while the application is listening » — is this verb's whole safety
            //      interlock, and it is enforced by looking for a listener on THIS machine. In a container the
            //      API listens in a sibling container, so the check finds nothing and PASSES, silently, while
            //      `pg_restore --clean --if-exists` drops every table out from under a live application.
            // A gate that refuses is the honest answer until a restore path exists that can stop the app first.
            var profile = DeploymentProfile.Resolve(configuration);
            if (!profile.HasLocalDbTooling)
            {
                Console.Error.WriteLine(
                    "Cet utilitaire de restauration ne s'applique qu'à une installation locale "
                    + $"(profil de déploiement : {profile.Kind}) : pg_restore n'y est pas installé, et surtout "
                    + "le contrôle « refuser tant que l'application écoute » ne peut pas être appliqué depuis "
                    + "un conteneur, où l'API écoute dans un autre conteneur.");
                return 1;
            }

            // ── 1. Validate the source, before anything else ─────────────────────────────────────────────────
            var source = Path.GetFullPath(folder);
            var dumpFile = Path.Combine(source, DumpFileName);

            if (!Directory.Exists(source))
            {
                Console.Error.WriteLine($"Dossier introuvable : {source}");
                return 1;
            }

            if (!File.Exists(dumpFile) || new FileInfo(dumpFile).Length == 0)
            {
                Console.Error.WriteLine(
                    $"Ce dossier ne contient pas de sauvegarde exploitable : {DumpFileName} est absent ou vide.");
                Console.Error.WriteLine($"  Attendu : {dumpFile}");
                return 1;
            }

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine(
                    "La chaîne de connexion à la base de données est introuvable (ConnectionStrings:DefaultConnection).");
                return 1;
            }

            // Credentials come from the install's own configuration — exactly as the backup writer reads them —
            // so no password is ever typed on a command line or prompted for (the spec's requirement). On a
            // packaged install that value was written by the installer from the encrypted `.local/db-credentials`.
            var conn = new NpgsqlConnectionStringBuilder(connectionString);

            var (pgRestore, pgDump) = ResolveTools(configuration);
            if (pgRestore == null || pgDump == null)
            {
                Console.Error.WriteLine(
                    "pg_restore ou pg_dump est introuvable sur ce serveur. Les deux outils sont fournis avec "
                    + "PostgreSQL, dans le même dossier : installez le client PostgreSQL ou indiquez son "
                    + "emplacement dans 'Backup:PgDumpPath'.");
                return 1;
            }

            // The same proof the backup writer requires before calling a dump a success: a readable, non-empty
            // table of contents. Doing it here too is what makes « validate before touching anything » true for a
            // folder produced before L4c existed, or copied off a failing disk.
            var objectCount = await CountDumpObjectsAsync(pgRestore, dumpFile, cancellationToken);
            if (objectCount <= 0)
            {
                Console.Error.WriteLine(
                    "La sauvegarde est illisible ou ne contient aucun objet — restauration annulée. "
                    + "Rien n'a été modifié.");
                return 1;
            }

            // ── 2. Refuse while the application is running ───────────────────────────────────────────────────
            // A restore drops every table the app holds open: pg_restore --clean would fail halfway and leave the
            // database in neither state. Detected by asking whether anything is listening on the app's own ports,
            // which works whether it runs as a Windows service, from a console, or under the desktop shell.
            var busyPort = FindListeningAppPort(configuration);
            if (busyPort is int port)
            {
                Console.Error.WriteLine(
                    $"L'application semble en cours d'exécution (le port {port} est utilisé). "
                    + "Arrêtez le service « Clinic Management » puis relancez cette commande.");
                Console.Error.WriteLine("  Exemple : sc stop ClinicManagementApi");
                return 1;
            }

            var filesTarget = ResolveFileStorageBasePath(configuration);
            var filesSource = Path.Combine(source, FilesFolderName);
            var willRestoreFiles = Directory.Exists(filesSource);

            // Refuse a file restore into a non-empty target unless forced. Copying over live documents is the one
            // step of a restore that destroys data the dump does not contain, so it is opt-in rather than implied.
            if (willRestoreFiles && !force && Directory.Exists(filesTarget) && HasAnyFile(filesTarget))
            {
                Console.Error.WriteLine(
                    $"Le dossier des fichiers n'est pas vide : {filesTarget}");
                Console.Error.WriteLine(
                    "Les documents actuels seraient écrasés. Relancez avec --force si c'est bien ce que vous voulez, "
                    + "ou déplacez ce dossier d'abord. Rien n'a été modifié.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("=== Restauration d'une sauvegarde ===");
            Console.WriteLine($"Source              : {source}");
            Console.WriteLine($"Objets dans le dump : {objectCount}");
            Console.WriteLine($"Base de données     : {conn.Database} sur {conn.Host}:{(conn.Port == 0 ? 5432 : conn.Port)}");
            Console.WriteLine($"Fichiers            : {(willRestoreFiles ? filesTarget : "(aucun dossier files dans la sauvegarde)")}");
            Console.WriteLine();

            // ── 3. Safety dump of the CURRENT state, before overwriting it ───────────────────────────────────
            var safetyFolder = Path.Combine(
                ResolveBackupRoot(configuration), $"{SafetyDumpPrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            Console.WriteLine("Sauvegarde de sécurité de l'état actuel...");
            var safetyDump = await TryWriteSafetyDumpAsync(pgDump, conn, safetyFolder, cancellationToken);
            if (safetyDump == null)
            {
                Console.Error.WriteLine(
                    "La sauvegarde de sécurité a échoué — restauration annulée. Rien n'a été modifié.");
                Console.Error.WriteLine(
                    "Restaurer sans filet reviendrait à remplacer des données qu'on ne peut plus récupérer.");
                return 1;
            }

            Console.WriteLine($"  → {safetyDump}");
            Console.WriteLine();

            // ── 4. The restore itself ────────────────────────────────────────────────────────────────────────
            Console.WriteLine("Restauration de la base de données...");
            var restored = await RunPgRestoreAsync(pgRestore, conn, dumpFile, cancellationToken);
            if (!restored)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("La restauration de la base a échoué.");
                Console.Error.WriteLine($"L'état précédent est dans : {safetyDump}");
                return 1;
            }

            Console.WriteLine("  → base de données restaurée.");

            // ── 5. Files ─────────────────────────────────────────────────────────────────────────────────────
            if (willRestoreFiles)
            {
                Console.WriteLine("Restauration des fichiers...");
                Directory.CreateDirectory(filesTarget);
                CopyDirectory(filesSource, filesTarget);
                Console.WriteLine($"  → {filesTarget}");
            }

            // ── 6. Invalidate every live session ────────────────────────────────────────────────────────────
            // The app issues stateless JWTs carrying a TokenVersion. After a restore, a token minted against the
            // NEWER state is still cryptographically valid but describes a user, role or clinic that the restored
            // database may no longer agree with. Bumping every user's version is what makes « restore » mean
            // « everyone logs in again » rather than « some sessions keep operating on assumptions that no longer
            // hold ».
            var invalidated = await InvalidateSessionsAsync(conn, cancellationToken);
            Console.WriteLine($"Sessions invalidées : {invalidated} compte(s) — chacun devra se reconnecter.");

            Console.WriteLine();
            Console.WriteLine("Restauration terminée.");
            Console.WriteLine("Étapes suivantes :");
            Console.WriteLine("  1. Redémarrez le service « Clinic Management ».");
            Console.WriteLine("  2. Connectez-vous et vérifiez un patient, une facture et un document enregistré.");
            Console.WriteLine($"  3. Conservez {safetyDump} jusqu'à ce que la vérification soit faite.");
            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Échec de la restauration : {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------ tooling & config

    /// <summary>
    /// <c>pg_restore</c> and <c>pg_dump</c>, through the <b>shared</b> <see cref="PostgresToolLocator"/> — the same
    /// object <c>PgDumpBackupService</c> resolves them with, so the verb cannot find a different PostgreSQL from
    /// the one that wrote the dump it is restoring.
    ///
    /// <para>This method used to be a hand-rolled copy of that rule, under a docstring claiming to be « one rule ».
    /// The two then drifted in the way that matters: neither could find the tools at all unless
    /// <c>Backup:PgDumpPath</c> was set, and only the Windows installer sets it — so on every other deployment the
    /// restore verb's first act was to refuse.</para>
    /// </summary>
    private static (string? PgRestore, string? PgDump) ResolveTools(IConfiguration configuration)
    {
        var pgDump = PostgresToolLocator.LocatePgDump(configuration);
        var pgRestore = PostgresToolLocator.LocatePgRestore(configuration, pgDump);

        return (pgRestore, pgDump);
    }

    private static string ResolveBackupRoot(IConfiguration configuration)
    {
        var configured = configuration["Backup:DefaultDestination"];
        return string.IsNullOrWhiteSpace(configured)
            ? LocalInstallPaths.Resolve("Backups")
            : configured.Trim();
    }

    private static string ResolveFileStorageBasePath(IConfiguration configuration)
    {
        var basePath = configuration["FileStorage:BasePath"];
        return LocalInstallPaths.Resolve(string.IsNullOrWhiteSpace(basePath) ? "Files" : basePath);
    }

    /// <summary>
    /// Is anything listening on the ports this install serves on? Returns the first busy one.
    ///
    /// <para>A TCP-listener check rather than a service query, because the app legitimately runs three ways
    /// (Windows service, console, desktop shell) and only one of them has a service name — a check that only
    /// knew about the service would happily restore underneath a developer's <c>dotnet run</c>.</para>
    /// </summary>
    private static int? FindListeningAppPort(IConfiguration configuration)
    {
        var ports = new[]
        {
            configuration.GetValue<int?>("Hosting:HttpsPort") ?? 5001,
            configuration.GetValue<int?>("Hosting:HttpPort") ?? 5000,
        };

        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (var port in ports)
            {
                if (listeners.Any(l => l.Port == port))
                {
                    return port;
                }
            }
        }
        catch
        {
            // Cannot enumerate listeners (an unusual host). Fall through: refusing on an inconclusive check would
            // make the verb unusable, and the pg_restore below fails loudly rather than silently if the app is up.
        }

        return null;
    }

    // ------------------------------------------------------------------ the three process calls

    /// <summary>Reads the dump's table of contents and returns how many objects it names; 0 on any problem.</summary>
    private static async Task<int> CountDumpObjectsAsync(
        string pgRestore, string dumpFile, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pgRestore,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--list");
        startInfo.ArgumentList.Add(dumpFile);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        _ = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return 0;
        }

        return stdout.Split('\n')
            .Select(l => l.Trim())
            .Count(l => l.Length > 0 && !l.StartsWith(';'));
    }

    /// <summary>
    /// Dumps the CURRENT database into <paramref name="folder"/> and returns the folder, or null on failure.
    /// A failure here aborts the restore: replacing data with no way back is not a restore, it is a coin toss.
    /// </summary>
    private static async Task<string?> TryWriteSafetyDumpAsync(
        string pgDump, NpgsqlConnectionStringBuilder conn, string folder, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, DumpFileName);

            var startInfo = new ProcessStartInfo
            {
                FileName = pgDump,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            AddConnectionArguments(startInfo, conn);
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("custom");
            startInfo.ArgumentList.Add("--file");
            startInfo.ArgumentList.Add(target);
            startInfo.ArgumentList.Add("--no-password");
            startInfo.Environment["PGPASSWORD"] = conn.Password ?? string.Empty;

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"  pg_dump code {process.ExitCode}. {stderr}".TrimEnd());
                return null;
            }

            return folder;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>pg_restore --clean --if-exists</c> into the configured database.
    ///
    /// <para><c>--clean --if-exists</c> and not a drop/recreate of the database itself: the verb runs as the
    /// application's own role, which owns the objects but not necessarily the database, and dropping the database
    /// would also discard the roles and grants the installer set up. <c>--no-owner</c> because the dump may name a
    /// role that a reinstall regenerated with a different password — the objects belong to whoever is restoring
    /// them.</para>
    ///
    /// <para>A non-zero exit is reported but pg_restore also exits non-zero on harmless « does not exist »
    /// warnings during the clean phase, so the count of real errors is what is shown to the operator.</para>
    /// </summary>
    private static async Task<bool> RunPgRestoreAsync(
        string pgRestore, NpgsqlConnectionStringBuilder conn, string dumpFile, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pgRestore,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        AddConnectionArguments(startInfo, conn);
        startInfo.ArgumentList.Add("--clean");
        startInfo.ArgumentList.Add("--if-exists");
        startInfo.ArgumentList.Add("--no-owner");
        startInfo.ArgumentList.Add("--no-password");
        startInfo.ArgumentList.Add(dumpFile);
        startInfo.Environment["PGPASSWORD"] = conn.Password ?? string.Empty;

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(RestoreTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            Console.Error.WriteLine($"  Délai dépassé ({RestoreTimeoutSeconds}s).");
            return false;
        }

        var stderr = (await stderrTask).Trim();

        if (process.ExitCode == 0)
        {
            return true;
        }

        // The clean phase legitimately warns about objects that are not there on a restore into an empty
        // database; those lines are not failures and must not be reported as one. Anything else is.
        var realErrors = stderr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Where(l => !l.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("n'existe pas", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (realErrors.Count == 0)
        {
            Console.WriteLine("  (avertissements « n'existe pas » ignorés — base initialement vide.)");
            return true;
        }

        foreach (var line in realErrors.Take(20))
        {
            Console.Error.WriteLine($"  {line}");
        }

        return false;
    }

    private static void AddConnectionArguments(ProcessStartInfo startInfo, NpgsqlConnectionStringBuilder conn)
    {
        // Argument LIST, never a shell string — the same rule PgDumpBackupService documents: no quoting or
        // injection surface, and the password goes through the environment rather than the command line.
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(conn.Host ?? "localhost");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add((conn.Port == 0 ? 5432 : conn.Port).ToString());
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(conn.Username ?? string.Empty);
        startInfo.ArgumentList.Add("--dbname");
        startInfo.ArgumentList.Add(conn.Database ?? string.Empty);
    }

    /// <summary>
    /// Bumps every user's <c>TokenVersion</c> so no JWT minted before the restore is still accepted. Raw SQL
    /// rather than the aggregate: this runs with the app stopped and no DI container, and it is one statement.
    /// </summary>
    private static async Task<int> InvalidateSessionsAsync(
        NpgsqlConnectionStringBuilder conn, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(conn.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """UPDATE "Users" SET "TokenVersion" = "TokenVersion" + 1""", connection);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ small helpers

    private static bool HasAnyFile(string path) =>
        Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Any();

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
}
