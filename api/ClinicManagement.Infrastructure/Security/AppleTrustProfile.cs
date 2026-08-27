using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Builds the iOS/iPadOS <c>.mobileconfig</c> configuration profile that installs this install's CA
/// (P8, AC-44). Pure: DER bytes in, plist bytes out — no I/O, no clock, no randomness, so it is unit-testable
/// and two downloads of the same CA are byte-identical.
///
/// <para><b>Why a generated profile and not the bare <c>ca.crt</c>.</b> Safari will download a <c>.crt</c>, but
/// on iOS a raw certificate file lands in a state the user has to go find; a
/// <c>com.apple.security.root</c> profile is the flow Apple actually supports — tap, « Installer », done.
/// The <c>.crt</c> is still served alongside it because Android wants exactly that instead.</para>
///
/// <para>⚠️ <b>Installing the profile is only half of trusting the CA on iOS 10.3+.</b> A profile-installed
/// root is inert until the user *also* enables it under
/// « Réglages → Général → Informations → Certificats de confiance ». That second switch is a deliberate Apple
/// decision and nothing served here can flip it — which is why it is one of the documented failure states
/// (a device that has "installed the certificate" and still sees a warning has stopped at exactly this point).
/// The profile's description says so in French, because the person holding the phone is the only one who can
/// finish the job.</para>
///
/// <para>⚠️ <b>The profile is generated at runtime, never staged at build time.</b> On a reinstall the CA is
/// reused and on a fresh install it is newly minted, so a profile baked into an installer payload is stale by
/// construction — it would install a root that signs nothing.</para>
/// </summary>
public static class AppleTrustProfile
{
    /// <summary>The MIME type iOS uses to recognise a configuration profile and offer to install it.</summary>
    public const string ContentType = "application/x-apple-aspen-config";

    /// <summary>Reverse-DNS identifier of the profile as a whole. Stable, so a re-install replaces.</summary>
    private const string ProfileIdentifier = "tn.clinicmanagement.trust";

    /// <summary>Reverse-DNS identifier of the single root-certificate payload inside it.</summary>
    private const string PayloadIdentifier = "tn.clinicmanagement.trust.ca";

    /// <summary>
    /// Wrap a DER-encoded CA certificate in a configuration profile.
    /// </summary>
    /// <param name="caDer">
    /// The CA's DER bytes — exactly what <c>.local/ca.crt</c> already holds, so no re-encoding step can
    /// corrupt them.
    /// </param>
    /// <param name="clinicLabel">
    /// What to call the clinic in the two names iOS shows the user. Blank falls back to a generic label rather
    /// than rendering an empty title, which reads as a broken profile.
    /// </param>
    public static byte[] Build(byte[] caDer, string? clinicLabel = null)
    {
        if (caDer is null || caDer.Length == 0)
        {
            throw new ArgumentException("Le certificat de l'autorité est requis.", nameof(caDer));
        }

        var label = string.IsNullOrWhiteSpace(clinicLabel) ? "Clinique" : clinicLabel.Trim();

        // Both UUIDs are DERIVED FROM THE CA, not random. iOS keys a profile on its UUID: a random one would
        // make every download a different profile, so a staff member who taps « Installer » twice ends up with
        // two roots and no way to tell which is live. Deriving it means re-downloading the SAME CA re-installs
        // in place, while a REGENERATED CA is correctly seen as a new profile — which is the case the operator
        // needs to be able to distinguish (a stale CA is a documented failure state).
        var profileUuid = DeterministicUuid(caDer, "profile");
        var payloadUuid = DeterministicUuid(caDer, "payload");

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteDocType(
                "plist",
                "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd",
                null);

            writer.WriteStartElement("plist");
            writer.WriteAttributeString("version", "1.0");
            writer.WriteStartElement("dict");

            WriteKey(writer, "PayloadContent");
            writer.WriteStartElement("array");
            writer.WriteStartElement("dict");

            WriteString(writer, "PayloadCertificateFileName", "ca.crt");
            WriteKey(writer, "PayloadContent");
            writer.WriteElementString("data", Convert.ToBase64String(caDer));
            WriteString(
                writer,
                "PayloadDescription",
                "Autorité de certification du serveur du cabinet. "
                + "Après l'installation, activez-la dans Réglages → Général → Informations → "
                + "Certificats de confiance.");
            WriteString(writer, "PayloadDisplayName", $"Autorité de certification — {label}");
            WriteString(writer, "PayloadIdentifier", PayloadIdentifier);
            WriteString(writer, "PayloadType", "com.apple.security.root");
            WriteString(writer, "PayloadUUID", payloadUuid);
            WriteInteger(writer, "PayloadVersion", 1);

            writer.WriteEndElement(); // payload dict
            writer.WriteEndElement(); // array

            WriteString(writer, "PayloadDisplayName", $"{label} — accès sécurisé");
            WriteString(
                writer,
                "PayloadDescription",
                "Permet à cet appareil de se connecter au serveur du cabinet sans avertissement de sécurité.");
            WriteString(writer, "PayloadIdentifier", ProfileIdentifier);
            WriteString(writer, "PayloadOrganization", label);
            WriteString(writer, "PayloadType", "Configuration");
            WriteString(writer, "PayloadUUID", profileUuid);
            WriteInteger(writer, "PayloadVersion", 1);

            // The clinic must be able to undo this on a staff member's personal phone without a factory reset.
            WriteKey(writer, "PayloadRemovalDisallowed");
            writer.WriteElementString("false", string.Empty);

            writer.WriteEndElement(); // root dict
            writer.WriteEndElement(); // plist
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A UUID derived from the CA's bytes and a role label — same CA, same UUID, on every machine and every
    /// request. Formatted as a v4-shaped UUID because that is what iOS parses; the version nibbles carry no
    /// meaning here beyond satisfying the format.
    /// </summary>
    private static string DeterministicUuid(byte[] caDer, string role)
    {
        var seed = new byte[caDer.Length + role.Length];
        caDer.CopyTo(seed, 0);
        Encoding.ASCII.GetBytes(role).CopyTo(seed, caDer.Length);

        var hash = SHA256.HashData(seed);
        var uuidBytes = hash.AsSpan(0, 16).ToArray();
        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x40); // version 4 shape
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(uuidBytes).ToString("D").ToUpperInvariant();
    }

    private static void WriteKey(XmlWriter writer, string key) => writer.WriteElementString("key", key);

    private static void WriteString(XmlWriter writer, string key, string value)
    {
        WriteKey(writer, key);
        writer.WriteElementString("string", value);
    }

    private static void WriteInteger(XmlWriter writer, string key, int value)
    {
        WriteKey(writer, key);
        writer.WriteElementString("integer", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
