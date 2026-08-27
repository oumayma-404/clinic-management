using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The key ring's protection at rest (<c>hosted-security-hardening</c> FR-3.1 / FR-3.2), and the generation
/// arithmetic the <c>reprotect-secrets</c> verb, <c>verify-schema</c>'s coverage figure and the FR-3.9 dump stamp
/// all read.
///
/// <para><b>What is worth testing here, and what is not.</b> That <c>ProtectKeysWithCertificate</c> encrypts is
/// the framework's business. What is this feature's business — and what fails <i>silently</i> — is: a configured
/// certificate that cannot be used must refuse rather than fall back to a cleartext ring; the active certificate
/// must be in the <b>decryptor</b> set or the ring writes keys it cannot read back after a restart; and the two
/// renderings of a key id must be the same text, or the FR-3.9 check refuses restores that were never in
/// danger.</para>
/// </summary>
public class KeyRingProtectionTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    /// <summary>Writes a real self-signed PKCS#12 with a private key, and returns its path.</summary>
    private static string WriteCertificate(string directory, string name, int daysValid = 3650)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(Now.AddDays(-1)), new DateTimeOffset(Now.AddDays(daysValid)));

        var path = Path.Combine(directory, $"{name}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12));
        return path;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            Directory.CreateDirectory(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hshb-" + Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // ---- FR-3.1: a configured certificate is loaded, and the ACTIVE one decrypts too ------------

    // ⚠️ The case that would otherwise be found in production, months later: with only the encryptor configured
    // the framework resolves the private key for DECRYPTION out of the machine's certificate store, which in a
    // Linux container holds nothing — so the ring writes keys it cannot read back on the next restart.
    [Fact]
    public void The_Active_Certificate_Is_A_Decryptor_As_Well_As_The_Encryptor() // [FR-3.1]
    {
        using var dir = new TempDir();
        var path = WriteCertificate(dir.Path, "active");

        var resolution = KeyRingProtectionCertificates.Resolve(
            Config((KeyRingProtectionCertificates.CertificatePathKey, path)), Now);

        Assert.True(resolution.IsConfigured);
        Assert.Contains(resolution.Decryptors, c => c.Thumbprint == resolution.Active!.Thumbprint);
    }

    // FR-3.2: a rotation must not make existing ciphertext unreadable, which is why previous generations stay.
    [Fact]
    public void Retained_Generations_Are_Loaded_As_Decryptors() // [FR-3.2]
    {
        using var dir = new TempDir();
        var active = WriteCertificate(dir.Path, "active");
        var previous = WriteCertificate(dir.Path, "previous");

        var resolution = KeyRingProtectionCertificates.Resolve(
            Config(
                (KeyRingProtectionCertificates.CertificatePathKey, active),
                ($"{KeyRingProtectionCertificates.PreviousCertificatesSection}:0:Path", previous)),
            Now);

        Assert.Equal(2, resolution.Decryptors.Count);
    }

    [Fact]
    public void Retaining_More_Than_The_Recommended_Generations_Warns() // [FR-3.2]
    {
        using var dir = new TempDir();
        var values = new List<(string, string?)>
        {
            (KeyRingProtectionCertificates.CertificatePathKey, WriteCertificate(dir.Path, "active")),
        };
        for (var i = 0; i <= KeyRingProtectionCertificates.RecommendedRetainedGenerations; i++)
        {
            values.Add(($"{KeyRingProtectionCertificates.PreviousCertificatesSection}:{i}:Path",
                WriteCertificate(dir.Path, $"previous{i}")));
        }

        var resolution = KeyRingProtectionCertificates.Resolve(Config(values.ToArray()), Now);

        Assert.Contains(resolution.Warnings, w => w.Contains("recommande", StringComparison.Ordinal));
    }

    // An expiring certificate is REPORTED, never refused: it still decrypts perfectly well, and taking a whole
    // deployment down on a date nobody watched is not a security gain.
    [Fact]
    public void An_Expiring_Certificate_Warns_Rather_Than_Refusing() // [FR-3.2]
    {
        using var dir = new TempDir();
        var path = WriteCertificate(dir.Path, "expiring", daysValid: 5);

        var resolution = KeyRingProtectionCertificates.Resolve(
            Config((KeyRingProtectionCertificates.CertificatePathKey, path)), Now);

        Assert.True(resolution.IsConfigured);
        Assert.Contains(resolution.Warnings, w => w.Contains("expire", StringComparison.OrdinalIgnoreCase));
    }

    // ---- The refusals. Each is a stated intention that must not become a silent no-op --------------

    [Fact]
    public void A_Missing_Certificate_File_Refuses_And_Names_The_Setting() // [Part 3 edge case 1]
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config((KeyRingProtectionCertificates.CertificatePathKey, "/nowhere/keyring.pfx")), Now));

        Assert.Contains(KeyRingProtectionCertificates.CertificatePathKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains("introuvable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Unreadable_Certificate_Refuses() // [Part 3 edge case 1]
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "not-a-pfx.pfx");
        File.WriteAllText(path, "this is not a PKCS#12 file");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config((KeyRingProtectionCertificates.CertificatePathKey, path)), Now));

        Assert.Contains("illisible", ex.Message, StringComparison.Ordinal);
    }

    // A certificate with no private key encrypts fine and can never DECRYPT, so the ring would write keys it
    // could not read back — the same failure as the decryptor case above, arrived at from the operator's side.
    [Fact]
    public void A_Certificate_Without_A_Private_Key_Refuses() // [FR-3.1]
    {
        using var dir = new TempDir();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=public-only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(Now.AddDays(-1)), new DateTimeOffset(Now.AddDays(365)));

        var path = Path.Combine(dir.Path, "public-only.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config((KeyRingProtectionCertificates.CertificatePathKey, path)), Now));

        Assert.Contains("clé privée", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Retained_Generation_With_No_Path_Refuses() // [FR-3.2]
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config(
                    (KeyRingProtectionCertificates.CertificatePathKey, WriteCertificate(dir.Path, "active")),
                    ($"{KeyRingProtectionCertificates.PreviousCertificatesSection}:0:Password", "x")),
                Now));

        Assert.Contains("Path", ex.Message, StringComparison.Ordinal);
    }

    // ---- FR-3.1 delivery: the same certificate, handed over as base64 --------------------------
    //
    // Why these exist: CertificatePath assumes a file mount, and a managed platform (Render, Fly, App Service)
    // hands a process environment variables. A `.pfx` pasted into a text-only "secret file" arrives corrupted and
    // a PEM loads with no private key, so without this route the deployment simply cannot start — which is what
    // happened on the first hosted deploy of this branch.

    /// <summary>The bytes of a real PKCS#12 with a private key, base64-encoded.</summary>
    private static string CertificateBase64(int daysValid = 3650)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=base64-active", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(Now.AddDays(-1)), new DateTimeOffset(Now.AddDays(daysValid)));

        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));
    }

    [Fact]
    public void A_Base64_Certificate_Is_Loaded_And_Also_Decrypts() // [FR-3.1]
    {
        var resolution = KeyRingProtectionCertificates.Resolve(
            Config((KeyRingProtectionCertificates.CertificateBase64Key, CertificateBase64())), Now);

        Assert.True(resolution.IsConfigured);
        Assert.True(resolution.Active!.HasPrivateKey);
        // The active certificate leads the decryptor set, exactly as on the file route — otherwise the ring
        // writes keys it cannot read back after a restart.
        Assert.Same(resolution.Active, resolution.Decryptors[0]);
    }

    // A dashboard that soft-wraps a ~3 KB value must not turn a perfectly good certificate into « illisible ».
    [Fact]
    public void A_Base64_Certificate_Survives_The_Wrapping_A_Dashboard_Adds() // [FR-3.1]
    {
        var wrapped = string.Join("\n", Chunk(CertificateBase64(), 64));

        var resolution = KeyRingProtectionCertificates.Resolve(
            Config((KeyRingProtectionCertificates.CertificateBase64Key, wrapped)), Now);

        Assert.True(resolution.IsConfigured);

        static IEnumerable<string> Chunk(string value, int size)
        {
            for (var i = 0; i < value.Length; i += size)
            {
                yield return value.Substring(i, Math.Min(size, value.Length - i));
            }
        }
    }

    // ⚠️ The case this pair exists for: naming two certificates for one role is an operator holding two
    // intentions, and honouring one silently is how a deployment encrypts under a key nobody is backing up.
    [Fact]
    public void Naming_Both_A_Path_And_Base64_Refuses_Rather_Than_Choosing() // [FR-3.1]
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config(
                    (KeyRingProtectionCertificates.CertificatePathKey, WriteCertificate(dir.Path, "active")),
                    (KeyRingProtectionCertificates.CertificateBase64Key, CertificateBase64())),
                Now));

        Assert.Contains(KeyRingProtectionCertificates.CertificatePathKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains(KeyRingProtectionCertificates.CertificateBase64Key, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Base64_Value_That_Is_Not_Base64_Refuses_And_Says_How_To_Encode() // [FR-3.1]
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config((KeyRingProtectionCertificates.CertificateBase64Key, "obviously-not-base64!!")), Now));

        Assert.Contains(KeyRingProtectionCertificates.CertificateBase64Key, ex.Message, StringComparison.Ordinal);
        Assert.Contains("base64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // The load-bearing one: the base64 route must not be a laxer door into the same ring. A certificate with no
    // private key is refused identically to the file route, which is what stops a deployment writing keys it
    // could never read back.
    [Fact]
    public void A_Base64_Certificate_With_No_Private_Key_Refuses_Like_The_File_Route() // [FR-3.1]
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=public-only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(Now.AddDays(-1)), new DateTimeOffset(Now.AddDays(365)));

        var publicOnly = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config((KeyRingProtectionCertificates.CertificateBase64Key, publicOnly)), Now));

        Assert.Contains("clé privée", ex.Message, StringComparison.Ordinal);
    }

    // FR-3.2 rotation has to be reachable on the same platforms, or the base64 route is a one-way door.
    [Fact]
    public void A_Retained_Generation_May_Be_Supplied_As_Base64() // [FR-3.2]
    {
        using var dir = new TempDir();

        var resolution = KeyRingProtectionCertificates.Resolve(
            Config(
                (KeyRingProtectionCertificates.CertificatePathKey, WriteCertificate(dir.Path, "active")),
                ($"{KeyRingProtectionCertificates.PreviousCertificatesSection}:0:Base64", CertificateBase64())),
            Now);

        Assert.Equal(2, resolution.Decryptors.Count);
        Assert.All(resolution.Decryptors, c => Assert.True(c.HasPrivateKey));
    }

    [Fact]
    public void A_Retained_Generation_Naming_Both_Forms_Refuses() // [FR-3.2]
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtectionCertificates.Resolve(
                Config(
                    (KeyRingProtectionCertificates.CertificatePathKey, WriteCertificate(dir.Path, "active")),
                    ($"{KeyRingProtectionCertificates.PreviousCertificatesSection}:0:Path",
                        WriteCertificate(dir.Path, "previous")),
                    ($"{KeyRingProtectionCertificates.PreviousCertificatesSection}:0:Base64", CertificateBase64())),
                Now));

        Assert.Contains("Base64", ex.Message, StringComparison.Ordinal);
    }

    // The other direction: neither key set is still « pas de certificat », not a refusal — a Windows install
    // protects the ring with DPAPI and configures neither.
    [Fact]
    public void Neither_Delivery_Route_Configured_Is_Still_Not_Configured() // [FR-3.1]
    {
        var resolution = KeyRingProtectionCertificates.Resolve(Config(), Now);

        Assert.False(resolution.IsConfigured);
        Assert.Empty(resolution.Decryptors);
    }

    // No certificate configured at all is the pre-FR-3.1 state and stays legal on the profiles that do not
    // require one — the Windows install protects the same ring with DPAPI instead.
    [Fact]
    public void No_Certificate_Configured_Is_Not_An_Error_In_Itself()
    {
        var resolution = KeyRingProtectionCertificates.Resolve(Config(), Now);

        Assert.False(resolution.IsConfigured);
        Assert.Empty(resolution.Decryptors);
    }

    // ---- Which deployments must supply one -------------------------------------------------------

    [Theory]
    [InlineData("HostedMultiTenant", true)]
    [InlineData("SelfHostedLan", false)]
    public void Only_The_Hosted_Multi_Tenant_Kind_Requires_A_Certificate(string profileName, bool required) // [FR-3.1]
    {
        var profile = ClinicManagement.Infrastructure.Deployment.DeploymentProfile.Resolve(
            Config(("Deployment:Profile", profileName)));

        Assert.Equal(required, LocalDataProtection.RequiresProtectingCertificate(profile));
    }

    // Development is exempt on MinioCredentials.TolerateUnconfigured's precedent — appsettings.Development.json
    // selects HostedMultiTenant deliberately and no developer has a PKCS#12, so failing there would break
    // `dotnet run` and `dotnet ef` on a fresh clone for everyone.
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void An_Unprotected_Ring_Is_Tolerated_In_Development_Only(string environment, bool tolerated) // [FR-3.1]
    {
        var previous = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
            Assert.Equal(tolerated, LocalDataProtection.TolerateUnprotectedKeyRing(Config()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previous);
        }
    }

    // ---- The generation arithmetic (FR-3.1's coverage figure, FR-3.9's stamp) --------------------

    private static IDataProtectionProvider EphemeralProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("tests").UseEphemeralDataProtectionProvider();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    /// <summary>
    /// A provider over its <b>own</b> persisted key ring, so it has a real, distinct key id.
    /// <see cref="EphemeralProvider"/> does not: its ring leaves the default key id at <c>Guid.Empty</c>, so two
    /// ephemeral providers write byte-identical payload headers.
    /// </summary>
    private static IDataProtectionProvider PersistedProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("tests")
            .PersistKeysToFileSystem(new DirectoryInfo(new TempDirKeeper().Path));
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public void Ciphertext_Written_By_The_Current_Ring_Is_Covered_By_It() // [FR-3.1]
    {
        var protector = EphemeralProvider().CreateProtector("purpose");
        var generation = DataProtectionKeyGeneration.Current(protector);

        Assert.True(generation.Covers(protector.Protect("secret")));
    }

    // ⚠️ The load-bearing direction. `verify-schema`'s zero authorises DELETING the old key files, so a payload
    // this cannot read must count as « still to do » — the safe answer — and never as covered.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not base64url at all !!")]
    [InlineData("AAAA")]
    public void Unreadable_Ciphertext_Is_Never_Reported_As_Covered(string? value) // [FR-3.1]
    {
        var generation = DataProtectionKeyGeneration.Current(EphemeralProvider().CreateProtector("purpose"));

        Assert.False(generation.Covers(value));
    }

    // Two rings, two generations: ciphertext from one is not covered by the other. Without this the coverage
    // figure would read zero for everything and the verb would appear to have finished before it started.
    //
    // ⚠️ Two PERSISTED rings, and that is not incidental. `UseEphemeralDataProtectionProvider` leaves the key
    // ring's default key id at `Guid.Empty`, so two ephemeral providers write the identical 20-byte header and
    // this assertion fails against perfectly correct code — which is exactly what happened when it was first
    // written with ephemeral providers. Every deployment that runs this check persists its ring to a volume, so
    // a persisted pair is also the arrangement production actually has.
    [Fact]
    public void Ciphertext_From_Another_Ring_Is_Not_Covered() // [FR-3.1]
    {
        var foreign = PersistedProvider().CreateProtector("purpose").Protect("secret");
        var generation = DataProtectionKeyGeneration.Current(PersistedProvider().CreateProtector("purpose"));

        Assert.False(generation.Covers(foreign));
    }

    // The purpose does not change the key id — the generation is a property of the RING. This is what lets one
    // probe protector answer for all six families instead of six.
    [Fact]
    public void The_Generation_Is_A_Property_Of_The_Ring_And_Not_Of_The_Purpose() // [FR-3.1]
    {
        var provider = EphemeralProvider();
        var one = DataProtectionKeyGeneration.Current(provider.CreateProtector("first"));

        Assert.True(one.Covers(provider.CreateProtector("second").Protect("secret")));
    }

    // ⚠️ FR-3.9's silent trap: `IKey.KeyId` and the id inside a payload are the SAME 16 bytes in the SAME order,
    // and rendering one as a canonical GUID would byte-swap three fields — so the marker and the stamp would
    // never match and every restore would be refused as a mismatch that is not real.
    [Fact]
    public void A_Key_Id_Renders_Identically_However_It_Was_Obtained() // [FR-3.9]
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("tests")
            .PersistKeysToFileSystem(new DirectoryInfo(new TempDirKeeper().Path));
        using var provider = services.BuildServiceProvider();

        var generation = DataProtectionKeyGeneration.Current(
            provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose"));
        var keyManager = provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>();

        var rendered = keyManager.GetAllKeys().Select(k => DataProtectionKeyGeneration.IdOf(k.KeyId)).ToList();

        Assert.Contains(generation.Id, rendered);
        Assert.True(generation.IsAmong(rendered));
    }

    /// <summary>A directory that outlives the test method, for the key-ring case above.</summary>
    private sealed class TempDirKeeper
    {
        public string Path { get; } =
            Directory.CreateDirectory(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hshb-ring-" + Guid.NewGuid().ToString("N"))).FullName;
    }
}
