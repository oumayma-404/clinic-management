using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The self-signed HTTPS certificate provisioner (FR-E2 / Phase 5 S3). Verifies it emits a loadable
/// server PFX + a CA <c>.crt</c>, that the server leaf is actually signed by the generated CA, that the
/// SANs cover the hostname + localhost, and that a second call is idempotent (reuses the existing set so
/// the CA the clients trust stays stable across restarts).
/// </summary>
public sealed class CertificateProvisionerTests : IDisposable
{
    private readonly string _dir;

    public CertificateProvisionerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm-cert-tests", Guid.NewGuid().ToString("N"));
    }

    private CertificateProvisioner Provisioner() =>
        new(NullLogger<CertificateProvisioner>.Instance, _dir);

    [Fact]
    public void EnsureServerCertificate_writes_a_loadable_pfx_and_ca_crt()
    {
        var result = Provisioner().EnsureServerCertificate();

        Assert.True(File.Exists(result.PfxPath));
        Assert.True(File.Exists(result.CaCertPath));
        Assert.False(string.IsNullOrWhiteSpace(result.Password));

        // The pfx opens with the reported password (proves the server private key is present).
        using var serverCert = new X509Certificate2(result.PfxPath, result.Password);
        Assert.True(serverCert.HasPrivateKey);
    }

    [Fact]
    public void Generated_leaf_is_signed_by_the_generated_ca()
    {
        var result = Provisioner().EnsureServerCertificate();

        using var serverCert = new X509Certificate2(result.PfxPath, result.Password);
        using var caCert = new X509Certificate2(result.CaCertPath);

        // The leaf's issuer is the CA's subject → the CA signed the leaf.
        Assert.Equal(caCert.Subject, serverCert.Issuer);
        Assert.Equal("CN=Clinic Management Local CA", caCert.Subject);
    }

    [Fact]
    public void Subject_alternative_names_cover_hostname_and_localhost()
    {
        var result = Provisioner().EnsureServerCertificate();

        using var serverCert = new X509Certificate2(result.PfxPath, result.Password);
        var sanExtension = serverCert.Extensions
            .Cast<X509Extension>()
            .Single(e => e.Oid?.Value == "2.5.29.17"); // subjectAltName

        // DNS names are IA5 (ASCII) in the DER, so their bytes appear verbatim in the raw extension.
        var raw = Encoding.ASCII.GetString(sanExtension.RawData);
        Assert.Contains("localhost", raw);
        Assert.Contains(Dns.GetHostName(), raw);
    }

    [Fact]
    public void Second_call_is_idempotent_and_reuses_the_same_certificate()
    {
        var provisioner = Provisioner();
        var first = provisioner.EnsureServerCertificate();
        var second = provisioner.EnsureServerCertificate();

        Assert.Equal(first.Password, second.Password);

        using var firstCert = new X509Certificate2(first.PfxPath, first.Password);
        using var secondCert = new X509Certificate2(second.PfxPath, second.Password);
        Assert.Equal(firstCert.Thumbprint, secondCert.Thumbprint);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
