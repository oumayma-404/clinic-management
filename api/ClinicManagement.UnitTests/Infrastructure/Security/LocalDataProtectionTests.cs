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
    public void A_hosted_deployment_honours_a_configured_key_ring_path()
    {
        var configured = Path.Combine(Path.GetTempPath(), "hosted-key-ring");

        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
            ("DataProtection:KeyRingPath", configured)));

        Assert.Equal(configured, path);
    }

    /*
     * ⚠️ Two tests stood here and are deliberately gone rather than ported:
     * « Cloud mode without a configured path falls back to the framework default » and « Cloud is the default
     * when no mode is configured ». Both asserted a NULL return, i.e. « skip PersistKeysToFileSystem and let the
     * framework pick a location » — and both reached it through the CloudBrowser profile, which is retired.
     *
     * That path is now unreachable by configuration, and saying so is the point: of the two surviving kinds,
     * SelfHostedLan returns its install-relative directory and HostedMultiTenant **throws** without an explicit
     * path (US-6 — the framework's per-instance ring is ephemeral, so the first redeploy makes every clinic's
     * encrypted reminder credentials undecryptable). Porting them to a profile key would have meant asserting
     * the opposite of what the code does, and re-pointing them at HostedMultiTenant would have duplicated the
     * refusal test below.
     *
     * A null return is still reachable — but only through `DataProtection:PersistToDatabase`, which has its own
     * coverage and is a different statement entirely.
     */

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
        // ring, so every clinic's stored reminder credentials become undecryptable and each channel reports
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

    // ---- The ring in the database, for a host that sells no durable disk -----------------------
    //
    // Why this option exists at all: on HostedMultiTenant an ephemeral ring is not « reminders stop working ».
    // RequiresAdminSecondFactor is true there, so every administrator holds a TOTP secret this ring encrypts, and
    // the first redeploy would lock every one of them out of their own cabinet. Where the only durable thing is
    // the database, that is where the ring belongs.

    [Fact]
    public void Persisting_To_The_Database_Needs_No_Directory_And_Does_Not_Refuse()
    {
        var path = LocalDataProtection.ResolveKeyRingPath(Configuration(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
            (LocalDataProtection.PersistToDatabaseKey, "true")));

        // Null is the answer, not a failure: the ring has a home, and it is not a folder.
        Assert.Null(path);
    }

    // ⚠️ The case worth having: two homes for one ring means half the keys are written where the other half is
    // not looked for, and the symptom is decryption that works until it abruptly does not. Refused, not resolved
    // by precedence.
    [Fact]
    public void Naming_Both_A_Database_And_A_Directory_Refuses()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LocalDataProtection.ResolveKeyRingPath(Configuration(
                (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
                (LocalDataProtection.PersistToDatabaseKey, "true"),
                (LocalDataProtection.KeyRingPathKey, Path.Combine(Path.GetTempPath(), "both")))));

        Assert.Contains(LocalDataProtection.PersistToDatabaseKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains(LocalDataProtection.KeyRingPathKey, ex.Message, StringComparison.Ordinal);
    }

    // The other direction, so the new option cannot quietly become a way to skip the requirement: with neither a
    // path nor the database, HostedMultiTenant still refuses — and now names both ways out.
    [Fact]
    public void Hosted_Still_Refuses_When_Neither_Home_Is_Configured()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LocalDataProtection.ResolveKeyRingPath(Configuration(
                (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)))));

        Assert.Contains(LocalDataProtection.KeyRingPathKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains(LocalDataProtection.PersistToDatabaseKey, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]          // not a bool: absent rather than an error, like the MinIO:UseSSL read
    [InlineData("true", true)]
    [InlineData("  TRUE  ", true)]     // an environment variable routinely arrives padded
    public void The_Database_Flag_Is_Read_Conservatively(string? configured, bool expected)
    {
        Assert.Equal(
            expected,
            LocalDataProtection.PersistsToDatabase(
                Configuration((LocalDataProtection.PersistToDatabaseKey, configured))));
    }
}
