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

    /// <summary>
    /// The same PKCS#12, base64-encoded, for a platform that has no way to put a <b>binary</b> file in front of
    /// the container (FR-3.1, delivery only — the guarantee is unchanged).
    ///
    /// <para><b>Why this exists.</b> <see cref="CertificatePathKey"/> assumes a file mount, which the compose
    /// deployments have and a managed platform generally does not: Render, Fly and App Service all hand secrets to
    /// a process as <b>environment variables</b>, and their "secret file" features store text — so a `.pfx` pasted
    /// into one arrives corrupted, and a PEM loads with no private key and is refused one check later. Without
    /// this key the only remaining routes are a persistent disk plus shell access, or a shell step in the image
    /// that decodes the certificate before the app starts. Both put the deployment's most sensitive material
    /// through more moving parts than reading one setting does.</para>
    ///
    /// <para>⚠️ <b>It weakens nothing.</b> The bytes take the identical path — same PKCS#12 parse, same
    /// private-key requirement, same refusal to start when unusable. What changes is where the bytes come from,
    /// and an environment variable holding a password-protected PKCS#12 is not more exposed than a file the same
    /// process must be able to read. <see cref="CertificatePasswordKey"/> still applies, and still should be set.</para>
    ///
    /// <para>⚠️ <b>Setting both this and <see cref="CertificatePathKey"/> is refused rather than resolved by
    /// precedence.</b> Two certificates named for one role is an operator holding two intentions, and silently
    /// honouring one of them is how a deployment ends up encrypting under a key its operator is not backing up.</para>
    /// </summary>
    public const string CertificateBase64Key = "DataProtection:CertificateBase64";

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
        var activeBase64 = configuration[CertificateBase64Key];
        var hasPath = !string.IsNullOrWhiteSpace(activePath);
        var hasBase64 = !string.IsNullOrWhiteSpace(activeBase64);

        if (hasPath && hasBase64)
        {
            throw new InvalidOperationException(
                $"{CertificatePathKey} et {CertificateBase64Key} sont tous deux renseignés. Un seul certificat "
                + "peut protéger le trousseau : choisir l'un des deux silencieusement reviendrait à chiffrer "
                + "sous une clé dont l'exploitant sauvegarde peut-être l'autre. Supprimez celui qui ne sert pas "
                + "— voir deploy/KEY-CUSTODY.md.");
        }

        if (!hasPath && !hasBase64)
        {
            return new Resolution(null, Array.Empty<X509Certificate2>(), Array.Empty<string>());
        }

        var warnings = new List<string>();
        var password = configuration[CertificatePasswordKey];
        var active = hasPath
            ? Load(activePath!, password, "active", CertificatePathKey)
            : LoadFromBase64(activeBase64!, password, "active", CertificateBase64Key);

        // The active certificate leads the decryptor set — see the ⚠️ on the class.
        var decryptors = new List<X509Certificate2> { active };

        var previous = configuration.GetSection(PreviousCertificatesSection).GetChildren().ToList();
        for (var i = 0; i < previous.Count; i++)
        {
            var path = previous[i]["Path"];
            var base64 = previous[i]["Base64"];
            var role = $"génération retenue n° {i}";

            // A retained generation gets the same two delivery routes as the active certificate, or rotation
            // (FR-3.2) would be impossible on exactly the platforms CertificateBase64Key exists for.
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(base64))
            {
                throw new InvalidOperationException(
                    $"{PreviousCertificatesSection}:{i} indique à la fois « Path » et « Base64 ». Une génération "
                    + "retenue est un seul certificat : n'en gardez qu'un.");
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                decryptors.Add(Load(path, previous[i]["Password"], role,
                    $"{PreviousCertificatesSection}:{i}:Path"));
            }
            else if (!string.IsNullOrWhiteSpace(base64))
            {
                decryptors.Add(LoadFromBase64(base64, previous[i]["Password"], role,
                    $"{PreviousCertificatesSection}:{i}:Base64"));
            }
            else
            {
                throw new InvalidOperationException(
                    $"{PreviousCertificatesSection}:{i} n'indique ni « Path » ni « Base64 ». Une génération "
                    + "retenue sans certificat ne déchiffre rien : indiquez le précédent ou supprimez l'entrée.");
            }
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

        return FromBytes(File.ReadAllBytes(path), password, role, settingKey, $"« {path} »");
    }

    /// <summary>
    /// The <see cref="CertificateBase64Key"/> route. Whitespace is stripped before decoding because an
    /// environment variable carrying ~3 KB of base64 is routinely pasted wrapped, and a dashboard that folds it
    /// would otherwise produce « illisible » for a certificate that is perfectly good.
    /// </summary>
    private static X509Certificate2 LoadFromBase64(string base64, string? password, string role, string settingKey)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(new string(base64.Where(c => !char.IsWhiteSpace(c)).ToArray()));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Le certificat de protection du trousseau ({role}) n'est pas du base64 valide. Il est nommé par "
                + $"{settingKey}. Encodez le fichier PKCS#12 entier : "
                + "`base64 -w0 dataprotection.pfx` — voir deploy/KEY-CUSTODY.md. "
                + $"Détail : {ex.Message}", ex);
        }

        return FromBytes(bytes, password, role, settingKey, "fourni en base64");
    }

    /// <summary>
    /// The single parse, so a certificate delivered as a file and one delivered as base64 meet <b>identical</b>
    /// checks — a second copy of « is this a PKCS#12 with a private key? » is how one route ends up laxer.
    /// </summary>
    private static X509Certificate2 FromBytes(
        byte[] bytes, string? password, string role, string settingKey, string origin)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = new X509Certificate2(bytes, password);
        }
        catch (Exception ex)
        {
            var passwordKey = settingKey.Contains("Base64", StringComparison.Ordinal)
                ? settingKey.Replace("Base64", "Password", StringComparison.Ordinal)
                : settingKey.Replace("Path", "Password", StringComparison.Ordinal);

            throw new InvalidOperationException(
                $"Le certificat de protection du trousseau ({role}) est illisible : {origin}. "
                + $"Vérifiez qu'il s'agit d'un fichier PKCS#12 et que {passwordKey} "
                + $"est correct. Détail : {ex.Message}", ex);
        }

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"Le certificat de protection du trousseau ({role}) {origin} ne contient pas de clé privée. "
                + "La clé privée est ce qui permet de RELIRE le trousseau ; sans elle le déploiement écrirait "
                + "des clés qu'il ne saurait plus déchiffrer au redémarrage suivant.");
        }

        return certificate;
    }

    private static string Describe(X509Certificate2 certificate) =>
        $"« {certificate.Subject} » ({certificate.Thumbprint})";
}
