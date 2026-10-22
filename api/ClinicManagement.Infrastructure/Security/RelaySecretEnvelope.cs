using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>Seals a secret for one PC de secours's RSA key, so it never crosses the wire under the cloud's key ring (D8).</summary>
public static class RelaySecretEnvelope
{
    public const string Prefix = "relay1:";

    /// <summary>A new RSA-2048 key pair: the public half (SPKI, base64) for the cloud, the private half (PKCS#8) for the PC.</summary>
    public static (string PublicKey, byte[] PrivateKey) NewKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return (Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), rsa.ExportPkcs8PrivateKey());
    }

    public static string Seal(string plaintext, string publicKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        return Prefix + Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(plaintext), RSAEncryptionPadding.OaepSHA256));
    }

    /// <summary>The plaintext, or null for anything this key did not seal.</summary>
    public static string? Open(string? sealedValue, byte[] privateKey)
    {
        if (string.IsNullOrEmpty(sealedValue) || !sealedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateKey, out _);
            var bytes = rsa.Decrypt(Convert.FromBase64String(sealedValue[Prefix.Length..]), RSAEncryptionPadding.OaepSHA256);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="publicKey"/> is an RSA SPKI key this envelope can seal for.</summary>
    public static bool IsValidPublicKey(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return rsa.KeySize >= 2048;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
