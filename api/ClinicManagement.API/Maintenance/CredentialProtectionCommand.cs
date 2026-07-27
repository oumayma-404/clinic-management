using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Security;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Server-side console utilities that encrypt / decrypt the per-install <c>.local/db-credentials</c> file
/// and exit, WITHOUT starting the web server or touching the database.
///
///   ClinicManagement.API.exe protect-credentials
///   ClinicManagement.API.exe read-credentials --out &lt;file&gt;
///
/// Closes audit § 2 finding 4: that file held both the <c>clinic_user</c> and the <c>postgres</c> superuser
/// password in cleartext under <c>Program Files</c>. Tightened ACLs stop other local accounts reading it,
/// but not an admin-level foothold or a disk-level copy — so the payload is encrypted through Data
/// Protection, whose Local key ring is machine-scoped DPAPI-protected (spec AC-3.1).
///
/// The installer owns password <i>generation</i> (CSPRNG, unchanged) and writes the plaintext file; this
/// verb encrypts it in place. On a reinstall the installer needs the passwords back to authenticate against
/// the existing cluster, so <c>read-credentials</c> decrypts to a caller-supplied file which the installer
/// deletes immediately — mirroring the existing <c>pg-super.pw</c> pattern. Passwords are never passed as
/// command-line arguments (spec AC-3.6).
///
/// Both verbs are idempotent and Local-only.
/// </summary>
public static class CredentialProtectionCommand
{
    public const string ProtectCommandName = "protect-credentials";
    public const string ReadCommandName = "read-credentials";

    /// <summary>File name inside <c>.local/</c>. Must match <c>clinic-server.iss</c>'s DbCredentialsFile.</summary>
    private const string CredentialsFileName = "db-credentials";

    /// <summary>
    /// Encrypts <c>.local/db-credentials</c> in place. A file that is already protected is left untouched
    /// (exit 0), so re-running the installer is safe.
    /// </summary>
    public static int RunProtect(string[] args)
    {
        try
        {
            var protector = BuildProtector(out var credentialsPath);
            if (protector is null)
            {
                return 1;
            }

            if (!File.Exists(credentialsPath))
            {
                Console.Error.WriteLine(
                    $"Fichier d'identifiants introuvable : {credentialsPath}");
                return 1;
            }

            var content = File.ReadAllText(credentialsPath);

            if (DbCredentialProtector.IsProtected(content))
            {
                Console.WriteLine("Le fichier d'identifiants de la base est déjà chiffré.");
                return 0;
            }

            // Read (validates the two-line shape) then re-write protected.
            var read = protector.ReadFileContent(content);
            WriteAtomically(credentialsPath, protector.ProtectFileContent(read.Credentials));

            Console.WriteLine(
                $"Fichier d'identifiants de la base chiffré : {credentialsPath}");
            Console.WriteLine(
                "Il n'est déchiffrable que sur cette machine. Sauvegardez le dossier .local avec la base.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Échec du chiffrement des identifiants de la base : {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Decrypts <c>.local/db-credentials</c> to the file given by <c>--out</c>, as two plaintext lines
    /// (clinic_user password, then postgres superuser password). If the source was still legacy plaintext it
    /// is migrated to the protected form in the same pass (spec AC-3.3).
    /// </summary>
    public static int RunRead(string[] args)
    {
        try
        {
            var outputPath = ResolveOutPath(args);
            if (outputPath is null)
            {
                Console.Error.WriteLine(
                    $"Usage : ClinicManagement.API.exe {ReadCommandName} --out <fichier>");
                return 1;
            }

            var protector = BuildProtector(out var credentialsPath);
            if (protector is null)
            {
                return 1;
            }

            if (!File.Exists(credentialsPath))
            {
                Console.Error.WriteLine($"Fichier d'identifiants introuvable : {credentialsPath}");
                return 1;
            }

            var content = File.ReadAllText(credentialsPath);
            var read = protector.ReadFileContent(content);

            // Two plaintext lines, in the order clinic-server.iss expects.
            WriteAtomically(
                outputPath,
                read.Credentials.ClinicUserPassword + "\r\n" + read.Credentials.PostgresSuperPassword + "\r\n");

            if (read.WasLegacyPlaintext)
            {
                // Upgrade path: an install created by an earlier installer still holds cleartext. Migrate it
                // now, while we already have the plaintext in hand.
                WriteAtomically(credentialsPath, protector.ProtectFileContent(read.Credentials));
                Console.WriteLine(
                    "Ancien fichier d'identifiants en clair détecté : il a été chiffré (migration).");
            }

            Console.WriteLine("Identifiants de la base récupérés.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Échec de la lecture des identifiants de la base : {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Builds the protector over the same key ring the web host uses, and resolves the credentials path.
    /// Returns <c>null</c> (after printing why) when this is not a Local install.
    /// </summary>
    private static DbCredentialProtector? BuildProtector(out string credentialsPath)
    {
        credentialsPath = string.Empty;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(
                $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                optional: true)
            .AddEnvironmentVariables()
            .Build();

        if (!LocalAuthConfig.IsLocalMode(configuration))
        {
            Console.Error.WriteLine(
                "Cet utilitaire ne fonctionne qu'en mode Local (hors ligne) (Auth:Mode=Local).");
            return null;
        }

        credentialsPath = LocalInstallPaths.LocalFile(CredentialsFileName);

        // Shares LocalDataProtection with AddInfrastructure, so the key ring here is byte-for-byte the one
        // the running API will use — the ciphertext this verb writes is always readable by the service.
        return new DbCredentialProtector(LocalDataProtection.CreateStandaloneProvider(configuration));
    }

    /// <summary>Reads <c>--out &lt;path&gt;</c> from the argument list.</summary>
    private static string? ResolveOutPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Writes via a unique temp file + move so a crash mid-write cannot leave a truncated credentials file —
    /// which, on an existing cluster, would be unrecoverable. The temp name is unique per write (a fixed
    /// shared temp path is not atomic under concurrency).
    /// </summary>
    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
