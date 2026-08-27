using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Paths + password for a provisioned server certificate.
/// </summary>
public sealed record ServerCertificateResult(string PfxPath, string CaCertPath, string Password);

/// <summary>
/// Self-generates the LAN HTTPS trust material on first Local-mode boot (FR-E2, Phase 5 S3): a self-signed
/// <b>CA</b> and a <b>server leaf</b> signed by it, whose SANs cover the machine hostname, <c>localhost</c>,
/// and every non-loopback IPv4 address so LAN clients (and the server PC via <c>localhost</c>, AC-2.5) can
/// connect by IP or name. The server key material is exported to <c>.local/server.pfx</c> (protected by a
/// random per-install password stored in <c>.local/server-cert-password</c>), and the public CA is exported
/// to <c>.local/ca.crt</c> for the client installer to import into the Windows trust store (S7).
///
/// Keeping certificate generation in testable C# — rather than an installer script — lets the SAN coverage,
/// CA→leaf signing, and idempotency be unit-tested. Idempotent: an existing, loadable set is reused as-is so
/// the cert (and thus the CA the clients trust) is stable across restarts.
/// </summary>
public sealed class CertificateProvisioner
{
    private const string PfxFileName = "server.pfx";
    private const string CaCertFileName = "ca.crt";
    private const string PasswordFileName = "server-cert-password";

    private readonly ILogger<CertificateProvisioner> _logger;
    private readonly string _localDir;

    public CertificateProvisioner(ILogger<CertificateProvisioner> logger, string? localDir = null)
    {
        _logger = logger;
        _localDir = localDir ?? LocalInstallPaths.LocalDir;
    }

    /// <summary>
    /// Returns the existing server certificate set if one is already present and loadable; otherwise
    /// generates a fresh CA + server certificate and persists them under <c>.local/</c>.
    /// </summary>
    public ServerCertificateResult EnsureServerCertificate()
    {
        Directory.CreateDirectory(_localDir);

        var pfxPath = Path.Combine(_localDir, PfxFileName);
        var caCertPath = Path.Combine(_localDir, CaCertFileName);
        var passwordPath = Path.Combine(_localDir, PasswordFileName);

        if (TryLoadExisting(pfxPath, caCertPath, passwordPath, out var existing))
        {
            _logger.LogInformation("Reusing existing server certificate at {PfxPath}.", pfxPath);
            return existing!;
        }

        var password = GeneratePassword();
        Generate(pfxPath, caCertPath, password);
        File.WriteAllText(passwordPath, password);

        _logger.LogInformation(
            "Generated a self-signed CA + server certificate ({PfxPath}); exported CA to {CaCertPath}.",
            pfxPath, caCertPath);

        return new ServerCertificateResult(pfxPath, caCertPath, password);
    }

    private static bool TryLoadExisting(string pfxPath, string caCertPath, string passwordPath, out ServerCertificateResult? result)
    {
        result = null;
        if (!File.Exists(pfxPath) || !File.Exists(caCertPath) || !File.Exists(passwordPath))
        {
            return false;
        }

        var password = File.ReadAllText(passwordPath).Trim();
        try
        {
            // Prove the pfx actually opens with the stored password before trusting the set.
            using var _ = new X509Certificate2(pfxPath, password);
            result = new ServerCertificateResult(pfxPath, caCertPath, password);
            return true;
        }
        catch (CryptographicException)
        {
            return false; // corrupt/mismatched — fall through and regenerate
        }
    }

    private void Generate(string pfxPath, string caCertPath, string password)
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1); // small backdate for client clock skew
        var caNotAfter = notBefore.AddYears(10);
        var serverNotAfter = notBefore.AddYears(5);

        // --- Certificate Authority (self-signed, can sign certs) ---
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=Clinic Management Local CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        using var caCert = caRequest.CreateSelfSigned(notBefore, caNotAfter);

        // --- Server leaf (signed by the CA) ---
        using var serverKey = RSA.Create(2048);
        var hostName = Dns.GetHostName();
        var serverRequest = new CertificateRequest(
            $"CN={hostName}", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") /* serverAuth */ }, critical: false));
        serverRequest.CertificateExtensions.Add(BuildSubjectAlternativeNames(hostName));

        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var serverPublic = serverRequest.Create(caCert, notBefore, serverNotAfter, serialNumber);
        using var serverCert = serverPublic.CopyWithPrivateKey(serverKey);

        // Export the server cert (with its private key) as a password-protected PFX for Kestrel.
        var pfxBytes = serverCert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);

        // Export the public CA certificate (DER .crt) for the client installer to import into Root trust.
        File.WriteAllBytes(caCertPath, caCert.Export(X509ContentType.Cert));
    }

    private static X509Extension BuildSubjectAlternativeNames(string hostName)
    {
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback); // 127.0.0.1 — server PC via localhost (AC-2.5)

        // Shared with the trust page, which advertises one of these as the address a phone should use. The
        // two must not drift: an advertised address absent from this SAN set installs the CA and then still
        // fails the TLS handshake. See LanAddresses for why that is one type and not two helpers.
        foreach (var ip in LanAddresses.IPv4())
        {
            sanBuilder.AddIpAddress(ip);
        }

        return sanBuilder.Build();
    }

    private static string GeneratePassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
