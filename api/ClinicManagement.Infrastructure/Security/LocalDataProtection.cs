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
    /// Whether this deployment must supply a certificate to encrypt the key ring with (FR-3.1). True on
    /// <see cref="DeploymentKind.HostedMultiTenant"/> and false everywhere else — a Windows install protects the
    /// same ring with machine-scoped DPAPI, and <see cref="DeploymentKind.CloudBrowser"/> may still opt in by
    /// setting <see cref="KeyRingProtectionCertificates.CertificatePathKey"/>.
    /// </summary>
    public static bool RequiresProtectingCertificate(DeploymentProfile profile) =>
        profile.Kind == DeploymentKind.HostedMultiTenant;

    /// <summary>
    /// Whether an unencrypted key ring is tolerated here. True in <c>Development</c> alone, exactly as
    /// <c>MinioCredentials.TolerateUnconfigured</c> decides the same question for object-store credentials.
    /// </summary>
    public static bool TolerateUnprotectedKeyRing(IConfiguration configuration) =>
        string.Equals(
            (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? configuration["Environment"])?.Trim(),
            "Development",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The operator-facing refusal. Names what to set, not merely what is wrong.</summary>
    public const string UnprotectedKeyRingMessage =
        "Aucun certificat de protection n'est renseigné (ni DataProtection:CertificatePath, ni "
        + "DataProtection:CertificateBase64). Le trousseau de protection des données reste alors "
        + "en clair sur son volume : les clés qui déchiffrent les identifiants de rappel de chaque cabinet et le "
        + "second facteur de chaque administrateur seraient lisibles depuis un disque volé ou une copie du "
        + "volume. Fournissez un fichier PKCS#12 — par chemin (DataProtection:CertificatePath) là où un fichier "
        + "peut être monté, ou encodé en base64 (DataProtection:CertificateBase64) sur un hébergeur qui ne "
        + "transmet que des variables d'environnement — avec son mot de passe dans "
        + "DataProtection:CertificatePassword. Voir deploy/KEY-CUSTODY.md.";

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
            // this is what makes the protected db-credentials file machine-bound (spec AC-3.1).
            if (profile.RunsAsWindowsService && OperatingSystem.IsWindows())
            {
                builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
            }
            else
            {
                ApplyCertificateProtection(builder, configuration, profile);
            }
        }

        return builder;
    }

    /// <summary>
    /// FR-3.1 — encrypts the key ring with the deployment's own certificate, the Linux answer to the DPAPI
    /// branch above.
    ///
    /// <para>⚠️ <b>This protects keys the ring WRITES from here on; it re-wraps nothing already on the volume.</b>
    /// Data Protection encrypts key XML only at the moment it persists it, so a key created before this was
    /// configured stays in cleartext for the rest of its life <i>and</i> remains a valid decryptor long after.
    /// FR-3.1 would read satisfied while a stolen volume still yields a readable master key. Closing that is the
    /// <c>reprotect-secrets</c> verb's job (re-encrypt the ciphertext under a fresh key), followed by deleting the
    /// superseded plaintext key files — in that order, and only once
    /// <c>verify-schema</c>'s <c>secrets-protected-under-current-ring</c> reads zero.</para>
    ///
    /// <para>⚠️ <b>Re-minting the ring is forbidden</b> (R-2): the old keys must stay as decryptors or every
    /// second factor Part A enrolled, and every clinic's reminder credentials, become unreadable at once.</para>
    /// </summary>
    private static void ApplyCertificateProtection(
        IDataProtectionBuilder builder, IConfiguration configuration, DeploymentProfile profile)
    {
        var certificates = KeyRingProtectionCertificates.Resolve(configuration, DateTime.UtcNow);

        if (!certificates.IsConfigured)
        {
            if (!RequiresProtectingCertificate(profile))
            {
                return;
            }

            // Development is exempt on MinioCredentials.TolerateUnconfigured's precedent, one file over and in
            // this same startup path: appsettings.Development.json selects HostedMultiTenant deliberately (it is
            // the only profile where public signup is open), and no developer has a PKCS#12 — so failing here
            // would break `dotnet run` and `dotnet ef` on a fresh clone for everyone.
            if (!TolerateUnprotectedKeyRing(configuration))
            {
                throw new InvalidOperationException(UnprotectedKeyRingMessage);
            }

            Console.Error.WriteLine(
                "[warn] The Data Protection key ring is NOT encrypted at rest. Acceptable in Development only — "
                + "a non-Development environment will refuse to start. " + UnprotectedKeyRingMessage);
            return;
        }

        // No logger exists this early — registration runs before the host is built — so an approaching expiry
        // goes to stderr, where `docker logs` shows it. verify-schema's `key-ring-protection` is the durable read.
        foreach (var warning in certificates.Warnings)
        {
            Console.Error.WriteLine($"[data-protection] {warning}");
        }

        builder.ProtectKeysWithCertificate(certificates.Active!);
        builder.UnprotectKeysWithAnyCertificate(certificates.Decryptors.ToArray());
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
