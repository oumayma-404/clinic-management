using ClinicManagement.Infrastructure.Auth;
using OtpNet;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Auth;

/// <summary>
/// The console's one-time codes (<c>platform-console</c> FR-1, AC-1.2).
///
/// <para><b>What is worth asserting here, and what is not.</b> Re-deriving HMAC-SHA1 to check <c>Otp.NET</c>'s
/// arithmetic would be reimplementing the library and then comparing it with itself. What this codebase can get
/// wrong is the <i>wrapping</i>: the secret's encoding, the drift window's edges, and the two shapes of malformed
/// input that reach an <b>anonymous</b> endpoint — where an exception is a 500 that distinguishes a corrupted
/// account from a wrong password.</para>
/// </summary>
public class TotpServiceTests
{
    private readonly TotpService _service = new();

    [Fact]
    public void A_generated_secret_is_base32_and_round_trips()
    {
        var secret = _service.GenerateSecret();

        // 160 bits, as RFC 4226 § 4 requires and as an authenticator's QR payload expects.
        Assert.Equal(20, Base32Encoding.ToBytes(secret).Length);
    }

    [Fact]
    public void Two_generated_secrets_differ()
    {
        Assert.NotEqual(_service.GenerateSecret(), _service.GenerateSecret());
    }

    [Fact]
    public void A_code_generated_from_the_secret_verifies()
    {
        var secret = _service.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        Assert.True(_service.VerifyCode(secret, code));
    }

    // The drift window's near edge. A clinic PC and a phone disagree by seconds routinely, so a code from the
    // previous step must still verify — with a zero window, a code typed at the end of its period is refused
    // while being entirely correct, which reads as « the console is broken ».
    [Fact]
    public void A_code_from_one_step_earlier_still_verifies()
    {
        var secret = _service.GenerateSecret();
        var previous = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(DateTime.UtcNow.AddSeconds(-30));

        Assert.True(_service.VerifyCode(secret, previous));
    }

    // …and its far edge, which is the half that keeps the window a window. Five minutes out is not clock drift.
    [Fact]
    public void A_code_from_far_outside_the_window_does_not_verify()
    {
        var secret = _service.GenerateSecret();
        var stale = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(DateTime.UtcNow.AddMinutes(-5));

        Assert.False(_service.VerifyCode(secret, stale));
    }

    [Fact]
    public void A_code_from_another_secret_does_not_verify()
    {
        var mine = _service.GenerateSecret();
        var theirs = _service.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(theirs)).ComputeTotp();

        Assert.False(_service.VerifyCode(mine, code));
    }

    // An authenticator app displays « 123 456 », so that is how it gets typed back.
    [Fact]
    public void Spacing_in_the_typed_code_is_tolerated()
    {
        var secret = _service.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        Assert.True(_service.VerifyCode(secret, $"{code[..3]} {code[3..]}"));
    }

    // ⚠️ Both of these reach an ANONYMOUS endpoint. Throwing would be a 500 that tells a caller the difference
    // between a corrupted account and a wrong password — so malformed input is refused, never thrown on.
    [Theory]
    [InlineData("not-base32-!!!", "123456")]
    [InlineData("", "123456")]
    [InlineData(null, "123456")]
    public void A_malformed_secret_is_refused_rather_than_thrown_on(string? secret, string code)
    {
        Assert.False(_service.VerifyCode(secret!, code));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("abcdef")]
    public void A_malformed_code_is_refused_rather_than_thrown_on(string? code)
    {
        Assert.False(_service.VerifyCode(_service.GenerateSecret(), code!));
    }
}
