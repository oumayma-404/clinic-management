using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Signs a TEIF XML document in-process with a qualified certificate (FR-2): an enveloped XMLDSig signature
/// using RSA-SHA256 and SHA-256 digests, with the signing certificate embedded in <c>KeyInfo</c>.
/// <para>
/// The exact XAdES profile TTN mandates (XAdES-B/BES qualifying-properties, canonicalization) is a spec
/// Open Question (#3) that cannot be pinned in-repo; this produces a valid enveloped XMLDSig signature,
/// the base every XAdES profile builds on. Extend with QualifyingProperties once the profile is confirmed.
/// </para>
/// <para>
/// ⚠️ <b>The single-cert-per-install constraint this class used to carry is closed</b> (multi-tenant-cloud
/// US-4). The certificate arrives as a resolved <see cref="ResolvedTtnIdentity"/> — the clinic's own where it
/// has one — so this class no longer reads configuration, no longer touches the disk, and no longer decides
/// whose identity anything is signed with. That decision lives in <see cref="ITtnIdentityProvider"/>, once.
/// What is left here is the crypto, which is a pure synchronous transform over bytes it is handed.
/// </para>
/// </summary>
public class XadesEInvoiceSigner : IEInvoiceSigner
{
    private const string SignatureMethodRsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    private const string DigestMethodSha256 = "http://www.w3.org/2001/04/xmlenc#sha256";

    private readonly ILogger<XadesEInvoiceSigner> _logger;

    public XadesEInvoiceSigner(ILogger<XadesEInvoiceSigner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens the PFX. <c>EphemeralKeySet</c> only (no on-disk key persistence), and NOT <c>Exportable</c> — a
    /// signing-only key never needs marshalling out of the key object.
    ///
    /// <para>⚠️ The wrap is the point (review finding 22). A <b>wrong PFX password</b> is the single most likely
    /// misconfiguration of a hand-provisioned identity, and unwrapped it threw a bare
    /// <c>CryptographicException</c> into <c>EInvoiceService</c>'s generic catch — which overwrote the invoice row's
    /// reason with « Erreur lors de l'envoi à El Fatoora. », telling the operator nothing about which secret to
    /// re-enter, on a queue that keeps retrying. Raised as an identity failure instead, it parks the row with a
    /// sentence naming the cause.</para>
    /// </summary>
    private X509Certificate2 LoadCertificate(ResolvedTtnIdentity identity)
    {
        try
        {
            return new X509Certificate2(
                identity.CertificateBytes,
                identity.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(
                ex, "Could not open the TTN signing certificate ({Source} identity).", identity.Source);
            throw new TtnIdentityUnavailableException(
                "Certificat de signature illisible ou mot de passe incorrect. Vérifiez le fichier PFX du cabinet "
                + "et son mot de passe dans les paramètres El Fatoora.", ex);
        }
    }

    public SignedEInvoiceResult Sign(string teifXml, ResolvedTtnIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(teifXml))
        {
            throw new ArgumentException("Le XML TEIF à signer est vide.", nameof(teifXml));
        }

        if (identity.CertificateBytes.Length == 0)
        {
            throw new InvalidOperationException(
                "Certificat de signature électronique introuvable ou vide. Vérifiez le certificat qualifié (PFX) du cabinet avant l'envoi à El Fatoora.");
        }

        using var certificate = LoadCertificate(identity);

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa == null)
        {
            throw new InvalidOperationException("Le certificat fourni ne contient pas de clé privée RSA utilisable pour la signature.");
        }

        var xmlDocument = new XmlDocument { PreserveWhitespace = true };
        xmlDocument.LoadXml(teifXml);

        var signedXml = new SignedXml(xmlDocument) { SigningKey = rsa };
        signedXml.SignedInfo!.SignatureMethod = SignatureMethodRsaSha256;

        var reference = new Reference { Uri = string.Empty, DigestMethod = DigestMethodSha256 };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificate));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var signatureElement = signedXml.GetXml();
        xmlDocument.DocumentElement!.AppendChild(xmlDocument.ImportNode(signatureElement, true));

        _logger.LogInformation("Signed TEIF XML with certificate thumbprint {Thumbprint}", certificate.Thumbprint);

        return new SignedEInvoiceResult { SignedXml = xmlDocument.OuterXml };
    }
}
