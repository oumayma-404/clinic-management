using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// The <b>single</b> definition of this install's ASP.NET Core Data Protection key-ring configuration.
///
/// Two entry points need it and they must never diverge:
///   1. the web host, via <c>AddInfrastructure</c>;
///   2. the Local console verbs (<c>protect-credentials</c> / <c>read-credentials</c>), which run
///      <b>before the web host boots</b> and therefore have no DI container to resolve from.
///
/// If those two configured the key ring separately they could drift (a changed application name, key-ring
/// path, or at-rest protection), and the failure mode is severe and silent: the installer would write
/// ciphertext the API cannot read, leaving an existing PostgreSQL cluster unreachable. Sharing one
/// definition makes that structurally impossible — the same convention <see cref="LocalAuthConfig"/>
/// applies to the JWT signing key, where issuer and validator resolve through one path.
///
/// Key-ring location follows the deployment profile: an install whose paths are install-relative keeps it in the
/// gitignored per-install <c>.local/</c> (via <see cref="LocalInstallPaths"/>); a hosted one uses a configured
/// directory (<c>DataProtection:KeyRingPath</c>).
///
/// <para><b>⚠️ In <see cref="DeploymentKind.HostedMultiTenant"/> that key is required and its absence fails
/// startup</b> (US-6 step 17). Everywhere else an unset path falls back to the framework default, which is
/// per-instance and ephemeral — the container's own filesystem. That fallback is survivable for a single clinic
/// on its own PC and is not survivable for a hosted backend: it works, and then the first redeploy replaces the
/// ring, so every clinic's stored reminder credentials become undecryptable and each channel reports
/// « non configuré » with nothing in any log tying that to a deployment. This also gates US-4, which protects
/// per-clinic reminder secrets the same way — a rotated ring would silently stop every clinic's channels.
/// A path with no durable volume behind it produces exactly the same symptom, which no code can detect; that
/// half is stated in <c>deploy/docker-compose.hosted.yml</c> beside the volume.</para>
/// </summary>
public static class LocalDataProtection
{
    /// <summary>Purpose-independent application discriminator. Changing it invalidates all ciphertext.</summary>
    public const string ApplicationName = "ClinicManagement";

    /// <summary>Key-ring folder name inside the per-install <c>.local/</c> directory.</summary>
    public const string LocalKeyRingFolderName = "dataprotection-keys";

    /// <summary>Configuration key naming a hosted deployment's shared key-ring directory.</summary>
    public const string KeyRingPathKey = "DataProtection:KeyRingPath";

    /// <summary>
    /// The directory the key ring is persisted to, or <c>null</c> when the deployment has not configured one and
    /// may fall back to the framework default location.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>DataProtection:KeyRingPath</c> is unset in <see cref="DeploymentKind.HostedMultiTenant"/>, where the
    /// ephemeral fallback loses every clinic's encrypted credentials on the next redeploy.
    /// </exception>
    public static string? ResolveKeyRingPath(IConfiguration configuration)
    {
        var profile = DeploymentProfile.Resolve(configuration);

        if (profile.RunsAsWindowsService)
        {
            return Path.Combine(LocalInstallPaths.LocalDir, LocalKeyRingFolderName);
        }

        var configured = configuration[KeyRingPathKey];

        if (string.IsNullOrWhiteSpace(configured) && profile.Kind == DeploymentKind.HostedMultiTenant)
        {
            throw new InvalidOperationException(
                $"{KeyRingPathKey} is required in the {nameof(DeploymentKind.HostedMultiTenant)} deployment "
                + "profile. Without it the Data Protection key ring is per-instance and ephemeral, so every "
                + "clinic's encrypted reminder credentials become unreadable after a redeploy "
                + "and each channel silently reports « non configuré ». Point it at a directory backed by a "
                + "durable volume (see deploy/docker-compose.hosted.yml).");
        }

        return configured;
    }

    /// <summary>
    /// Registers Data Protection with this install's configuration. Called by <c>AddInfrastructure</c> for
    /// the web host and by <see cref="CreateStandaloneProvider"/> for the console verbs.
    /// </summary>
    public static IDataProtectionBuilder AddConfiguredDataProtection(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        var profile = DeploymentProfile.Resolve(configuration);
        var keyRingPath = ResolveKeyRingPath(configuration);

        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            Directory.CreateDirectory(keyRingPath);
            builder.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

            // Encrypt the key ring itself at rest: supplying a custom key repository disables the framework's
            // automatic key-at-rest protection, which would leave the master keys (that encrypt every clinic's
            // credentials, and the DB passwords) in cleartext on disk. On the Local Windows install, protect
            // them with machine-scoped DPAPI so a stolen/copied key-ring folder is useless off the host —
            // this is what makes the protected db-credentials file machine-bound (spec AC-3.1). DPAPI is
            // Windows-only; a hosted key ring at DataProtection:KeyRingPath relies on that directory's ACLs
            // (ops responsibility).
            if (profile.RunsAsWindowsService && OperatingSystem.IsWindows())
            {
                builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
            }
        }

        return builder;
    }

    /// <summary>
    /// Builds a provider over the <b>same</b> key ring for a process with no DI container — the Local
    /// console verbs. The backing <see cref="ServiceProvider"/> is deliberately not disposed: the returned
    /// protector depends on it, and these verbs exit immediately after use.
    /// </summary>
    public static IDataProtectionProvider CreateStandaloneProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        AddConfiguredDataProtection(services, configuration);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }
}
