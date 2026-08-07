using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The single definition of the Data Protection key-ring configuration (security-hardening Part 1, DEV-1).
///
/// Why this exists: the Local <c>protect-credentials</c> / <c>read-credentials</c> console verbs run before the
/// web host boots, so they have no DI container and must configure the <b>identical</b> key ring. If the two
/// sites drifted, the installer would write ciphertext the API cannot read and an existing PostgreSQL cluster
/// would become unreachable. Both now route through <see cref="LocalDataProtection"/>, mirroring the
/// convention <c>LocalAuthConfig</c> already applies to the JWT signing key.
///
/// These tests pin the mode-resolved path only. Building a real provider is deliberately not exercised here:
/// it would write a key ring to disk and, on Windows, invoke machine-scoped DPAPI — filesystem and OS side
/// effects that do not belong in a unit test. The no-drift guarantee is structural (one code path), not
/// something a test can add.
/// </summary>
public class LocalDataProtectionTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void Local_mode_resolves_the_key_ring_into_the_per_install_local_directory()
    {
        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(("Auth:Mode", "Local")));

        Assert.NotNull(path);
        // Anchored to the install directory, not the CWD — a Windows service's CWD is System32 (R-6).
        Assert.Equal(
            Path.Combine(LocalInstallPaths.LocalDir, LocalDataProtection.LocalKeyRingFolderName),
            path);
    }

    [Fact]
    public void Cloud_mode_honours_a_configured_key_ring_path()
    {
        var configured = Path.Combine(Path.GetTempPath(), "cloud-key-ring");

        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(
            ("Auth:Mode", "Cloud"),
            ("DataProtection:KeyRingPath", configured)));

        Assert.Equal(configured, path);
    }

    [Fact]
    public void Cloud_mode_without_a_configured_path_falls_back_to_the_framework_default()
    {
        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(("Auth:Mode", "Cloud")));

        // null ⇒ the caller skips PersistKeysToFileSystem, leaving the framework default location.
        Assert.Null(path);
    }

    [Fact]
    public void Cloud_is_the_default_when_no_mode_is_configured() // matches LocalAuthConfig / appsettings.json
    {
        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(("DataProtection:KeyRingPath", null)));

        Assert.Null(path);
    }

    [Fact]
    public void The_application_name_is_stable() // changing it silently invalidates every existing ciphertext
    {
        Assert.Equal("ClinicManagement", LocalDataProtection.ApplicationName);
    }

    // ---- The hosted profile requires the key ring, and fails startup without it (US-6 step 17) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_hosted_profile_refuses_to_start_without_a_key_ring_path(string? configured)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LocalDataProtection.ResolveKeyRingPath(
            Configuration(
                (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
                (LocalDataProtection.KeyRingPathKey, configured))));

        // The framework fallback is per-instance and ephemeral. It WORKS — and then the first redeploy replaces the
        // ring, so every clinic's stored reminder and TTN credentials become undecryptable and each channel reports
        // « non configuré » with nothing in any log tying that to a deployment. Failing loud at startup is the only
        // moment this is visible at all.
        Assert.Contains(LocalDataProtection.KeyRingPathKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DeploymentKind.HostedMultiTenant), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosted_profile_accepts_a_configured_key_ring_path()
    {
        var configured = Path.Combine(Path.GetTempPath(), "hosted-key-ring");

        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
            (LocalDataProtection.KeyRingPathKey, configured)));

        Assert.Equal(configured, path);
    }

    [Theory]
    [InlineData(nameof(DeploymentKind.SelfHostedLan))]
    [InlineData(nameof(DeploymentKind.CloudBrowser))]
    public void The_two_shipped_profiles_are_unchanged_by_the_new_requirement(string profile)
    {
        // R-2: SelfHostedLan resolves its own install-relative path and never reads the key; CloudBrowser keeps the
        // framework fallback, which is survivable there because that profile is not what US-6 is hardening.
        var path = LocalDataProtection.ResolveKeyRingPath(Configuration((DeploymentProfile.ProfileKey, profile)));

        if (profile == nameof(DeploymentKind.SelfHostedLan))
        {
            Assert.Equal(
                Path.Combine(LocalInstallPaths.LocalDir, LocalDataProtection.LocalKeyRingFolderName),
                path);
        }
        else
        {
            Assert.Null(path);
        }
    }
}
