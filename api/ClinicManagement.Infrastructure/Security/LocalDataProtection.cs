using ClinicManagement.Infrastructure.Auth;
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
/// Key-ring location is mode-resolved: Local → the gitignored per-install <c>.local/</c> (via
/// <see cref="LocalInstallPaths"/>); Cloud → an optional configured directory
/// (<c>DataProtection:KeyRingPath</c>). If Cloud leaves it unset, keys use the framework default location
/// (single-instance only — a multi-instance Cloud deployment must configure a shared key ring; ops note).
/// </summary>
public static class LocalDataProtection
{
    /// <summary>Purpose-independent application discriminator. Changing it invalidates all ciphertext.</summary>
    public const string ApplicationName = "ClinicManagement";

    /// <summary>Key-ring folder name inside the per-install <c>.local/</c> directory (Local mode).</summary>
    public const string LocalKeyRingFolderName = "dataprotection-keys";

    /// <summary>
    /// The directory the key ring is persisted to, or <c>null</c> when Cloud has not configured one (in
    /// which case the framework default location is used).
    /// </summary>
    public static string? ResolveKeyRingPath(IConfiguration configuration) =>
        LocalAuthConfig.IsLocalMode(configuration)
            ? Path.Combine(LocalInstallPaths.LocalDir, LocalKeyRingFolderName)
            : configuration["DataProtection:KeyRingPath"];

    /// <summary>
    /// Registers Data Protection with this install's configuration. Called by <c>AddInfrastructure</c> for
    /// the web host and by <see cref="CreateStandaloneProvider"/> for the console verbs.
    /// </summary>
    public static IDataProtectionBuilder AddConfiguredDataProtection(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        var isLocalMode = LocalAuthConfig.IsLocalMode(configuration);
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
            // Windows-only; a Cloud key ring at DataProtection:KeyRingPath relies on that directory's ACLs
            // (ops responsibility).
            if (isLocalMode && OperatingSystem.IsWindows())
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
