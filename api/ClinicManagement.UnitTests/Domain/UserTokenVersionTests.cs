using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Token revocation via <see cref="User.TokenVersion"/> (security-hardening US-5 / AC-5.1, AC-5.2, AC-5.11).
///
/// The stateless JWT means server-side state changes had no effect until expiry: a <b>voluntary</b> password
/// change left every existing token valid for its full remaining lifetime, so the natural reaction to a
/// suspected theft — change my password — did nothing. Bumping this stamp invalidates them immediately.
///
/// The paired trap is <see cref="User.UpgradePasswordHash"/>: it runs <i>during</i> a successful login, so
/// bumping there would invalidate the token that login is about to issue, logging the user straight back out
/// on every sign-in whose stored hash needs upgrading. That is plan risk R-7, and it is pinned below.
/// </summary>
public class UserTokenVersionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static User LocalUser() =>
        User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.tn", "STORED-HASH", "Dr House");

    [Fact]
    public void Setting_a_password_revokes_existing_tokens() // [AC-5.2] voluntary change, admin reset, CLI
    {
        var user = LocalUser();
        var before = user.TokenVersion;

        user.SetPassword("NEW-HASH");

        Assert.True(user.TokenVersion > before);
    }

    [Fact]
    public void A_forced_reset_also_revokes() // admin reset goes through SetPassword with mustChange: true
    {
        var user = LocalUser();
        var before = user.TokenVersion;

        user.SetPassword("NEW-HASH", mustChangePassword: true);

        Assert.True(user.TokenVersion > before);
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public void Deactivating_revokes_existing_tokens() // [AC-5.2]
    {
        var user = LocalUser();
        var before = user.TokenVersion;

        user.Deactivate();

        Assert.True(user.TokenVersion > before);
        Assert.False(user.IsActive);
    }

    // ---- R-7: the trap ----

    [Fact]
    public void Upgrading_the_hash_does_NOT_revoke() // [AC-5.11] — the whole point of this test
    {
        // This runs mid-login, after the password verified. Bumping here would kill the token the same login
        // is about to hand back, so every sign-in that upgrades a hash would appear to fail.
        var user = LocalUser();
        var before = user.TokenVersion;

        user.UpgradePasswordHash("UPGRADED-HASH");

        Assert.Equal(before, user.TokenVersion);
    }

    [Fact]
    public void Recording_a_successful_login_does_not_revoke()
    {
        var user = LocalUser();
        var before = user.TokenVersion;

        user.RecordSuccessfulLogin();

        Assert.Equal(before, user.TokenVersion);
    }

    [Fact]
    public void Recording_a_failed_login_does_not_revoke() // a wrong password must not log out other devices
    {
        var user = LocalUser();
        var before = user.TokenVersion;

        user.RecordFailedLogin();

        Assert.Equal(before, user.TokenVersion);
    }

    [Fact]
    public void Reactivating_does_not_revoke() // the deactivation already did; no reason to invalidate again
    {
        var user = LocalUser();
        user.Deactivate();
        var afterDeactivate = user.TokenVersion;

        user.Activate();

        Assert.Equal(afterDeactivate, user.TokenVersion);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Each_revoking_change_bumps_again() // two resets must not collide on one version
    {
        var user = LocalUser();

        user.SetPassword("A");
        var first = user.TokenVersion;
        user.SetPassword("B");

        Assert.True(user.TokenVersion > first);
    }
}
