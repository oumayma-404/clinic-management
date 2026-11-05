using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The two facts <c>guided-first-days</c> Part A adds to an account: when its pending authenticator QR was issued
/// (so a repeat shows the same one), and when it first signed in from the phone app (« Premiers pas »).
/// </summary>
public class UserOnboardingSignInTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static User LocalUser() =>
        User.CreateLocalUser(ClinicId, User.RoleAdmin, "doc@clinic.tn", "STORED-HASH", "Dr House");

    // ── The pending QR ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_secret_issued_an_hour_ago_and_not_confirmed_is_reusable()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A", DateTime.UtcNow.AddHours(-1));

        Assert.True(user.HasReusablePendingTotpSecret(Day));
    }

    [Fact]
    public void A_secret_issued_more_than_a_day_ago_is_not_reusable()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A", DateTime.UtcNow.AddHours(-25));

        Assert.False(user.HasReusablePendingTotpSecret(Day));
    }

    [Fact]
    public void An_account_with_no_secret_has_nothing_to_reuse()
    {
        Assert.False(LocalUser().HasReusablePendingTotpSecret(Day));
    }

    // A confirmed factor is never « pending » — re-showing it would be handing out a live second factor.
    [Fact]
    public void A_confirmed_secret_is_never_reusable_and_clears_the_issue_time()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A");
        user.CompleteTotpEnrolment(new[] { UserRecoveryCode.NewCode() });

        Assert.False(user.HasReusablePendingTotpSecret(Day));
        Assert.Null(user.TotpEnrolmentStartedAt);
    }

    [Fact]
    public void Resetting_the_factor_clears_the_issue_time()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A");

        user.DisableTotp();

        Assert.Null(user.TotpEnrolmentStartedAt);
        Assert.False(user.HasReusablePendingTotpSecret(Day));
    }

    // ⚠️ A stolen PC de secours held a sealed copy of the pending secret too; with the 24 h reuse, keeping it
    // would hand that copy back to whoever enrols next.
    [Fact]
    public void Requiring_new_credentials_discards_a_pending_secret()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A");

        user.RequireNewCredentials();

        Assert.Null(user.ProtectedTotpSecret);
        Assert.Null(user.TotpEnrolmentStartedAt);
        Assert.False(user.HasReusablePendingTotpSecret(Day));
    }

    [Fact]
    public void A_new_password_discards_a_pending_secret()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A");

        user.SetPassword("NEW-HASH");

        Assert.Null(user.ProtectedTotpSecret);
        Assert.Null(user.TotpEnrolmentStartedAt);
    }

    [Fact]
    public void A_new_password_keeps_a_confirmed_factor()
    {
        var user = LocalUser();
        user.IssueTotpSecret("protected:A");
        user.CompleteTotpEnrolment(new[] { UserRecoveryCode.NewCode() });

        user.SetPassword("NEW-HASH");

        Assert.True(user.IsTotpEnrolled);
        Assert.Equal("protected:A", user.ProtectedTotpSecret);
    }

    // ── The phone app ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(User.PhoneAppSurfaces.Android)]
    [InlineData(User.PhoneAppSurfaces.IosHomeScreen)]
    [InlineData(User.PhoneAppSurfaces.Ios)]
    public void The_first_phone_app_sign_in_is_recorded(string surface)
    {
        var user = LocalUser();
        var at = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

        Assert.True(user.RecordPhoneAppSignIn(surface, at));

        Assert.Equal(at, user.PhoneAppFirstSignInAt);
        Assert.Equal(surface, user.PhoneAppPlatform);
    }

    // The Windows shell and a browser are not « the phone app », and an unknown value is not either.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("windows")]
    [InlineData("browser")]
    [InlineData("ANDROID")]
    public void Any_other_surface_records_nothing(string? surface)
    {
        var user = LocalUser();

        Assert.False(user.RecordPhoneAppSignIn(surface));

        Assert.Null(user.PhoneAppFirstSignInAt);
        Assert.Null(user.PhoneAppPlatform);
    }

    // Written once: a later sign-in from the other phone does not move the date or the platform.
    [Fact]
    public void A_later_phone_sign_in_changes_nothing()
    {
        var user = LocalUser();
        var first = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
        user.RecordPhoneAppSignIn(User.PhoneAppSurfaces.Android, first);

        Assert.False(user.RecordPhoneAppSignIn(User.PhoneAppSurfaces.IosHomeScreen, first.AddDays(2)));

        Assert.Equal(first, user.PhoneAppFirstSignInAt);
        Assert.Equal(User.PhoneAppSurfaces.Android, user.PhoneAppPlatform);
    }
}
