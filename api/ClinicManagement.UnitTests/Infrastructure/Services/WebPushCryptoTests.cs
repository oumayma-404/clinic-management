using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Domain.Common;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Web Push's two cryptographic halves (staff-chat Part 8). The known-answer case is RFC 8291's own Appendix A: if it
/// passes, a real browser decrypts what this server sends; a round trip alone would only prove we agree with ourselves.
/// </summary>
public class WebPushCryptoTests
{
    // RFC 8291, Appendix A.
    private const string Plaintext = "When I grow up, I want to be a watermelon";
    private const string UaPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string UaPrivate = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    private const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string AsPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string AsPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string ExpectedBody =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    private static byte[] B64(string value) => WebPushSubscription.FromBase64Url(value)!;

    private static ECDiffieHellman Key(string publicKey, string privateKey)
    {
        var point = B64(publicKey);
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = B64(privateKey),
            Q = new ECPoint { X = point[1..33], Y = point[33..65] }
        });
    }

    [Fact]
    public void The_Payload_Encrypts_To_The_Rfc_8291_Example()
    {
        var body = WebPushCrypto.Encrypt(
            Encoding.UTF8.GetBytes(Plaintext), B64(UaPublic), B64(AuthSecret), B64(Salt), Key(AsPublic, AsPrivate));

        Assert.Equal(ExpectedBody, WebPushSubscription.ToBase64Url(body));
    }

    // The browser's side of RFC 8291: what it does with what we send.
    [Fact]
    public void A_Browser_Decrypts_What_The_Server_Sends()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var browserPublic = Point(browser.ExportParameters(false));
        var auth = RandomNumberGenerator.GetBytes(16);
        var message = Encoding.UTF8.GetBytes("""{"title":"Nouveau message"}""");

        var body = WebPushCrypto.Encrypt(message, browserPublic, auth);

        Assert.Equal(message, Decrypt(body, browser, browserPublic, auth));
        Assert.Equal(4096u, BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4)));
        Assert.Equal(65, body[20]);
    }

    [Fact]
    public void Two_Sends_Of_One_Message_Never_Share_A_Key_Or_A_Salt()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var browserPublic = Point(browser.ExportParameters(false));
        var auth = RandomNumberGenerator.GetBytes(16);
        var message = Encoding.UTF8.GetBytes("même texte");

        var first = WebPushCrypto.Encrypt(message, browserPublic, auth);
        var second = WebPushCrypto.Encrypt(message, browserPublic, auth);

        Assert.NotEqual(first[..16], second[..16]);
        Assert.NotEqual(first[21..86], second[21..86]);
    }

    // VAPID (RFC 8292): the push service checks the signature against the key the subscription was made with.
    [Fact]
    public void The_Vapid_Header_Is_A_Token_Signed_By_The_Configured_Key()
    {
        var (publicKey, privateKey) = WebPushCrypto.NewVapidKeys();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        var header = WebPushCrypto.VapidAuthorization("https://web.push.apple.com", "mailto:contact@apexa.tn", publicKey, privateKey, now);

        Assert.StartsWith("vapid t=", header);
        Assert.EndsWith($", k={publicKey}", header);
        var jwt = header["vapid t=".Length..header.IndexOf(',')];
        var parts = jwt.Split('.');
        var claims = JsonDocument.Parse(B64(parts[1])).RootElement;
        Assert.Equal("https://web.push.apple.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:contact@apexa.tn", claims.GetProperty("sub").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());

        var point = B64(publicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..65] }
        });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), B64(parts[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Malformed_Vapid_Keys_Are_Refused_Rather_Than_Signed_With()
    {
        Assert.Throws<CryptographicException>(() =>
            WebPushCrypto.VapidAuthorization("https://fcm.googleapis.com", "mailto:x@y.z", "not-a-key", "also-not", DateTimeOffset.UtcNow));
    }

    private static byte[] Point(ECParameters parameters) =>
        new byte[] { 0x04 }.Concat(parameters.Q.X!).Concat(parameters.Q.Y!).ToArray();

    private static byte[] Decrypt(byte[] body, ECDiffieHellman browser, byte[] browserPublic, byte[] auth)
    {
        var salt = body[..16];
        var idLength = body[20];
        var serverPublic = body[21..(21 + idLength)];
        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] }
        });
        var shared = browser.DeriveRawSecretAgreement(server.PublicKey);
        var info = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(browserPublic).Concat(serverPublic).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, info);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var record = body[(21 + idLength)..];
        var cipher = record[..^16];
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Decrypt(nonce, cipher, record[^16..], plain);
        }

        Assert.Equal(0x02, plain[^1]);
        return plain[..^1];
    }
}
