using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ClinicManagement.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// Verifies the <c>IFileStorage</c> backend is chosen by the deployment's <c>UsesDiskStorage</c>
/// capability: a clinic's own PC → local disk; a hosted deployment → MinIO (FR-C1/C2). Resolves the seam from a real
/// <c>AddInfrastructure</c> registration without touching any external service.
/// </summary>
public class InfrastructureFileStorageWiringTests
{
    private const string DummyConnection = "Host=localhost;Database=clinic;Username=u;Password=p";

    private static IFileStorage ResolveFileStorage(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<IFileStorage>();
    }

    // [AC-1] Local mode resolves the local-disk backend (no MinIO configured).
    [Fact]
    public void LocalMode_Resolves_LocalDiskFileStorage()
    {
        var fileStorage = ResolveFileStorage(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Local",
            ["ConnectionStrings:DefaultConnection"] = DummyConnection,
            ["FileStorage:BasePath"] = Path.Combine(Path.GetTempPath(), "clinic-wiring-tests"),
        });

        Assert.IsType<LocalDiskFileStorage>(fileStorage);
    }

    // [AC-2] A deployment that does NOT store blobs on its own disk resolves the MinIO backend.
    //
    // ⚠️ This used to say `["Auth:Mode"] = "Cloud"`, leaning on the derivation that turned a non-Local auth mode
    // into the CloudBrowser profile. That kind is retired, so the shorthand no longer resolves to anything — and
    // it was always the wrong thing to assert here anyway: the branch reads `UsesDiskStorage`, not the auth
    // mode, so naming the profile is what makes the test say the same thing the code does.
    [Fact]
    public void HostedDeployment_With_Minio_Resolves_MinioFileStorage()
    {
        var fileStorage = ResolveFileStorage(new Dictionary<string, string?>
        {
            [DeploymentProfile.ProfileKey] = nameof(DeploymentKind.HostedMultiTenant),
            ["ConnectionStrings:DefaultConnection"] = DummyConnection,
            // ⚠️ Both of these are required by this profile and neither is about file storage.
            // `AddInfrastructure` wires Data Protection before the storage seam, and HostedMultiTenant refuses
            // without a durable key-ring home (US-6) AND without a certificate to encrypt that ring with
            // (hosted-security-hardening FR-3.1). The old `Auth:Mode = Cloud` shorthand met neither because
            // CloudBrowser was exempt from both — so naming the real profile is what makes this test pay the
            // real profile's price, which is the whole reason to name it.
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "clinic-wiring-tests-keyring"),
            ["DataProtection:CertificateBase64"] = ThrowawayProtectingCertificate(),
            ["MinIO:Endpoint"] = "localhost:9000",
            ["MinIO:AccessKey"] = "access",
            ["MinIO:SecretKey"] = "secret",
        });

        Assert.IsType<MinioFileStorage>(fileStorage);
    }
    /// <summary>
    /// A self-signed PKCS#12, in memory, purely so `AddInfrastructure` can be resolved at all under the hosted
    /// profile. Nothing here asserts anything about it — `KeyRingProtectionTests` owns that surface.
    /// </summary>
    private static string ThrowawayProtectingCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=file-storage-wiring-tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3650));

        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));
    }
}
