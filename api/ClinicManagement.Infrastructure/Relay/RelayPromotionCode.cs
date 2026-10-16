using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Relay;

public enum RelayPromotionVerdict
{
    Valid,
    Malformed,
    BadSignature,
    OtherPc,
    Expired,
}

/// <summary>What a promotion code says: which PC of which cabinet may become a local server, until when.</summary>
public sealed record RelayPromotionClaim(Guid RelayId, Guid ClinicId, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, string Nonce);

/// <summary>
/// The code that turns a PC de secours into a standalone local server once its cloud is lost for good
/// (<c>clinic-pc-copy</c> D11, AC-9.3). Signed <b>offline</b> by the vendor with a private key that never sits on a
/// server — the console lives with the cloud and dies with it — and checked on the PC against
/// <see cref="VendorPublicKey"/>, which is compiled in so that no configuration can make a PC trust another key.
///
/// <para>Format: <c>APEXA-PROMO-1.&lt;payload&gt;.&lt;signature&gt;</c>, both base64url; ECDSA P-256 over SHA-256 of
/// the first two parts as ASCII — the exact bytes travel, so no JSON canonicalisation can make two sides disagree.
/// Bound to one PC (<see cref="RelayPromotionClaim.RelayId"/>) of one cabinet, and short-lived.</para>
///
/// <para>⚠️ <b>Rotating the key is replacing <see cref="VendorPublicKey"/></b> in a build. A PC verifies with the key
/// of the build it runs — the last one it followed before its cloud was lost — so the private key behind every key
/// that ever shipped must be kept.</para>
/// </summary>
public static class RelayPromotionCode
{
    public const string Prefix = "APEXA-PROMO-1";
    public const string Purpose = "relay-promotion";

    public static readonly TimeSpan DefaultValidity = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaxValidity = TimeSpan.FromDays(30);

    /// <summary>A PC whose clock runs a little behind the vendor's must still accept a code issued just now.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The vendor's public key (P-256 SubjectPublicKeyInfo, base64), generated 2026-10-08 with
    /// <c>sign-relay-promotion --new-key</c>; its private half lives on the vendor's own machine, never in this repository.
    /// </summary>
    public const string VendorPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE9HwSikzPNCJmOBGqtiqsaqqp/+XJB/HbQmLvW66Poqcr41qGkMtODa5uQy507N7USGJGwpmTmI3VBKinPeZcJA==";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Payload(string Purpose, Guid RelayId, Guid ClinicId, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, string Nonce);

    /// <summary>A new vendor key: the private half as PKCS#8 PEM (kept offline), the public half to compile in.</summary>
    public static (string PrivateKeyPem, string PublicKey) NewVendorKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    public static string PublicKeyOf(string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static string Sign(string privateKeyPem, Guid relayId, Guid clinicId, DateTime issuedAtUtc, TimeSpan validity)
    {
        if (validity <= TimeSpan.Zero || validity > MaxValidity)
        {
            throw new ArgumentOutOfRangeException(nameof(validity), $"A promotion code is valid between a moment and {MaxValidity.TotalDays} days.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(Purpose, relayId, clinicId, issuedAtUtc,
            issuedAtUtc + validity, Convert.ToHexString(RandomNumberGenerator.GetBytes(16))), Json);
        var body = Prefix + "." + Base64Url(payload);

        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var signature = key.SignData(Encoding.ASCII.GetBytes(body), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return body + "." + Base64Url(signature);
    }

    /// <summary>
    /// The verdict on <paramref name="code"/> for this PC. The signature is checked before a byte of the payload is
    /// read, so nothing an untrusted party wrote is ever parsed.
    /// </summary>
    public static (RelayPromotionVerdict Verdict, RelayPromotionClaim? Claim) Verify(
        string? code, string trustedPublicKey, Guid relayId, Guid clinicId, DateTime nowUtc)
    {
        var parts = (code ?? string.Empty).Trim().Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return (RelayPromotionVerdict.Malformed, null);
        }

        Payload? payload;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(trustedPublicKey), out _);
            if (!key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromBase64Url(parts[2]),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return (RelayPromotionVerdict.BadSignature, null);
            }

            payload = JsonSerializer.Deserialize<Payload>(FromBase64Url(parts[1]), Json);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            return (RelayPromotionVerdict.Malformed, null);
        }

        if (payload is null || !string.Equals(payload.Purpose, Purpose, StringComparison.Ordinal))
        {
            return (RelayPromotionVerdict.Malformed, null);
        }

        var claim = new RelayPromotionClaim(payload.RelayId, payload.ClinicId, payload.IssuedAtUtc, payload.ExpiresAtUtc, payload.Nonce);
        if (payload.RelayId != relayId || payload.ClinicId != clinicId)
        {
            return (RelayPromotionVerdict.OtherPc, claim);
        }

        if (nowUtc > payload.ExpiresAtUtc || payload.IssuedAtUtc > nowUtc + ClockSkew
            || payload.ExpiresAtUtc - payload.IssuedAtUtc > MaxValidity)
        {
            return (RelayPromotionVerdict.Expired, claim);
        }

        return (RelayPromotionVerdict.Valid, claim);
    }

    public static string Sentence(RelayPromotionVerdict verdict) => verdict switch
    {
        RelayPromotionVerdict.Valid => "Le code de promotion est valable pour ce PC.",
        RelayPromotionVerdict.BadSignature => "Ce code de promotion n'a pas été émis par l'éditeur : il est refusé.",
        RelayPromotionVerdict.OtherPc => "Ce code de promotion a été émis pour un autre PC de secours : il est refusé.",
        RelayPromotionVerdict.Expired => "Ce code de promotion a expiré : demandez-en un nouveau à l'éditeur.",
        _ => "Ce code de promotion est illisible : vérifiez qu'il a été copié en entier.",
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
