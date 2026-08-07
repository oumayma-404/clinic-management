using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// XAdES/XMLDSig signer guards (FR-2, edge: certificate missing/invalid).
///
/// <para>⚠️ <b>The positive path is testable now, and it was not before</b> (multi-tenant-cloud US-4). The signer
/// used to read a PFX off disk from per-install configuration, so « the happy path needs a real qualified
/// certificate » was true and the only coverage was fail-fast. It now takes the certificate as
/// <see cref="ResolvedTtnIdentity"/> bytes, so a self-signed pair generated in-process exercises the whole
/// signature — and the case US-4 rests on is asserted directly: <b>the certificate embedded in the signature is
/// the one the caller supplied</b>, which is what « each clinic signs with its own identity » actually means.</para>
/// </summary>
public class XadesEInvoiceSignerTests
{
    private const string Teif = "<TEIF version=\"1.8.8\"><InvoiceBody/></TEIF>";
    private const string CertPassword = "pfx-password";
    private const string DsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    private static XadesEInvoiceSigner Signer() => new(NullLogger<XadesEInvoiceSigner>.Instance);

    /// <summary>A throwaway self-signed RSA pair, exported as a password-protected PFX like a real one.</summary>
    private static (byte[] Pfx, string Thumbprint) SelfSignedPfx(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        return (certificate.Export(X509ContentType.Pfx, CertPassword), certificate.Thumbprint);
    }

    private static ResolvedTtnIdentity Identity(byte[] pfx, string? password = CertPassword) =>
        new(pfx, password, "clinic-user", "clinic-secret", TtnIdentitySource.Clinic);

    private static XmlNode? SelectDsig(string signedXml, string node)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(signedXml);
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("ds", DsigNamespace);
        return document.SelectSingleNode($"//ds:{node}", namespaces);
    }

    private static string EmbeddedThumbprint(SignedEInvoiceResult signed)
    {
        var encoded = SelectDsig(signed.SignedXml, "X509Certificate")!.InnerText;
        using var embedded = new X509Certificate2(Convert.FromBase64String(encoded));
        return embedded.Thumbprint;
    }

    // [FR-2][US-4] The happy path: a valid enveloped signature is appended to the document.
    [Fact]
    public void Sign_Appends_An_Enveloped_Signature()
    {
        var (pfx, _) = SelfSignedPfx("Cabinet Test");

        var signed = Signer().Sign(Teif, Identity(pfx));

        Assert.NotNull(SelectDsig(signed.SignedXml, "Signature"));
        Assert.NotNull(SelectDsig(signed.SignedXml, "SignatureValue"));
    }

    /// <summary>
    /// [US-4] The load-bearing assertion of this part: the signature carries <b>the supplied</b> certificate.
    /// Two clinics signing with two identities must produce two different embedded certificates — a signer that
    /// quietly went on reading one configured path would pass every other test in this file.
    /// </summary>
    [Fact]
    public void Sign_Embeds_The_Certificate_It_Was_Given()
    {
        var first = SelfSignedPfx("Cabinet A");
        var second = SelfSignedPfx("Cabinet B");
        Assert.NotEqual(first.Thumbprint, second.Thumbprint);

        Assert.Equal(first.Thumbprint, EmbeddedThumbprint(Signer().Sign(Teif, Identity(first.Pfx))));
        Assert.Equal(second.Thumbprint, EmbeddedThumbprint(Signer().Sign(Teif, Identity(second.Pfx))));
    }

    /// <summary>
    /// [FR-2][edge] A certificate whose password does not open it fails rather than signing with nothing — and it
    /// fails as an <b>identity</b> problem carrying a French operator sentence, not as a bare
    /// <c>CryptographicException</c> (review finding 22).
    ///
    /// <para>A wrong PFX password is the single most likely misconfiguration of a hand-provisioned identity, and the
    /// raw exception landed in <c>EInvoiceService</c>'s generic catch — which overwrote the invoice row's reason with
    /// « Erreur lors de l'envoi à El Fatoora. », telling the operator nothing about which secret to re-enter, on a
    /// queue that keeps retrying. As a <c>TtnIdentityUnavailableException</c> it instead parks the row with a reason
    /// that names the cause. The original exception is kept as the inner one, so nothing diagnostic is lost.</para>
    /// </summary>
    [Fact]
    public void Sign_With_The_Wrong_Password_Fails_As_An_Unusable_Identity()
    {
        var (pfx, _) = SelfSignedPfx("Cabinet Test");

        var ex = Assert.Throws<TtnIdentityUnavailableException>(
            () => Signer().Sign(Teif, Identity(pfx, "not-the-password")));

        Assert.Contains("mot de passe incorrect", ex.Message);
        Assert.IsAssignableFrom<CryptographicException>(ex.InnerException);
    }

    // [FR-2][edge] No certificate at all fails fast with a clear operator message.
    [Fact]
    public void Sign_Without_Certificate_Bytes_Throws_InvalidOperation()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Signer().Sign(Teif, Identity(Array.Empty<byte>())));

        Assert.Contains("Certificat", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // [FR-2] Empty TEIF input is rejected before any certificate work.
    [Fact]
    public void Sign_Empty_Xml_Throws_Argument()
    {
        var (pfx, _) = SelfSignedPfx("Cabinet Test");

        Assert.Throws<ArgumentException>(() => Signer().Sign("   ", Identity(pfx)));
    }

    // [US-4] No identity is a programming error, not an operator one — it must not surface as a French message.
    [Fact]
    public void Sign_Without_An_Identity_Throws_ArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => Signer().Sign(Teif, null!));
    }
}
