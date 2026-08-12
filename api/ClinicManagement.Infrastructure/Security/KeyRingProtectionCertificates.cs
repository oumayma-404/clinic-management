using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Resolves the certificate the Data Protection key ring is encrypted with, and the previous generations kept
/// so their ciphertext stays readable (<c>hosted-security-hardening</c> FR-3.1 / FR-3.2).
///
/// <para><b>Why a certificate and not the framework's own at-rest protection.</b> Supplying a custom key
/// repository — which <see cref="LocalDataProtection"/> does, because the ring has to live on a named volume —
/// <i>disables</i> the automatic key encryption. So the ring sat in cleartext on that volume for the life of the
/// hosted profile: the master keys that decrypt every clinic's reminder credentials, every console second factor
/// and (since Part A) every clinic administrator's, readable off a stolen disk or a copied volume with no key at
/// all. Windows installs are covered by DPAPI; a Linux container has no equivalent, which is why the deployment
/// supplies one.</para>
///
/// <para>⚠️ <b>The active certificate must appear in the decryptor set too, not only in the encryptor.</b> With
/// only <c>ProtectKeysWithCertificate</c> configured the framework resolves the private key for <i>decryption</i>
/// by looking the thumbprint up in the machine's certificate store — which in a Linux container holds nothing.
/// The ring would then write keys it could not read back on the next restart. That is why
/// <see cref="Decryptors"/> includes <see cref="Active"/>.</para>
///
/// <para>⚠️ <b>A configured-but-unusable certificate refuses startup in every profile</b>, rather than falling
/// back to a cleartext ring: an operator who stated the intention and got a silent no-op would believe the ring
/// is protected, which is worse than never having configured one. What « unusable » means is deliberately narrow —
/// unreadable, or carrying no private key. An <i>expired</i> one still decrypts perfectly well and is reported
/// rather than refused, because taking a whole deployment down on a date nobody watched is not a security gain.</para>
/// </summary>
public static class KeyRingProtectionCertificates
{
    /// <summary>PKCS#12 file protecting the key ring. Its password key is <see cref="CertificatePasswordKey"/>.</summary>
    public const string CertificatePathKey = "DataProtection:CertificatePath";

    /// <summary>Password for <see cref="CertificatePathKey"/>. Empty is legitimate for an unprotected PKCS#12.</summary>
    public const string CertificatePasswordKey = "DataProtection:CertificatePassword";

    /// <summary>
    /// Superseded certificates retained so ciphertext written under them still opens (FR-3.2), as
    /// <c>DataProtection:PreviousCertificates:0:{Path,Password}</c>. <b>Two generations</b> is what
    /// <c>deploy/KEY-CUSTODY.md</c> tells operators to keep.
    /// </summary>
    public const string PreviousCertificatesSection = "DataProtection:PreviousCertificates";

    /// <summary>How many superseded generations the operator guide says to keep. Stated for FR-3.2.</summary>
    public const int RecommendedRetainedGenerations = 2;

    /// <summary>Days of remaining life below which the resolution reports a warning.</summary>
    public const int ExpiryWarningDays = 30;

    /// <summary>
    /// What the deployment configured, already loaded. <see cref="Active"/> is null when no certificate is
    /// configured at all — the pre-FR-3.1 state, and still correct on a Windows install where DPAPI protects
    /// the ring instead.
    /// </summary>
    public sealed record Resolution(
        X509Certificate2? Active,
        IReadOnlyList<X509Certificate2> Decryptors,
        IReadOnlyList<string> Warnings)
    {
        public bool IsConfigured => Active is not null;
    }

    /// <summary>
    /// Loads the active certificate and every retained generation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A path is configured and the file is missing, unreadable, or carries no private key.
    /// </exception>
    public static Resolution Resolve(IConfiguration configuration, DateTime nowUtc)
    {
        var activePath = configuration[CertificatePathKey];

        if (string.IsNullOrWhiteSpace(activePath))
        {
            return new Resolution(null, Array.Empty<X509Certificate2>(), Array.Empty<string>());
        }

        var warnings = new List<string>();
        var active = Load(activePath, configuration[CertificatePasswordKey], "active", CertificatePathKey);

        // The active certificate leads the decryptor set — see the ⚠️ on the class.
        var decryptors = new List<X509Certificate2> { active };

        var previous = configuration.GetSection(PreviousCertificatesSection).GetChildren().ToList();
        for (var i = 0; i < previous.Count; i++)
        {
            var path = previous[i]["Path"];
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"{PreviousCertificatesSection}:{i} n'indique aucun « Path ». Une génération retenue sans "
                    + "fichier ne déchiffre rien : indiquez le chemin du certificat précédent ou supprimez "
                    + "l'entrée.");
            }

            decryptors.Add(Load(path, previous[i]["Password"], $"génération retenue n° {i}",
                $"{PreviousCertificatesSection}:{i}:Path"));
        }

        if (previous.Count > RecommendedRetainedGenerations)
        {
            warnings.Add(
                $"{previous.Count} générations de certificat sont retenues ; le guide d'exploitation en "
                + $"recommande {RecommendedRetainedGenerations}. Exécutez « reprotect-secrets » puis retirez "
                + "les plus anciennes (deploy/KEY-CUSTODY.md).");
        }

        foreach (var certificate in decryptors)
        {
            if (certificate.NotAfter.ToUniversalTime() <= nowUtc)
            {
                warnings.Add(
                    $"Le certificat {Describe(certificate)} a expiré le "
                    + $"{certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}. Il déchiffre encore, mais faites "
                    + "tourner la clé : voir deploy/KEY-CUSTODY.md.");
            }
            else if ((certificate.NotAfter.ToUniversalTime() - nowUtc).TotalDays <= ExpiryWarningDays)
            {
                warnings.Add(
                    $"Le certificat {Describe(certificate)} expire le "
                    + $"{certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}. Préparez la rotation "
                    + "(deploy/KEY-CUSTODY.md).");
            }
        }

        return new Resolution(active, decryptors, warnings);
    }

    private static X509Certificate2 Load(string path, string? password, string role, string settingKey)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Le certificat de protection du trousseau ({role}) est introuvable : « {path} ». "
                + $"Il est nommé par {settingKey}. Sans lui les clés de protection des données ne peuvent être "
                + "ni chiffrées ni relues — voir deploy/KEY-CUSTODY.md.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = new X509Certificate2(File.ReadAllBytes(path), password);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Le certificat de protection du trousseau ({role}) est illisible : « {path} ». "
                + $"Vérifiez qu'il s'agit d'un fichier PKCS#12 et que {settingKey.Replace("Path", "Password")} "
                + $"est correct. Détail : {ex.Message}", ex);
        }

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"Le certificat de protection du trousseau ({role}) « {path} » ne contient pas de clé privée. "
                + "La clé privée est ce qui permet de RELIRE le trousseau ; sans elle le déploiement écrirait "
                + "des clés qu'il ne saurait plus déchiffrer au redémarrage suivant.");
        }

        return certificate;
    }

    private static string Describe(X509Certificate2 certificate) =>
        $"« {certificate.Subject} » ({certificate.Thumbprint})";
}
