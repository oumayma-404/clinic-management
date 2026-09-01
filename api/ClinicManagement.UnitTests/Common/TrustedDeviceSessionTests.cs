using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The pieces « Rester connecté sur cet appareil » is built from, and the three ways they could silently drift
/// apart.
///
/// <para>Each test here exists because its failure mode is <b>quiet</b>: a claim nobody issues reads as
/// « no trusted device ever », a label longer than its column fails at the last save of a completed sign-in, and
/// two identical sign-out reasons make the journal unable to answer the question it was split to answer.</para>
/// </summary>
public class TrustedDeviceSessionTests
{
    // ── The claim name crosses a project boundary as a literal ───────────────────────────────────────────

    /// <summary>
    /// <c>ClinicContext.GetSessionFamilyId</c> reads <c>"family_id"</c> as a **string literal**, because
    /// <c>Application</c> may not reference <c>Infrastructure</c>, where <see cref="LocalAuthClaims"/> lives.
    ///
    /// <para>⚠️ Renaming the constant would leave that reader looking for a claim nothing issues. It would not
    /// throw: <c>Guid.TryParse(null)</c> is false, so every session reports « I cannot tell which device I am »
    /// and « Mes appareils » marks no row « cet appareil » — on a screen whose buttons end sessions.</para>
    /// </summary>
    [Fact]
    public void The_Session_Family_Claim_Name_Is_The_Same_On_Both_Sides_Of_The_Layer_Boundary()
    {
        // Read from the source rather than retyped here — a copy in the test is a third place to drift.
        var reader = File.ReadAllText(Path.Combine(
            SolutionSources.Root().FullName,
            "ClinicManagement.Application", "Common", "Services", "ClinicContext.cs"));

        Assert.Contains($"FindFirst(\"{LocalAuthClaims.SessionFamily}\")", reader, StringComparison.Ordinal);
    }

    // ── The device label's cap is the column's ────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="DeviceLabels.MaxLength"/> must be the width <c>SessionFamilyConfiguration</c> maps.
    ///
    /// <para>⚠️ A label longer than the column is not refused anywhere in the request: it survives validation,
    /// the password check and the second factor, and then PostgreSQL rejects the INSERT at the single save that
    /// ends the sign-in. The user sees a failed login with a correct password and a correct code.</para>
    ///
    /// <para>Derived from the EF model itself, so widening the column and forgetting the constant — or the
    /// reverse — fails here rather than in production.</para>
    /// </summary>
    [Fact]
    public void The_Device_Label_Cap_Matches_The_Column_It_Is_Stored_In()
    {
        var builder = new ModelBuilder();
        new SessionFamilyConfiguration().Configure(builder.Entity<SessionFamily>());

        var mapped = builder.Model
            .FindEntityType(typeof(SessionFamily))!
            .FindProperty(nameof(SessionFamily.DeviceLabel))!
            .GetMaxLength();

        Assert.Equal(mapped, DeviceLabels.MaxLength);
    }

    [Fact]
    public void A_Long_Label_Is_Truncated_Rather_Than_Refused()
    {
        var cleaned = DeviceLabels.Sanitise(new string('x', DeviceLabels.MaxLength + 50));

        Assert.NotNull(cleaned);
        Assert.Equal(DeviceLabels.MaxLength, cleaned!.Length);
    }

    /// <summary>
    /// Control characters go, because the label is written into a journal a person reads. Escaping is a
    /// property of a renderer; a log file has none, and a newline inside a label forges a second line in it.
    /// </summary>
    [Fact]
    public void A_Label_Cannot_Smuggle_A_Newline_Into_The_Journal()
    {
        var cleaned = DeviceLabels.Sanitise("Poste 1\r\n\u001b[ADMIN] session accord\u00e9e\u001b[0m");

        Assert.NotNull(cleaned);
        Assert.DoesNotContain('\n', cleaned!);
        Assert.DoesNotContain('\r', cleaned!);
        Assert.DoesNotContain('\u001b', cleaned!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Nothing_Usable_Becomes_Null_Rather_Than_An_Empty_Name(string? label) =>
        Assert.Null(DeviceLabels.Sanitise(label));

    // ── The two sign-out reasons must stay two ───────────────────────────────────────────────────────────

    /// <summary>
    /// There was <b>one</b> constant for both a chosen sign-out and a 30-minute inactivity sign-out, so the
    /// session table could not tell them apart — and « how often is the timeout actually signing people out? »
    /// had to be answered by reading the browser's source instead of the practice's own records.
    ///
    /// <para>⚠️ Asserting they are simply « not equal » is the whole point: a future edit that reworded one into
    /// the other, or pointed both at the same constant, would restore exactly the state this split removed.</para>
    /// </summary>
    [Fact]
    public void A_Chosen_Sign_Out_And_A_Timeout_Are_Recorded_Differently()
    {
        var chosen = EndSessionCommandHandler.ReasonFor(SessionEnding.UserRequested);
        var timedOut = EndSessionCommandHandler.ReasonFor(SessionEnding.Inactivity);

        Assert.NotEqual(chosen, timedOut);
        Assert.False(string.IsNullOrWhiteSpace(chosen));
        Assert.False(string.IsNullOrWhiteSpace(timedOut));
    }

    /// <summary>
    /// And « Déconnecter » from « Mes appareils » is a third thing again — a device revoked from elsewhere is
    /// not the same event as the browser in front of you being closed.
    /// </summary>
    [Fact]
    public void Revoking_A_Device_Is_Recorded_As_Neither_Of_The_Other_Two()
    {
        Assert.NotEqual(
            EndSessionCommandHandler.ReasonFor(SessionEnding.UserRequested),
            EndOtherSessionCommandHandler.Reason);

        Assert.NotEqual(
            EndSessionCommandHandler.ReasonFor(SessionEnding.Inactivity),
            EndOtherSessionCommandHandler.Reason);
    }

    // ── Trust is a property of the row, and it must survive rotation ──────────────────────────────────────

    /// <summary>
    /// A family created untrusted stays untrusted, and a trusted one stays trusted, across any number of
    /// rotations.
    ///
    /// <para>⚠️ <c>Rotate</c> rewrites the hashes and the expiry on every silent renewal — roughly every half
    /// hour of an open tab. Were it to reset the flag, a trusted session would quietly fall back to 12 hours at
    /// its first renewal and the feature would appear to work all day and fail every night, which is the exact
    /// symptom it was built to remove.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rotation_Preserves_Whether_The_Device_Was_Trusted(bool trusted)
    {
        var family = new SessionFamily(
            "user-1", SessionCredential.Hash("first"), DateTime.UtcNow.AddHours(12), "Poste du cabinet", trusted);

        family.Rotate(SessionCredential.Hash("second"), DateTime.UtcNow.AddHours(12));
        family.Rotate(SessionCredential.Hash("third"), DateTime.UtcNow.AddHours(12));

        Assert.Equal(trusted, family.IsTrusted);
    }

    /// <summary>
    /// A family exposes no way to become trusted after the fact. Trust is asserted by somebody who has just
    /// presented a password and a second factor; a setter reachable later would be a way to lengthen a session
    /// without ever re-authenticating.
    /// </summary>
    [Fact]
    public void There_Is_No_Way_To_Trust_A_Session_After_It_Has_Been_Opened()
    {
        // ⚠️ `IsSpecialName` excludes property accessors, and leaving it out is how this test first went red on
        // `get_IsTrusted` — the reader the feature is built on. What must not exist is a *method* that sets it;
        // the getter existing is the point. Kept as a note because the mistake is one line and reads as correct.
        var mutators = typeof(SessionFamily)
            .GetMethods()
            .Where(m => !m.IsSpecialName)
            .Where(m => m.Name.Contains("Trust", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        // And the setter itself must stay private — a public one would be a mutator this scan cannot name.
        var setter = typeof(SessionFamily).GetProperty(nameof(SessionFamily.IsTrusted))!.SetMethod;
        Assert.True(
            setter is null || !setter.IsPublic,
            "SessionFamily.IsTrusted has a public setter. Trust is asserted once by somebody who has just "
            + "presented a password and a second factor; anything that can raise it later extends a live session "
            + "with no re-authentication.");

        Assert.True(
            mutators.Count == 0,
            "SessionFamily now exposes " + string.Join(", ", mutators)
            + ". Trust is set once, at sign-in, and raising it later would extend a live session without any "
            + "re-authentication. If this is deliberate, the entity's own note has to change first.");
    }

    // ── The two lifetimes are actually different ─────────────────────────────────────────────────────────

    /// <summary>
    /// The trusted lifetime must be longer than the ordinary one, and by enough to survive a night.
    ///
    /// <para>⚠️ The bug being ruled out is not « somebody set it to 5 minutes » — it is a configuration key
    /// typed into the wrong place, leaving both reads resolving to 720 and the whole feature inert with a green
    /// build. A cabinet closing at 18 h and opening at 8 h 30 leaves a <b>14½-hour</b> gap; anything at or below
    /// that is indistinguishable from not having shipped this.</para>
    /// </summary>
    [Fact]
    public void A_Trusted_Session_Outlives_A_Night_And_An_Ordinary_One_Does_Not()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var ordinary = LocalAuthConfig.TokenLifetimeMinutes(configuration);
        var trusted = LocalAuthConfig.TrustedTokenLifetimeMinutes(configuration);

        const int overnightGapMinutes = 14 * 60 + 30;

        Assert.True(
            ordinary < overnightGapMinutes,
            $"The ordinary session is {ordinary} min, which already spans a night — the premise of this feature "
            + "has changed and its documentation is now wrong.");

        Assert.True(
            trusted > overnightGapMinutes,
            $"A trusted session is {trusted} min, which does not survive the 14½ h between closing and opening. "
            + "It would be a longer session that still asks for the authenticator every morning.");
    }
}
