using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The two cryptographic halves of a Web Push (staff-chat Part 8), on the framework's own primitives — no library.
///
/// <para><b>The payload is encrypted to the browser</b> (RFC 8291, <c>aes128gcm</c>): the push service relays bytes it
/// cannot read. <b>The request is signed by the server</b> (RFC 8292, VAPID): the push service accepts it only from the
/// key the subscription was created with.</para>
/// </summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    /// <summary>
    /// The request body: <c>salt(16) · rs(4) · idlen(1) · keyid(65)</c> then the one encrypted record. The salt and the
    /// ephemeral key are parameters so a test can reproduce a known answer; production passes neither.
    /// </summary>
    public static byte[] Encrypt(
        byte[] plaintext, byte[] browserPublicKey, byte[] authSecret, byte[]? salt = null, ECDiffieHellman? ephemeral = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(16);
        using var serverKey = ephemeral ?? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var serverPublicKey = UncompressedPoint(serverKey.ExportParameters(false));

        using var browserKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = browserPublicKey[1..33], Y = browserPublicKey[33..65] }
        });
        var sharedSecret = serverKey.DeriveRawSecretAgreement(browserKey.PublicKey);

        // RFC 8291 § 3.4: IKM = HKDF(auth_secret, ecdh_secret, "WebPush: info" ‖ 0x00 ‖ ua_public ‖ as_public, 32).
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), browserPublicKey, serverPublicKey);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);

        // RFC 8188 § 2.2: the content key and the nonce, from the record's salt.
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // One record, so it is the last: the delimiter is 0x02 and there is no padding.
        var padded = Concat(plaintext, new byte[] { 0x02 });
        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Encrypt(nonce, padded, cipher, tag);
        }

        var header = new byte[16 + 4 + 1 + serverPublicKey.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), RecordSize);
        header[20] = (byte)serverPublicKey.Length;
        serverPublicKey.CopyTo(header, 21);
        return Concat(header, cipher, tag);
    }

    /// <summary>
    /// <c>Authorization: vapid t=…, k=…</c> for one push service (RFC 8292). <paramref name="audience"/> is the
    /// endpoint's origin; the token lives 12 hours, the maximum Apple accepts being 24.
    /// </summary>
    public static string VapidAuthorization(string audience, string subject, string publicKey, string privateKey, DateTimeOffset now)
    {
        var publicBytes = WebPushSubscription.FromBase64Url(publicKey)
            ?? throw new CryptographicException("Clé publique VAPID illisible.");
        var privateBytes = WebPushSubscription.FromBase64Url(privateKey)
            ?? throw new CryptographicException("Clé privée VAPID illisible.");
        if (publicBytes.Length != 65 || publicBytes[0] != 0x04 || privateBytes.Length != 32)
        {
            throw new CryptographicException("Clés VAPID invalides : une clé publique P-256 non compressée et une clé privée de 32 octets sont attendues.");
        }

        using var signer = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateBytes,
            Q = new ECPoint { X = publicBytes[1..33], Y = publicBytes[33..65] }
        });

        var header = WebPushSubscription.ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
        var claims = WebPushSubscription.ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = audience,
            exp = now.AddHours(12).ToUnixTimeSeconds(),
            sub = subject
        }));
        var signingInput = $"{header}.{claims}";
        // ES256 is the raw r ‖ s pair, which is .NET's default signature format for ECDsa.
        var signature = signer.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"vapid t={signingInput}.{WebPushSubscription.ToBase64Url(signature)}, k={publicKey.Trim()}";
    }

    /// <summary>A fresh VAPID key pair, base64url (public uncompressed, private raw) — what an operator configures once.</summary>
    public static (string PublicKey, string PrivateKey) NewVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return (WebPushSubscription.ToBase64Url(UncompressedPoint(parameters)), WebPushSubscription.ToBase64Url(parameters.D!));
    }

    private static byte[] UncompressedPoint(ECParameters parameters) =>
        Concat(new byte[] { 0x04 }, parameters.Q.X!, parameters.Q.Y!);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
