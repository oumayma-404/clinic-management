using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClinicManagement.Infrastructure.Security;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The one authority on "is this deployment's internal root usable" (hosted-security-hardening Part 2, FR-2.5).
///
/// <para><b>What earns these cases their place.</b> The verdicts are not decoration: three of them —
/// <c>Absent</c>, <c>Unreadable</c>, <c>NotYetValid</c> — are cases a presence check reports as healthy, and
/// FR-2.5 requires each to refuse and say which. The last is the sharpest: a certificate whose validity starts
/// next week is a real file of the right shape, so anything short of parsing it and comparing the window calls
/// it fine and the deployment fails at its first query instead of at startup.</para>
///
/// <para>⚠️ Every case supplies its own instant. The validity window is the thing under test, so a test reading
/// the clock would agree with a clock-reading implementation by construction — the trap
/// <c>ClinicClockTests</c> documents.</para>
/// </summary>
public class InternalCertificateTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_Unconfigured_Path_Is_Not_Configured_Rather_Than_Absent()
    {
        foreach (var path in new[] { null, "", "   " })
        {
            var inspection = InternalCertificate.Inspect(path, Now, NeverRead());

            Assert.Equal(InternalCertificate.Verdict.NotConfigured, inspection.Verdict);
            Assert.False(inspection.IsUsable);
            Assert.Null(inspection.DaysRemaining);
        }
    }

    // The two must be distinguishable: "you did not set it" and "you set it to something that is not there" have
    // different fixes, and the second usually means the volume is not mounted.
    [Fact]
    public void A_Configured_Path_That_Names_Nothing_Is_Absent_And_The_Message_Names_The_File()
    {
        var inspection = InternalCertificate.Inspect(
            "/certs/ca.crt", Now, new InternalCertificate.Store(_ => false, _ => Array.Empty<byte>()));

        Assert.Equal(InternalCertificate.Verdict.Absent, inspection.Verdict);
        Assert.Contains("/certs/ca.crt", inspection.Detail, StringComparison.Ordinal);
        Assert.Null(inspection.DaysRemaining);
    }

    [Fact]
    public void A_File_That_Is_Not_A_Certificate_Is_Unreadable_Rather_Than_Throwing()
    {
        var inspection = InternalCertificate.Inspect(
            "/certs/ca.crt", Now, StoreReturning(Encoding.UTF8.GetBytes("this is not a certificate")));

        Assert.Equal(InternalCertificate.Verdict.Unreadable, inspection.Verdict);
        Assert.Contains("/certs/ca.crt", inspection.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Certificate_Inside_Its_Window_Is_Usable_And_Reports_Whole_Days_Remaining()
    {
        using var certificate = SelfSigned(Now.AddDays(-1), Now.AddDays(3650));
        var inspection = InternalCertificate.Inspect("/certs/ca.crt", Now, StoreReturning(Pem(certificate)));

        Assert.Equal(InternalCertificate.Verdict.Usable, inspection.Verdict);
        Assert.True(inspection.IsUsable);
        Assert.Equal(3650, inspection.DaysRemaining);
    }

    // The case a presence check calls healthy. A container whose clock is behind, or a certificate minted for a
    // later cut-over, is a real file of exactly the right shape that no hop will accept.
    [Fact]
    public void A_Certificate_Whose_Validity_Has_Not_Started_Is_Refused_And_Named()
    {
        using var certificate = SelfSigned(Now.AddDays(7), Now.AddDays(3650));
        var inspection = InternalCertificate.Inspect("/certs/ca.crt", Now, StoreReturning(Pem(certificate)));

        Assert.Equal(InternalCertificate.Verdict.NotYetValid, inspection.Verdict);
        Assert.False(inspection.IsUsable);
        Assert.Contains("pas encore valide", inspection.Detail, StringComparison.Ordinal);
    }

    // Negative rather than clamped to zero: « expiré il y a 12 jours » and « expire aujourd'hui » are different
    // situations, and a floor at 0 would merge them.
    [Fact]
    public void An_Expired_Certificate_Is_Refused_And_Its_Days_Remaining_Go_Negative()
    {
        using var certificate = SelfSigned(Now.AddDays(-100), Now.AddDays(-12));
        var inspection = InternalCertificate.Inspect("/certs/ca.crt", Now, StoreReturning(Pem(certificate)));

        Assert.Equal(InternalCertificate.Verdict.Expired, inspection.Verdict);
        Assert.Equal(-12, inspection.DaysRemaining);
    }

    // PEM is what `openssl req -x509` writes, which is what deploy/certs/issue.sh runs — and whether the
    // byte[] constructor accepts a PEM body is platform-dependent, so this is the case that would have failed
    // on one operating system and passed on another.
    [Fact]
    public void A_Pem_Encoded_Root_Loads_As_A_Trust_Anchor()
    {
        using var certificate = SelfSigned(Now.AddDays(-1), Now.AddDays(3650));

        using var loaded = InternalCertificate.TryLoad("/certs/ca.crt", StoreReturning(Pem(certificate)));

        Assert.NotNull(loaded);
        Assert.Equal(certificate.Thumbprint, loaded!.Thumbprint);
    }

    [Fact]
    public void TryLoad_Answers_Null_Rather_Than_Throwing_On_Anything_Unusable()
    {
        Assert.Null(InternalCertificate.TryLoad(null, NeverRead()));
        Assert.Null(InternalCertificate.TryLoad(
            "/certs/ca.crt", new InternalCertificate.Store(_ => false, _ => Array.Empty<byte>())));
        Assert.Null(InternalCertificate.TryLoad(
            "/certs/ca.crt", StoreReturning(Encoding.UTF8.GetBytes("nope"))));
    }

    private static InternalCertificate.Store StoreReturning(byte[] bytes) => new(_ => true, _ => bytes);

    /// <summary>A store whose read would fail the test if it were reached — the path is never opened.</summary>
    private static InternalCertificate.Store NeverRead() =>
        new(_ => throw new InvalidOperationException("existence must not be probed for an unconfigured path"),
            _ => throw new InvalidOperationException("nothing should be read"));

    private static X509Certificate2 SelfSigned(DateTime notBefore, DateTime notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Clinic Management internal CA test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static byte[] Pem(X509Certificate2 certificate) =>
        Encoding.UTF8.GetBytes(certificate.ExportCertificatePem());
}
