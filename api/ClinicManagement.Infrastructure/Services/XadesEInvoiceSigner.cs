using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Signs a TEIF XML document in-process with the clinic's qualified certificate (FR-2): an enveloped
/// XMLDSig signature using RSA-SHA256 and SHA-256 digests, with the signing certificate embedded in
/// <c>KeyInfo</c>. The certificate + password come from the per-install <c>.local/</c> store (see
/// <see cref="TtnConfig"/>), never from the DB or committed config.
/// <para>
/// The exact XAdES profile TTN mandates (XAdES-B/BES qualifying-properties, canonicalization) is a spec
/// Open Question (#3) that cannot be pinned in-repo; this produces a valid enveloped XMLDSig signature,
/// the base every XAdES profile builds on. Extend with QualifyingProperties once the profile is confirmed.
/// </para>
/// </summary>
public class XadesEInvoiceSigner : IEInvoiceSigner
{
    private const string SignatureMethodRsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    private const string DigestMethodSha256 = "http://www.w3.org/2001/04/xmlenc#sha256";

    private readonly IConfiguration _configuration;
    private readonly ILogger<XadesEInvoiceSigner> _logger;

    public XadesEInvoiceSigner(IConfiguration configuration, ILogger<XadesEInvoiceSigner> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public SignedEInvoiceResult Sign(string teifXml)
    {
        if (string.IsNullOrWhiteSpace(teifXml))
        {
            throw new ArgumentException("Le XML TEIF à signer est vide.", nameof(teifXml));
        }

        var certPath = TtnConfig.CertificatePath(_configuration);
        if (!File.Exists(certPath))
        {
            throw new InvalidOperationException(
                "Certificat de signature électronique introuvable. Déposez le certificat qualifié (PFX) dans le dossier .local/ avant l'envoi à El Fatoora.");
        }

        var password = TtnConfig.CertificatePassword(_configuration);

        using var certificate = new X509Certificate2(
            certPath,
            password,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

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
