using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Reading and judging the deployment's <b>internal root certificate</b> — the one trust anchor every hop
/// inside the perimeter is verified against (hosted-security-hardening Part 2, FR-2.1/FR-2.2/FR-2.6).
/// It is minted by <c>deploy/certs/issue.sh</c> into the <c>internal_certs</c> volume and named by the Npgsql
/// connection string's <c>Root Certificate=</c> and by <c>MinIO:RootCertificate</c>.
///
/// <para><b>One authority, three callers.</b> The API's <c>TransportAssurance</c> refuses to start on a bad
/// one, <c>verify-schema</c> reports its remaining life, and the Minio client verifies the object store's leaf
/// against it. Three readings of "is this certificate usable" would drift into a deployment that boots and
/// then cannot talk to anything — so the verdicts, and the sentences that name them, live here.</para>
///
/// <para><b>Why the verdicts are distinguished rather than folded into a bool.</b> "The file is not there",
/// "I cannot parse it" and "it is not valid until next week" have three different operator actions, and the
/// last one is the case a presence check reports as healthy. FR-2.5 requires each to be named.</para>
///
/// <para>⚠️ <b><see cref="Inspect"/> takes the instant as a parameter</b>, on this codebase's own rule for
/// anything whose answer turns on a date: a validity window is otherwise only testable by waiting.</para>
/// </summary>
public static class InternalCertificate
{
    /// <summary>Config key naming the root the object-store client verifies MinIO's leaf against.</summary>
    public const string MinioRootCertificateKey = "MinIO:RootCertificate";

    private const string PemHeader = "-----BEGIN CERTIFICATE-----";

    /// <summary>
    /// The file-reading seam, on <c>PostgresToolLocator.FileSystem</c>'s pattern and for the same reason:
    /// "this deployment has no certificate" is otherwise untestable, since a developer machine and a CI runner
    /// both have whatever happens to be on disk.
    /// </summary>
    public sealed record Store(Func<string, bool> FileExists, Func<string, byte[]> ReadAllBytes)
    {
        public static readonly Store Real = new(File.Exists, File.ReadAllBytes);
    }

    /// <summary>What is wrong with the internal root, or <see cref="Usable"/> when nothing is.</summary>
    public enum Verdict
    {
        /// <summary>Present, parseable, and inside its validity window.</summary>
        Usable,

        /// <summary>No path was configured at all — the setting is absent or blank.</summary>
        NotConfigured,

        /// <summary>A path was configured and names nothing.</summary>
        Absent,

        /// <summary>The file is there and is not a certificate this runtime can parse.</summary>
        Unreadable,

        /// <summary>Parseable, and its validity has not started yet — the case a presence check calls healthy.</summary>
        NotYetValid,

        /// <summary>Parseable, and expired.</summary>
        Expired,
    }

    /// <summary>
    /// One reading of the internal root. <see cref="DaysRemaining"/> is <c>null</c> whenever the certificate
    /// could not be parsed — "I could not read it" and "0 days left" are different claims and the second must
    /// not stand in for the first.
    /// </summary>
    public sealed record Inspection(
        Verdict Verdict,
        string? Path,
        DateTime? NotBeforeUtc,
        DateTime? NotAfterUtc,
        int? DaysRemaining,
        string Detail)
    {
        public bool IsUsable => Verdict == Verdict.Usable;
    }

    /// <summary>
    /// Judges the root at <paramref name="path"/> as of <paramref name="nowUtc"/>. Never throws: every failure
    /// is a <see cref="Verdict"/> the caller decides what to do about.
    /// </summary>
    public static Inspection Inspect(string? path, DateTime nowUtc, Store? store = null)
    {
        var fs = store ?? Store.Real;
        var trimmed = path?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return new Inspection(Verdict.NotConfigured, null, null, null, null,
                "aucun certificat racine interne n'est configuré");
        }

        if (!fs.FileExists(trimmed))
        {
            return new Inspection(Verdict.Absent, trimmed, null, null, null,
                $"le fichier « {trimmed} » est introuvable");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = Load(fs.ReadAllBytes(trimmed));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException
                                       or ArgumentException or FormatException)
        {
            return new Inspection(Verdict.Unreadable, trimmed, null, null, null,
                $"le fichier « {trimmed} » n'est pas un certificat lisible");
        }

        using (certificate)
        {
            var notBefore = certificate.NotBefore.ToUniversalTime();
            var notAfter = certificate.NotAfter.ToUniversalTime();
            var daysRemaining = (int)Math.Floor((notAfter - nowUtc).TotalDays);

            if (nowUtc < notBefore)
            {
                return new Inspection(Verdict.NotYetValid, trimmed, notBefore, notAfter, daysRemaining,
                    $"le certificat « {trimmed} » n'est pas encore valide (valide à partir du "
                    + $"{notBefore:yyyy-MM-dd HH:mm} UTC)");
            }

            if (nowUtc > notAfter)
            {
                return new Inspection(Verdict.Expired, trimmed, notBefore, notAfter, daysRemaining,
                    $"le certificat « {trimmed} » a expiré le {notAfter:yyyy-MM-dd} UTC");
            }

            return new Inspection(Verdict.Usable, trimmed, notBefore, notAfter, daysRemaining,
                $"valide jusqu'au {notAfter:yyyy-MM-dd} UTC ({daysRemaining} jour(s))");
        }
    }

    /// <summary>
    /// Loads the root for use as a trust anchor, or <c>null</c> when it cannot be read. Callers that must
    /// refuse rather than degrade go through <see cref="Inspect"/> first — this is for the wiring that has
    /// already been told the certificate is usable.
    /// </summary>
    public static X509Certificate2? TryLoad(string? path, Store? store = null)
    {
        var fs = store ?? Store.Real;
        var trimmed = path?.Trim();

        if (string.IsNullOrEmpty(trimmed) || !fs.FileExists(trimmed))
        {
            return null;
        }

        try
        {
            return Load(fs.ReadAllBytes(trimmed));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException
                                       or ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// PEM is handled explicitly rather than left to the <c>byte[]</c> constructor: that overload reads DER
    /// (and PKCS#7/PFX) everywhere, but whether it accepts a PEM body depends on the platform's crypto stack —
    /// so a CA that loads on the Linux container would fail on a Windows developer machine, or the reverse.
    /// <c>openssl req -x509</c> writes PEM, which is what the internal CA is.
    /// </summary>
    private static X509Certificate2 Load(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Contains(PemHeader, StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : new X509Certificate2(bytes);
    }
}
