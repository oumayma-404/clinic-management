using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

public class UserLocalAuthTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // [FR-B1] Local user factory: stable local| id, normalized email, active, password-backed.
    [Fact]
    public void CreateLocalUser_Should_Normalize_And_Set_Fields()
    {
        var user = User.CreateLocalUser(ClinicId, "doctor", "  Doc@Clinic.COM ", "HASH", "  Dr House  ");

        Assert.StartsWith("local|", user.Id);
        Assert.Equal("doc@clinic.com", user.Email);
        Assert.Equal("Dr House", user.FullName);
        Assert.True(user.IsActive);
        Assert.False(user.MustChangePassword);
        Assert.True(user.IsLocalAccount());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateLocalUser_Should_Reject_Blank_Email(string email)
    {
        Assert.Throws<ArgumentException>(() =>
            User.CreateLocalUser(ClinicId, "doctor", email, "HASH", "Dr House"));
    }

    // [AC-3.4] Lockout triggers once the failed-attempt threshold is crossed.
    [Fact]
    public void RecordFailedLogin_Should_Lock_After_Threshold()
    {
        var user = User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "HASH", "Dr House");

        for (var i = 0; i < User.MaxFailedLoginAttempts - 1; i++) user.RecordFailedLogin();
        Assert.False(user.IsLockedOut());

        user.RecordFailedLogin(); // crosses the threshold
        Assert.True(user.IsLockedOut());
        Assert.Equal(0, user.FailedLoginAttempts); // reset when the lockout is applied
    }

    // Successful login clears failed attempts + lockout and stamps LastLoginAt.
    [Fact]
    public void RecordSuccessfulLogin_Should_Clear_Lockout_State()
    {
        var user = User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "HASH", "Dr House");
        user.RecordFailedLogin();

        user.RecordSuccessfulLogin();

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.False(user.IsLockedOut());
        Assert.NotNull(user.LastLoginAt);
    }

    // [AC-5.2] Setting a new password can force a change at next login and clears lockout.
    [Fact]
    public void SetPassword_Should_Update_Hash_And_Force_Change()
    {
        var user = User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "OLD", "Dr House");
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++) user.RecordFailedLogin();

        user.SetPassword("NEW", mustChangePassword: true);

        Assert.Equal("NEW", user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.False(user.IsLockedOut());
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    // [AC-5.3] Deactivate / reactivate toggles the active flag.
    [Fact]
    public void Deactivate_And_Activate_Should_Toggle_IsActive()
    {
        var user = User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "HASH", "Dr House");

        user.Deactivate();
        Assert.False(user.IsActive);

        user.Activate();
        Assert.True(user.IsActive);
    }

    // A Cloud (Auth0) user has no password → not a local account.
    [Fact]
    public void CloudUser_Should_Not_Be_Local_Account()
    {
        var user = new User("auth0|123", ClinicId, "doctor", "doc@clinic.com", "Dr House");
        Assert.False(user.IsLocalAccount());
    }
}
