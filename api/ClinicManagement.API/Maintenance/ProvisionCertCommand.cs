using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Server-side console utility that provisions the LAN HTTPS trust material (a self-signed CA + server
/// certificate) into <c>.local/</c> and exits, WITHOUT starting the web server or touching the database.
///
///   ClinicManagement.API.exe provision-cert
///
/// The server installer runs this at install time — before the API Windows service is started — so the
/// service's first boot reuses an already-provisioned certificate instead of generating it under the
/// ~30s Windows SCM start timeout (on top of first-run JIT), which could otherwise miss the window on a
/// fresh install. It reuses the exact provisioning path the web host uses on first boot
/// (<see cref="CertificateProvisioner.EnsureServerCertificate"/>), so both entry points converge on the
/// same <c>.local/</c> cert set.
///
/// Idempotent: an existing, loadable certificate set is reused as-is, keeping the CA (which clients
/// already trust) stable across reinstalls. Makes no database connection. Only valid in Local (offline)
/// mode — Cloud deployments use a configured certificate.
/// </summary>
public static class ProvisionCertCommand
{
    public const string CommandName = "provision-cert";

    /// <summary>Returns a process exit code: 0 on success, 1 on any failure.</summary>
    public static int Run(string[] args)
    {
        try
        {
            // Resolve appsettings from the install directory (R-6), not the CWD, so the packaged
            // `ClinicManagement.API.exe provision-cert` works from any working directory. The cert set
            // likewise resolves against the install directory via LocalInstallPaths (used by the
            // CertificateProvisioner), so the CLI and the web host write/read the same .local/ folder.
            var configuration = InstallConfiguration.BuildForConsoleVerb();

            var profile = DeploymentProfile.Resolve(configuration);
            if (!profile.SelfSignsCertificate)
            {
                Console.Error.WriteLine(
                    "This certificate-provisioning utility only runs where the deployment signs its own " +
                    $"certificate (deployment profile: {profile.Kind} uses a configured one).");
                return 1;
            }

            var provisioner = new CertificateProvisioner(NullLogger<CertificateProvisioner>.Instance);
            var result = provisioner.EnsureServerCertificate();

            Console.WriteLine();
            Console.WriteLine("HTTPS certificate provisioned.");
            Console.WriteLine($"  CA certificate: {result.CaCertPath}");
            Console.WriteLine();
            Console.WriteLine("The API service will reuse this certificate on first boot; import the CA");
            Console.WriteLine("into client machines' trust store (see packaging/README.md).");
            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Certificate provisioning failed: {ex.Message}");
            return 1;
        }
    }
}
