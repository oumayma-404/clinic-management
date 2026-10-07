using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The PC de secours aggregate (<c>clinic-pc-copy</c> Part 1): one setup attempt, its one-time code, its secret, and
/// which rows still hold the clinic's one place. Every instant is a fixed literal — nothing here reads the clock.
/// </summary>
public class ClinicRelayTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private const string PublicKey = "MIIBIjANBgkq";

    private static (ClinicRelay Relay, string Code) Begin(DateTime? at = null) =>
        ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", at ?? T0);

    private static ClinicRelay Paired(out string secret, DateTime? at = null)
    {
        var (relay, _) = Begin(at);
        secret = relay.Pair("PC-ACCUEIL", PublicKey, null, "192.168.1.10", "1.4.0", at ?? T0);
        return relay;
    }

    private static RelayHeartbeat Beat(
        long applied = 0, bool seedComplete = false, string? error = null, IReadOnlyList<string>? mismatch = null,
        long? diskFree = null, bool updating = false) =>
        new(applied, seedComplete ? 100 : 40, seedComplete, 10, 5, diskFree, updating, "1.4.0", null, null, mismatch, error);

    // [AC-1.4] A setup starts with a code that is never stored in clear and is shown once.
    [Fact]
    public void Beginning_A_Setup_Stores_Only_The_Codes_Hash()
    {
        var (relay, code) = Begin();

        Assert.Equal(ClinicRelayStatus.Pairing, relay.Status);
        Assert.NotEqual(code, relay.PairingCodeHash);
        Assert.Equal(ClinicRelay.Hash(code), relay.PairingCodeHash);
        Assert.Equal(T0 + ClinicRelay.PairingCodeLifetime, relay.PairingCodeExpiresAtUtc);
    }

    [Fact]
    public void A_Code_Matches_Only_Before_It_Expires()
    {
        var (relay, code) = Begin();

        Assert.True(relay.CodeMatches(code, T0.AddMinutes(9)));
        Assert.False(relay.CodeMatches(code, T0 + ClinicRelay.PairingCodeLifetime));
        Assert.False(relay.CodeMatches("autre-code", T0.AddMinutes(1)));
    }

    // [D8] The code is single-use: pairing clears it, so the same code cannot pair a second PC.
    [Fact]
    public void A_Code_Is_Spent_By_Pairing()
    {
        var (relay, code) = Begin();
        relay.Pair("PC-ACCUEIL", PublicKey, null, null, null, T0.AddMinutes(1));

        Assert.Null(relay.PairingCodeHash);
        Assert.False(relay.CodeMatches(code, T0.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            relay.Pair("PC-AUTRE", PublicKey, null, null, null, T0.AddMinutes(2)));
    }

    [Fact]
    public void Pairing_Requires_A_Public_Key()
    {
        var (relay, _) = Begin();

        Assert.Throws<ArgumentException>(() => relay.Pair("PC-ACCUEIL", " ", null, null, null, T0));
    }

    [Fact]
    public void Pairing_Hands_Back_A_Secret_That_Only_It_Matches()
    {
        var relay = Paired(out var secret);

        Assert.Equal(ClinicRelayStatus.Seeding, relay.Status);
        Assert.True(relay.SecretMatches(secret));
        Assert.False(relay.SecretMatches(secret + "x"));
        Assert.NotEqual(secret, relay.SecretHash);
    }

    // A fingerprint that is not a SHA-256 is dropped rather than pinned: a device pinning garbage trusts nothing.
    [Theory]
    [InlineData("AB:CD", null)]
    [InlineData("ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89",
        "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void A_Certificate_Fingerprint_Is_Kept_Only_When_It_Is_A_Sha256(string given, string? kept)
    {
        var (relay, _) = Begin();
        relay.Pair("PC-ACCUEIL", PublicKey, given, null, null, T0);

        Assert.Equal(kept, relay.CertificateFingerprint);
    }

    // [AC-1.10][AC-1.11][EC-9] Which rows hold the clinic's one place.
    [Fact]
    public void An_Unused_Code_Holds_The_Place_Until_It_Expires()
    {
        var (relay, _) = Begin();

        Assert.True(relay.OccupiesTheClinic(T0.AddMinutes(5)));
        Assert.False(relay.OccupiesTheClinic(T0 + ClinicRelay.PairingCodeLifetime));
    }

    [Fact]
    public void A_Setup_That_Never_Finishes_Its_First_Copy_Is_Abandoned_After_A_Day()
    {
        var relay = Paired(out _);

        Assert.False(relay.IsAbandoned(T0.AddHours(23)));
        Assert.True(relay.OccupiesTheClinic(T0.AddHours(23)));
        Assert.True(relay.IsAbandoned(T0 + ClinicRelay.AbandonedAfter));
        Assert.False(relay.OccupiesTheClinic(T0 + ClinicRelay.AbandonedAfter));
    }

    // A heartbeat during the first copy keeps the setup alive: « abandoned » counts from the last contact.
    [Fact]
    public void A_Heartbeat_During_The_First_Copy_Postpones_Abandonment()
    {
        var relay = Paired(out _);
        relay.RecordHeartbeat(Beat(), highWater: 0, T0.AddHours(20));

        Assert.False(relay.IsAbandoned(T0.AddHours(30)));
    }

    [Fact]
    public void An_Active_Relay_Holds_The_Place_And_A_Retired_One_Does_Not()
    {
        var relay = Paired(out _);
        relay.RecordHeartbeat(Beat(seedComplete: true), 0, T0.AddMinutes(30));

        Assert.True(relay.OccupiesTheClinic(T0.AddYears(1)));

        relay.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddDays(2));

        Assert.False(relay.OccupiesTheClinic(T0.AddDays(2)));
    }

    // [FR-8] The heartbeat that reports the first copy complete is the one that turns the relay Active, once.
    [Fact]
    public void The_First_Complete_Copy_Activates_The_Relay_Once()
    {
        var relay = Paired(out _);

        Assert.False(relay.RecordHeartbeat(Beat(seedComplete: false), 0, T0.AddMinutes(1)));
        Assert.True(relay.RecordHeartbeat(Beat(seedComplete: true), 0, T0.AddMinutes(2)));
        Assert.False(relay.RecordHeartbeat(Beat(seedComplete: true), 0, T0.AddMinutes(3)));

        Assert.Equal(ClinicRelayStatus.Active, relay.Status);
        Assert.Equal(T0.AddMinutes(2), relay.SeededAtUtc);
    }

    // [FR-2] « Prêt » is holding everything up to the cloud's high-water at its last answer.
    [Fact]
    public void Ready_Means_Applied_Up_To_The_High_Water_Of_The_Last_Answer()
    {
        var relay = Paired(out _);
        relay.RecordHeartbeat(Beat(applied: 40, seedComplete: true), highWater: 42, T0.AddMinutes(1));

        Assert.False(relay.IsCaughtUp);
        Assert.Null(relay.LastReadyAtUtc);

        relay.RecordHeartbeat(Beat(applied: 42, seedComplete: true), highWater: 42, T0.AddMinutes(2));

        Assert.True(relay.IsCaughtUp);
        Assert.Equal(T0.AddMinutes(2), relay.LastReadyAtUtc);
    }

    // [FR-9] A difference the PC could not repair is remembered from when it began, and clears when it is gone.
    [Fact]
    public void A_Mismatch_Keeps_Its_First_Instant_And_Clears_When_Reported_Gone()
    {
        var relay = Paired(out _);
        relay.RecordHeartbeat(Beat(seedComplete: true, mismatch: new[] { "Patient" }), 0, T0.AddMinutes(1));
        relay.RecordHeartbeat(Beat(seedComplete: true, mismatch: new[] { "Patient", "Invoice" }), 0, T0.AddMinutes(9));

        Assert.Equal(T0.AddMinutes(1), relay.MismatchSinceUtc);
        Assert.Equal("Invoice,Patient", relay.MismatchTables);

        relay.RecordHeartbeat(Beat(seedComplete: true), 0, T0.AddMinutes(10));

        Assert.Null(relay.MismatchSinceUtc);
        Assert.Null(relay.MismatchTables);
    }

    [Fact]
    public void A_Retired_Relay_Ignores_Heartbeats()
    {
        var relay = Paired(out _);
        relay.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddMinutes(1));

        Assert.False(relay.RecordHeartbeat(Beat(seedComplete: true), 0, T0.AddMinutes(2)));
        Assert.Equal(ClinicRelayStatus.Retired, relay.Status);
        Assert.NotEqual(T0.AddMinutes(2), relay.LastSeenAtUtc);
    }

    // The first retirement is the one that counts — a second press cannot rewrite who retired it or why.
    [Fact]
    public void Retirement_Is_Idempotent()
    {
        var relay = Paired(out var secret);
        relay.Retire(ClinicRelayRetirement.Retired, "local|first", T0.AddMinutes(1));
        relay.Retire(ClinicRelayRetirement.LostOrStolen, "local|second", T0.AddMinutes(2));

        Assert.Equal(ClinicRelayRetirement.Retired, relay.RetiredReason);
        Assert.Equal("local|first", relay.RetiredByUserId);
        Assert.Equal(T0.AddMinutes(1), relay.RetiredAtUtc);
        // The secret survives, so the PC learns « retiré » on its next token exchange (AC-8.1).
        Assert.True(relay.SecretMatches(secret));
    }

    // Found by the first end-to-end run: a 76-character build string failed the pairing save. What the PC reports is
    // capped where it is written, so a long report updates the row instead of refusing it.
    [Fact]
    public void Long_Reports_From_The_Pc_Are_Capped_To_Their_Columns()
    {
        var (relay, _) = Begin();
        relay.Pair("PC", PublicKey, null, null, new string('b', 500), T0);

        Assert.Equal(ClinicRelay.MaxBuildLength, relay.Build!.Length);

        relay.RecordHeartbeat(new RelayHeartbeat(0, 0, false, 0, 0, null, false, new string('b', 500), null, null,
            Enumerable.Range(0, 400).Select(i => "Table" + i).ToList(), new string('e', 5000)), 0, T0.AddMinutes(1));

        Assert.Equal(ClinicRelay.MaxErrorLength, relay.LastError!.Length);
        Assert.Equal(ClinicRelay.MaxMismatchLength, relay.MismatchTables!.Length);
        Assert.Equal(ClinicRelay.MaxBuildLength, relay.Build!.Length);
    }

    [Fact]
    public void A_Relay_Belongs_To_A_Clinic()
    {
        Assert.Throws<ArgumentException>(() => ClinicRelay.BeginPairing(Guid.Empty, "PC", "local|a", T0));
    }

    // Its token subject is never mistaken for a person's.
    [Fact]
    public void Its_Subject_Is_Not_A_User_Id()
    {
        var (relay, _) = Begin();

        Assert.StartsWith(ClinicRelay.SubjectPrefix, relay.Subject);
        Assert.DoesNotContain("local|", relay.Subject);
    }
}
