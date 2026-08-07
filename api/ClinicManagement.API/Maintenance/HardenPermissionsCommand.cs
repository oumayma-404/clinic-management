using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Server-side console utility that tightens NTFS permissions on the install's data directories and exits,
/// WITHOUT starting the web server or touching the database.
///
///   ClinicManagement.API.exe harden-permissions "C:\...\api\.local" "C:\...\api\Files" ...
///
/// The server installer invokes this instead of running <c>icacls</c> from its own Pascal script, so the
/// permission policy has exactly one implementation (<see cref="DirectoryAclHardener"/>) — shared with the
/// one-click backup — and that implementation is unit-testable. Inno Setup script logic is not.
///
/// Closes audit § 2 findings 1–3: the installer's <c>[Dirs] Permissions:</c> entries only ADD an ACE and
/// leave the inherited <c>Users: Read &amp; Execute</c> intact, and the Full Control granted to
/// <c>BUILTIN\Users</c> so de-privileged <c>initdb</c> could run was never revoked.
///
/// Fails loud: any directory that cannot be secured produces a French operator message and a non-zero exit
/// so the installer aborts, rather than completing with patient data readable by every local account.
/// Only valid in Local (offline) mode — Cloud deployments do not own their host's filesystem.
/// </summary>
public static class HardenPermissionsCommand
{
    public const string CommandName = "harden-permissions";

    /// <summary>Returns a process exit code: 0 when every directory was secured, 1 on any failure.</summary>
    public static int Run(string[] args)
    {
        try
        {
            // Resolve appsettings from the install directory (R-6), not the CWD, so the packaged
            // `ClinicManagement.API.exe harden-permissions` works from any working directory.
            var configuration = InstallConfiguration.BuildForConsoleVerb();

            var profile = DeploymentProfile.Resolve(configuration);
            if (!profile.RunsAsWindowsService)
            {
                Console.Error.WriteLine(
                    "Cet utilitaire de sécurisation des droits ne s'applique qu'à une installation Windows " +
                    $"locale (profil de déploiement : {profile.Kind}).");
                return 1;
            }

            // args[0] is the verb itself.
            var directories = args.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
            if (directories.Length == 0)
            {
                Console.Error.WriteLine(
                    $"Usage : ClinicManagement.API.exe {CommandName} <dossier> [<dossier> ...]");
                return 1;
            }

            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine(
                    "La sécurisation des droits NTFS n'est disponible que sous Windows.");
                return 1;
            }

            var hardener = new DirectoryAclHardener();

            foreach (var directory in directories)
            {
                hardener.Harden(directory);

                Console.WriteLine($"Droits sécurisés : {directory}");
                // Record the resulting posture in the installer log so the operator can verify it without
                // re-running icacls by hand (spec AC-1.1 is an operator-checklist item).
                Console.WriteLine(hardener.Describe(directory));
                Console.WriteLine();
            }

            Console.WriteLine(
                $"{directories.Length} dossier(s) sécurisé(s) : accès réservé au service, aux " +
                "administrateurs et au système. Le groupe « Utilisateurs » n'a plus aucun accès.");
            return 0;
        }
        catch (Exception ex)
        {
            // Fail loud — a half-applied permission change must never look like success.
            Console.Error.WriteLine($"Échec de la sécurisation des droits : {ex.Message}");
            return 1;
        }
    }
}
