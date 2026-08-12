using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Answers « which key-ring generation was this ciphertext written under, and is it the current one? »
/// (<c>hosted-security-hardening</c> FR-3.1 / FR-3.9).
///
/// <para>Three callers share it deliberately: the <c>reprotect-secrets</c> verb (which rows still need
/// re-encrypting), <c>verify-schema</c>'s <c>secrets-protected-under-current-ring</c> (the figure that says the
/// verb finished) and <c>KeyRingGenerationMarker</c> (the generation stamped beside each dump). Two answers to
/// « is this row done? » would let the verb report success against a figure computed a different way.</para>
///
/// <para><b>The format.</b> A Data Protection payload is base64url over
/// <c>{ magic header, 4 bytes }{ key id, 16 bytes }{ ciphertext }</c> — the layout the framework documents. The
/// magic header is <i>not</i> hardcoded here: <see cref="Current"/> obtains the whole 20-byte prefix by
/// protecting a probe value, so the comparison is always against what this process actually writes.</para>
///
/// <para>⚠️ <b>The rendered id is the key id's bytes in the payload's own order, and <see cref="IdOf"/> is the
/// only place that knows it.</b> The framework writes the <c>Guid</c>'s native memory layout, which is what
/// <c>Guid.ToByteArray()</c> produces on every platform this product runs on — so a key id taken from
/// <c>IKeyManager</c> renders identically to one lifted out of a payload, and the two can be compared as text.
/// Rendering one of them as a canonical GUID instead would byte-swap the first three fields and make every
/// comparison silently fail, which on the FR-3.9 marker means refusing a restore that was perfectly valid.</para>
/// </summary>
public static class DataProtectionKeyGeneration
{
    private const int MagicHeaderBytes = 4;
    private const int KeyIdBytes = 16;
    private const int PrefixBytes = MagicHeaderBytes + KeyIdBytes;

    /// <summary>The probe protected to learn the active generation. Its value is irrelevant and never stored.</summary>
    private const string Probe = "keyring-generation-probe";

    /// <summary>
    /// The generation a given protector is writing under right now, plus the test for whether a stored
    /// ciphertext is already under it.
    /// </summary>
    public sealed class Generation
    {
        private readonly byte[] _prefix;

        internal Generation(byte[] prefix)
        {
            _prefix = prefix;
        }

        /// <summary>Hex rendering of the key id, for the FR-3.9 dump stamp and for operator messages.</summary>
        public string Id => Convert.ToHexString(_prefix, MagicHeaderBytes, KeyIdBytes).ToLowerInvariant();

        /// <summary>Whether this generation is one of <paramref name="ids"/> — the FR-3.9 restore question.</summary>
        public bool IsAmong(IEnumerable<string> ids) =>
            ids.Any(id => string.Equals(id?.Trim(), Id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether <paramref name="ciphertext"/> was written under this generation. <b>Unparseable ciphertext
        /// answers false</b> — a row this cannot read is a row that still needs work, which is the safe
        /// direction for a figure whose zero authorises deleting the old key files.
        /// </summary>
        public bool Covers(string? ciphertext)
        {
            if (string.IsNullOrEmpty(ciphertext) || !TryDecodeBase64Url(ciphertext, out var bytes))
            {
                return false;
            }

            return bytes.Length >= PrefixBytes && bytes.AsSpan(0, PrefixBytes).SequenceEqual(_prefix);
        }

        /// <summary>Whether a stamp taken earlier names this same generation (FR-3.9's restore check).</summary>
        public bool Matches(string? stamp) =>
            string.Equals(stamp?.Trim(), Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renders a key id the ring reports (<c>IKey.KeyId</c>) into the <b>same</b> text a payload's own id
    /// renders to. See the ⚠️ on the class for why this must not be <c>Guid.ToString("N")</c>.
    /// </summary>
    public static string IdOf(Guid keyId) => Convert.ToHexString(keyId.ToByteArray()).ToLowerInvariant();

    /// <summary>
    /// Reads the active generation off <paramref name="protector"/> by protecting a probe.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The payload is shorter than the documented header, i.e. the format assumption above no longer holds.
    /// </exception>
    public static Generation Current(IDataProtector protector)
    {
        var payload = protector.Protect(Probe);

        if (!TryDecodeBase64Url(payload, out var bytes) || bytes.Length < PrefixBytes)
        {
            throw new InvalidOperationException(
                "Le format des données protégées n'est pas celui attendu : impossible d'y lire l'identifiant de "
                + "génération du trousseau. La vérification « secrets-protected-under-current-ring » et le "
                + "marquage des sauvegardes en dépendent (FR-3.1 / FR-3.9).");
        }

        return new Generation(bytes[..PrefixBytes]);
    }

    /// <summary>
    /// Decodes Data Protection's base64url. Hand-rolled rather than taken from a web package: this assembly is
    /// a class library and the rule is four characters long.
    /// </summary>
    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

        var buffer = new byte[padded.Length / 4 * 3];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}
