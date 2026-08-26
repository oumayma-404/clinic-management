using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The single-use, time-bounded token behind « mot de passe oublié ».
/// </summary>
public class PasswordResetRequestTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private static PasswordResetRequest Create(DateTime? at = null) =>
        PasswordResetRequest.Create("local|abc", "Dr.House@Clinic.TN", "HASH-1", at ?? Now);

    // The address is stored normalised — the same spelling `User` holds, or the lookup that finds this row would
    // answer against a different one.
    [Fact]
    public void Create_Normalises_The_Address()
    {
        Assert.Equal("dr.house@clinic.tn", Create().Email);
    }

    [Fact]
    public void Create_Requires_A_Target_And_An_Address_And_A_Token()
    {
        Assert.Throws<ArgumentException>(() => PasswordResetRequest.Create("", "a@b.tn", "HASH", Now));
        Assert.Throws<ArgumentException>(() => PasswordResetRequest.Create("local|abc", " ", "HASH", Now));
        Assert.Throws<ArgumentException>(() => PasswordResetRequest.Create("local|abc", "a@b.tn", "", Now));
    }

    // One hour, and deliberately not `ClinicSignup`'s twenty-four: this link replaces the credential of an account
    // that already holds patient records.
    [Fact]
    public void A_Fresh_Row_Expires_One_Hour_Out_And_Is_Usable_Until_Then()
    {
        var row = Create();

        Assert.Equal(TimeSpan.FromHours(1), PasswordResetRequest.TokenLifetime);
        Assert.Equal(Now.AddHours(1), row.ExpiresAtUtc);
        Assert.True(row.IsUsable(Now));
        Assert.True(row.IsUsable(Now.AddMinutes(59)));
        Assert.False(row.IsUsable(Now.AddHours(1)));
        Assert.False(row.IsUsable(Now.AddHours(2)));
    }

    // Single use: the token is what a second attempt is refused on.
    [Fact]
    public void A_Consumed_Row_Is_No_Longer_Usable()
    {
        var row = Create();
        row.Consume(Now.AddMinutes(5));

        Assert.False(row.IsUsable(Now.AddMinutes(6)));
        Assert.Equal(Now.AddMinutes(5), row.ConsumedAtUtc);
    }

    /// <summary>
    /// ⚠️ The behaviour that separates this entity from <c>ClinicSignup</c>, which needed two methods to avoid it:
    /// re-arming is unconditional and CLEARS <c>ConsumedAtUtc</c>. A spent row must be reusable, or somebody who
    /// resets their password once could never do it again — single use is a property of a token, and the token is
    /// what this replaces. It is safe here only because the row carries no credential for a stranger's request to
    /// overwrite.
    /// </summary>
    [Fact]
    public void Rearm_Revives_A_Spent_Row_With_A_New_Token()
    {
        var row = Create();
        row.Consume(Now.AddMinutes(5));

        row.Rearm("HASH-2", Now.AddHours(3));

        Assert.Null(row.ConsumedAtUtc);
        Assert.Equal("HASH-2", row.TokenHash);
        Assert.Equal(Now.AddHours(4), row.ExpiresAtUtc);
        Assert.True(row.IsUsable(Now.AddHours(3)));
    }

    [Fact]
    public void Rearm_Requires_A_Token()
    {
        Assert.Throws<ArgumentException>(() => Create().Rearm(" ", Now));
    }

    // Derived from the expiry so no column carries it — the per-account cooldown reads this.
    [Fact]
    public void LastIssuedAtUtc_Is_Derived_From_The_Expiry()
    {
        var row = Create();
        Assert.Equal(Now, row.LastIssuedAtUtc());

        row.Rearm("HASH-2", Now.AddMinutes(30));
        Assert.Equal(Now.AddMinutes(30), row.LastIssuedAtUtc());
    }

    [Fact]
    public void Email_Send_Attempts_Accumulate()
    {
        var row = Create();
        Assert.Equal(0, row.EmailSendAttempts);

        row.RecordEmailSendAttempt();
        row.RecordEmailSendAttempt();

        Assert.Equal(2, row.EmailSendAttempts);
    }

    // Lowercase hex SHA-256, and stable: the write side and the read side must hash identically or no link ever
    // verifies.
    [Fact]
    public void HashToken_Is_Stable_Lowercase_Hex_And_Distinguishes_Tokens()
    {
        var hash = PasswordResetRequest.HashToken("a-raw-token");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Equal(hash, PasswordResetRequest.HashToken("a-raw-token"));
        Assert.NotEqual(hash, PasswordResetRequest.HashToken("a-raw-tokeo"));
    }

    [Fact]
    public void TokenHashMatches_Compares_Equal_Hashes_And_Rejects_Others()
    {
        var hash = PasswordResetRequest.HashToken("token");

        Assert.True(PasswordResetRequest.TokenHashMatches(hash, hash));
        Assert.False(PasswordResetRequest.TokenHashMatches(hash, PasswordResetRequest.HashToken("other")));
        // Differing lengths must not throw — FixedTimeEquals returns false rather than raising.
        Assert.False(PasswordResetRequest.TokenHashMatches(hash, "short"));
    }

    /// <summary>
    /// ⚠️ <b>No <c>ClinicId</c>, asserted rather than assumed.</b> Its absence is what puts this table outside the
    /// EF tenant query filter by construction — <c>TenantScopeFilterTests</c> derives the clinic-owned set from the
    /// presence of that very property — and both endpoints reading it are anonymous, so a filtered read would return
    /// zero rows with no error. Somebody adding the column for tidiness would break the feature silently, which is
    /// exactly the failure this asserts against.
    /// </summary>
    [Fact]
    public void The_Entity_Carries_No_ClinicId()
    {
        Assert.Null(typeof(PasswordResetRequest).GetProperty("ClinicId"));
    }
}
