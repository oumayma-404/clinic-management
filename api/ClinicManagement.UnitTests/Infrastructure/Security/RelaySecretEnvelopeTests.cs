using ClinicManagement.Infrastructure.Security;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The envelope a TOTP secret travels in to one PC de secours (<c>clinic-pc-copy</c> D8): sealed for that PC's key,
/// opened by that PC alone, and never mistaken for something it did not seal.
/// </summary>
public class RelaySecretEnvelopeTests
{
    private const string Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public void A_Sealed_Secret_Opens_With_The_Matching_Private_Key()
    {
        var (publicKey, privateKey) = RelaySecretEnvelope.NewKeyPair();

        var sealedValue = RelaySecretEnvelope.Seal(Secret, publicKey);

        Assert.StartsWith(RelaySecretEnvelope.Prefix, sealedValue);
        Assert.DoesNotContain(Secret, sealedValue);
        Assert.Equal(Secret, RelaySecretEnvelope.Open(sealedValue, privateKey));
    }

    // Another PC's key opens nothing: a copy carried to the wrong machine yields no second factor.
    [Fact]
    public void Another_Pcs_Key_Opens_Nothing()
    {
        var (publicKey, _) = RelaySecretEnvelope.NewKeyPair();
        var (_, otherPrivateKey) = RelaySecretEnvelope.NewKeyPair();

        Assert.Null(RelaySecretEnvelope.Open(RelaySecretEnvelope.Seal(Secret, publicKey), otherPrivateKey));
    }

    // Two seals of one secret differ (OAEP is randomised), so the wire never shows that two accounts share a secret.
    [Fact]
    public void Sealing_Is_Randomised()
    {
        var (publicKey, _) = RelaySecretEnvelope.NewKeyPair();

        Assert.NotEqual(RelaySecretEnvelope.Seal(Secret, publicKey), RelaySecretEnvelope.Seal(Secret, publicKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CfDJ8-a-key-ring-ciphertext")]
    [InlineData("relay1:not-base64!!")]
    public void Anything_It_Did_Not_Seal_Opens_To_Null(string? value)
    {
        var (_, privateKey) = RelaySecretEnvelope.NewKeyPair();

        Assert.Null(RelaySecretEnvelope.Open(value, privateKey));
    }

    [Fact]
    public void Only_An_Rsa_2048_Public_Key_Is_Valid()
    {
        var (publicKey, _) = RelaySecretEnvelope.NewKeyPair();

        Assert.True(RelaySecretEnvelope.IsValidPublicKey(publicKey));
        Assert.False(RelaySecretEnvelope.IsValidPublicKey(null));
        Assert.False(RelaySecretEnvelope.IsValidPublicKey("not a key"));
        Assert.False(RelaySecretEnvelope.IsValidPublicKey(Convert.ToBase64String(new byte[64])));
    }
}
