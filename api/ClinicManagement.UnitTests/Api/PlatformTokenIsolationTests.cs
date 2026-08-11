using System.Text;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [AC-1.4] A console session and a clinic session are not interchangeable <b>in either direction</b>, and each is
/// refused as <b>unauthenticated</b> rather than merely unauthorised.
///
/// <para><b>Why the distinction is worth a test class.</b> « Refused » could be delivered by an authorization
/// policy — and that version is one forgotten attribute away from working, and produces a 403 that tells the
/// caller its credential was accepted. Here it is delivered by <i>token validation</i>: each scheme validates a
/// different issuer, audience and signing key, so the other's token fails before any policy runs. These tests
/// assert that property directly, by validating a real issued token against the other side's real parameters —
/// the same <see cref="JsonWebTokenHandler"/> the runtime's JwtBearer uses.</para>
///
/// <para>⚠️ It is asserted against <see cref="PlatformAuthConfig"/> and <see cref="LocalAuthConfig"/> themselves
/// rather than against retyped issuer strings: a test carrying its own copy of the two names would keep passing
/// on the day somebody made them equal.</para>
/// </summary>
public class PlatformTokenIsolationTests
{
    private const string ConsoleKey = "a-console-signing-key-that-is-long-enough-for-hmac-sha256";
    private const string ClinicKey = "a-DIFFERENT-clinic-signing-key-also-long-enough-for-hs256";

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Console:SigningKey"] = ConsoleKey,
                ["Auth:Local:SigningKey"] = ClinicKey,
                ["Auth:Mode"] = "Local"
            })
            .Build();

    private static PlatformAuthService ConsoleService(IConfiguration configuration)
    {
        // The password hashing half is delegated to the clinic side; token issuance is not, which is the whole
        // design. Only issuance is under test here, so the delegate is a mock.
        var hashing = new Mock<ILocalAuthService>();
        return new PlatformAuthService(hashing.Object, configuration);
    }

    private static PlatformAccount Account()
    {
        var account = PlatformAccount.Create("ops@editeur.tn", "Ops", "hash");
        return account;
    }

    private static TokenValidationParameters ClinicParameters(IConfiguration configuration) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = LocalAuthConfig.Issuer(configuration),
        ValidateAudience = true,
        ValidAudience = LocalAuthConfig.Audience(configuration),
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = LocalAuthConfig.SecurityKey(configuration),
        ClockSkew = TimeSpan.Zero
    };

    private static TokenValidationParameters ConsoleParameters(IConfiguration configuration) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = PlatformAuthConfig.Issuer(configuration),
        ValidateAudience = true,
        ValidAudience = PlatformAuthConfig.Audience(configuration),
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = PlatformAuthConfig.SecurityKey(configuration),
        ClockSkew = TimeSpan.Zero
    };

    private static bool Validates(string token, TokenValidationParameters parameters) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, parameters).GetAwaiter().GetResult().IsValid;

    // [AC-1.4] Direction one: the console's token is not a clinic credential.
    [Fact]
    public void A_console_token_does_not_validate_against_the_clinic_scheme()
    {
        var configuration = Configuration();
        var token = ConsoleService(configuration).GenerateToken(Account()).AccessToken;

        Assert.True(Validates(token, ConsoleParameters(configuration)));
        Assert.False(Validates(token, ClinicParameters(configuration)));
    }

    // [AC-1.4] Direction two, which the console policy's pinned scheme is what delivers: a clinic token presented
    // to a console route authenticates against the console scheme and fails it.
    [Fact]
    public void A_clinic_token_does_not_validate_against_the_console_scheme()
    {
        var configuration = Configuration();
        var user = User.CreateLocalUser(Guid.NewGuid(), User.RoleAdmin, "a@b.tn", "hash", "A B");
        var token = new LocalAuthService(configuration).GenerateToken(user).AccessToken;

        Assert.True(Validates(token, ClinicParameters(configuration)));
        Assert.False(Validates(token, ConsoleParameters(configuration)));
    }

    // The three values that make the two above true. Read off the config readers, never retyped — a test with its
    // own copy of the names would keep passing on the day the two were made equal.
    [Fact]
    public void The_two_schemes_share_no_issuer_no_audience_and_no_key()
    {
        var configuration = Configuration();

        Assert.NotEqual(LocalAuthConfig.Issuer(configuration), PlatformAuthConfig.Issuer(configuration));
        Assert.NotEqual(LocalAuthConfig.Audience(configuration), PlatformAuthConfig.Audience(configuration));
        Assert.NotEqual(
            Convert.ToBase64String(LocalAuthConfig.ResolveSigningKey(configuration)),
            Convert.ToBase64String(PlatformAuthConfig.ResolveSigningKey(configuration)));
    }

    // ⚠️ There is no fallback to the clinic key. An absent Console:SigningKey THROWS where the console is bound,
    // because a shared key would make the two token kinds differ only by their claims — and AC-1.4 would become
    // an authorization decision instead of a validation one.
    [Fact]
    public void An_absent_console_signing_key_fails_loud_rather_than_borrowing_the_clinic_one()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Local:SigningKey"] = ClinicKey })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformAuthConfig.ResolveSigningKey(configuration));

        Assert.Contains(PlatformAuthConfig.SigningKeyKey, exception.Message);
    }

    [Fact]
    public void A_console_signing_key_below_256_bits_is_refused()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Console:SigningKey"] = "too-short" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => PlatformAuthConfig.ResolveSigningKey(configuration));
    }

    // The claim IPlatformSessionContext keys on — and the two the console token must NOT carry. A clinic_id or a
    // role on it would give RoleAuthorizationHandler and TenantScopeMiddleware something to resolve against, i.e.
    // a clinic-side gate evaluating a principal it was never meant to see.
    //
    // ⚠️ Asserted against the RAW payload rather than JwtSecurityTokenHandler.ReadJwtToken. That handler's claim
    // view is filtered by process-wide statics (DefaultInboundClaimTypeMap / DefaultInboundClaimFilter) which any
    // other test in this assembly can mutate — it was observed dropping `email`, `jti` and `token_kind` from a
    // token that demonstrably carried all three. The bytes on the wire are also the honest subject here: what
    // another process reads is the payload, not this handler's projection of it.
    [Fact]
    public void The_console_token_carries_its_kind_and_neither_a_clinic_nor_a_role()
    {
        var payload = PayloadOf(ConsoleService(Configuration()).GenerateToken(Account()).AccessToken);

        Assert.Equal(
            IPlatformSessionContext.PlatformTokenKind,
            payload.GetProperty(IPlatformSessionContext.TokenKindClaim).GetString());
        Assert.False(payload.TryGetProperty("clinic_id", out _));
        Assert.False(payload.TryGetProperty("role", out _));
    }

    /// <summary>The JWT's decoded payload object — base64url, so the padding and the two substituted characters
    /// have to be restored before <see cref="Convert.FromBase64String"/> will read it.</summary>
    private static JsonElement PayloadOf(string jwt)
    {
        var segment = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');

        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment))).RootElement;
    }
}
