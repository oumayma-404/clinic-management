using ClinicManagement.Application.Common.Interfaces;
using OtpNet;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// RFC 6238 one-time codes over <c>Otp.NET</c> — the console's second factor (FR-1, AC-1.2).
///
/// <para>Defaults throughout: SHA-1, 6 digits, a 30-second step. Not laziness — those <i>are</i> the algorithm
/// every authenticator app implements, and a deployment that varied them would produce a secret the operator's
/// phone reads as permanently wrong, with no error anywhere saying so.</para>
///
/// <para>⚠️ <b>The verification window is one step either side, and it is deliberate.</b> A clinic PC and a phone
/// disagree by seconds routinely, so with a zero window a code typed at the end of its period is refused while
/// being entirely correct — which reads as « the console is broken » and is the fastest route to somebody asking
/// for the factor to be turned off. The cost is that a code stays valid for at most 90 seconds, which is the
/// standard trade every TOTP implementation makes.</para>
/// </summary>
public class TotpService : ITotpService
{
    /// <summary>160 bits, as RFC 4226 § 4 requires and as every authenticator's QR payload expects.</summary>
    private const int SecretBytes = 20;

    private static readonly VerificationWindow Window = new(previous: 1, future: 1);

    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(SecretBytes));

    public bool VerifyCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Base32Encoding.ToBytes(base32Secret.Trim());
        }
        catch (ArgumentException)
        {
            // A secret that is not base32 cannot have produced any code. Refuse rather than throw: this runs on
            // an anonymous endpoint, and a 500 there would distinguish a corrupted account from a wrong password.
            return false;
        }

        // Spaces are how an authenticator app displays a code and therefore how it gets typed back.
        return new Totp(key).VerifyTotp(code.Replace(" ", string.Empty).Trim(), out _, Window);
    }
}
