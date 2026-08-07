using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// The refresh exchange (mobile-native-shells Part 2, AC-35…AC-39). It now mints a <b>fresh durable
/// credential</b> alongside the access token, which is what makes the session slide — so the load-bearing half
/// of this class is that the guards it slides past are all still refusing (AC-36).
///
/// <para>⚠️ <c>Deactivate()</c> and <c>SetPassword()</c> both bump <c>TokenVersion</c>, so every arrangement
/// here mutates the account <b>before</b> reading the version it presents. Reading it first would make the
/// deactivation test pass on the version check instead of the <c>IsActive</c> one it exists for.</para>
/// </summary>
public class RefreshTokenCommandHandlerTests
{
    private const string Credential = "cookie-credential";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime AccessExpiry = new(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime RefreshExpiry = new(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILocalAuthService> _auth = new();

    private RefreshTokenCommandHandler Handler() => new(_users.Object, _auth.Object);

    private static RefreshTokenCommand Command() => new() { RefreshToken = Credential };

    private static User LocalUser(bool mustChangePassword = false) =>
        User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "STORED-HASH", "Dr House", mustChangePassword);

    /// <summary>
    /// Arranges the exchange: the presented credential asserts <paramref name="presentedVersion"/> (the
    /// account's current one unless a test is deliberately presenting a stale one) and both tokens are issuable.
    /// </summary>
    private void Arrange(User user, int? presentedVersion = null)
    {
        _auth.Setup(a => a.ValidateRefreshToken(Credential))
            .Returns(new RefreshTokenPrincipal(user.Id, presentedVersion ?? user.TokenVersion));
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.GenerateToken(user)).Returns(new LocalAuthToken("access-jwt", AccessExpiry));
        _auth.Setup(a => a.GenerateRefreshToken(user)).Returns(new LocalAuthToken("refresh-jwt-2", RefreshExpiry));
    }

    // [AC-35] The exchange returns a NEW durable credential and its own later expiry. Without it the cookie kept
    // the token minted at login, so the session died 12 h after sign-in whatever the user was doing.
    [Fact]
    public async Task Handle_Should_Issue_A_Fresh_Refresh_Credential_And_Expiry()
    {
        var user = LocalUser();
        Arrange(user);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-jwt", result.Value!.AccessToken);
        Assert.Equal("refresh-jwt-2", result.Value.RefreshToken);
        Assert.Equal(RefreshExpiry, result.Value.RefreshExpiresAt);
        Assert.NotEqual(Credential, result.Value.RefreshToken);
        // The cookie's lifetime is keyed off the durable expiry, so equal expiries would collapse a 12 h
        // session to the access token's 30 minutes.
        Assert.True(result.Value.RefreshExpiresAt > result.Value.ExpiresAt);
        _auth.Verify(a => a.GenerateRefreshToken(user), Times.Once);
    }

    // [AC-36] A token version bumped since the cookie was issued (password change, admin reset, role change,
    // deactivation) still refuses — it is the only revocation a stateless credential has.
    [Fact]
    public async Task Handle_Should_Refuse_A_Superseded_Token_Version()
    {
        var user = LocalUser();
        var versionInTheCookie = user.TokenVersion;
        user.SetPassword("NEW-HASH");
        Arrange(user, presentedVersion: versionInTheCookie);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasIssued();
    }

    // [AC-36] A deactivated account cannot renew even when its credential's version matches — the arrangement
    // presents the post-deactivation version precisely so this fails on IsActive and not on the check above.
    [Fact]
    public async Task Handle_Should_Refuse_A_Deactivated_Account()
    {
        var user = LocalUser();
        user.Deactivate();
        Arrange(user);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasIssued();
    }

    // [AC-36] A forced password change is SURFACED, not refused: the change-password screen needs a working
    // access token to submit, and the API's enforcement middleware already restricts it to that one endpoint.
    [Fact]
    public async Task Handle_Should_Surface_A_Pending_Forced_Password_Change()
    {
        var user = LocalUser(mustChangePassword: true);
        Arrange(user);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.MustChangePassword);
        Assert.Equal("refresh-jwt-2", result.Value.RefreshToken);
    }

    // [AC-36] A Cloud account has no local password, so it can never be renewed through this path.
    [Fact]
    public async Task Handle_Should_Refuse_A_NonLocal_Account()
    {
        var cloudUser = new User("auth0|123", ClinicId, "doctor", "doc@clinic.com", "Dr House");
        Arrange(cloudUser);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasIssued();
    }

    /// <summary>
    /// [AC-36] Every refusal reads the same. An expired credential, a forged one, an unknown account, a revoked
    /// session and a deactivated one must be indistinguishable, or this anonymous endpoint becomes an oracle for
    /// account state.
    /// </summary>
    [Fact]
    public async Task Every_Refusal_Reports_The_Same_Message()
    {
        var forged = await Refusal(principal: null, account: null);
        var unknown = await Refusal(new RefreshTokenPrincipal("local|ghost", 0), account: null);

        var revoked = LocalUser();
        var versionInTheCookie = revoked.TokenVersion;
        revoked.SetPassword("NEW-HASH");
        var revokedError = await Refusal(new RefreshTokenPrincipal(revoked.Id, versionInTheCookie), revoked);

        var inactive = LocalUser();
        inactive.Deactivate();
        var inactiveError = await Refusal(new RefreshTokenPrincipal(inactive.Id, inactive.TokenVersion), inactive);

        Assert.False(string.IsNullOrWhiteSpace(forged));
        Assert.Equal(forged, unknown);
        Assert.Equal(forged, revokedError);
        Assert.Equal(forged, inactiveError);
    }

    /// <summary>
    /// [AC-39] Sliding expiry, <b>not</b> revoking rotation: a superseded credential keeps working until its own
    /// expiry, because it is a stateless JWT and nothing stores it.
    ///
    /// <para>Asserted as a property the design claims, not merely left untested. Two tabs — or one retried
    /// request — exchange the same cookie value moments apart, and refusing the second would log the user out
    /// mid-action. A test asserting the opposite would pin a property this design does not have.</para>
    /// </summary>
    [Fact]
    public async Task An_Unexpired_Superseded_Credential_Is_Still_Accepted()
    {
        var user = LocalUser();
        Arrange(user);
        _auth.SetupSequence(a => a.GenerateRefreshToken(user))
            .Returns(new LocalAuthToken("refresh-jwt-2", RefreshExpiry))
            .Returns(new LocalAuthToken("refresh-jwt-3", RefreshExpiry));

        var first = await Handler().Handle(Command(), CancellationToken.None);
        var second = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        // Each exchange mints its own credential and neither invalidates the one presented.
        Assert.Equal("refresh-jwt-2", first.Value!.RefreshToken);
        Assert.Equal("refresh-jwt-3", second.Value!.RefreshToken);
    }

    /// <summary>
    /// Runs one refusal on its own mocks and returns the message. A <c>null</c> <paramref name="principal"/> is a
    /// credential that does not validate at all; a <c>null</c> <paramref name="account"/> is one whose subject
    /// resolves to no user.
    /// </summary>
    private static async Task<string?> Refusal(RefreshTokenPrincipal? principal, User? account)
    {
        var auth = new Mock<ILocalAuthService>();
        var users = new Mock<IUserRepository>();
        auth.Setup(a => a.ValidateRefreshToken(Credential)).Returns(principal);
        users.Setup(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await new RefreshTokenCommandHandler(users.Object, auth.Object)
            .Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        return result.Error;
    }

    private void AssertNothingWasIssued()
    {
        _auth.Verify(a => a.GenerateToken(It.IsAny<User>()), Times.Never);
        _auth.Verify(a => a.GenerateRefreshToken(It.IsAny<User>()), Times.Never);
    }
}
