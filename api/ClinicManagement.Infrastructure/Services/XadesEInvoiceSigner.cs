using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
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

        // EphemeralKeySet only (no on-disk key persistence); NOT Exportable — a signing-only key never needs
        // to be marshalled out of the key object.
        using var certificate = new X509Certificate2(
            identity.CertificateBytes,
            identity.CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);

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
