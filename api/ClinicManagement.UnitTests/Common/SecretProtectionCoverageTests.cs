using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every credential-shaped column is encrypted at rest, or is a named decision</b>
/// (<c>hosted-security-hardening</c> FR-3.4, step 16).
///
/// <para><b>The criterion, stated once:</b> for each persisted property whose name looks like a secret
/// (<see cref="SecretShapedNames"/>), either it holds Data-Protection ciphertext, or this file says in words why
/// plaintext is acceptable for it. There is no third answer, and « nobody has looked » is not one of them.</para>
///
/// <para><b>Why a derived guard and not a checklist.</b> FR-3.4 closed the last plaintext credential in this
/// database — the Google Calendar refresh token — which had sat there since the per-clinic connection shipped,
/// through several security features, unnoticed. Nothing could have seen it: the column worked, the sync worked,
/// every test passed. A hand-written list of « secrets we encrypt » would have been just as blind, because it
/// cannot fail on the case it was never told about, which is the only case a guard exists for. So the candidate
/// set is reflected off the EF model and the <i>exceptions</i> are what is written down.</para>
///
/// <para><b>Asserted in both directions.</b> An entry naming a column that no longer exists is worse than a
/// missing one: it is a pre-approved hole waiting for a future property to be renamed into it.</para>
///
/// <para>⚠️ <b>Encryption is judged by the <c>Protected</c>/<c>Encrypted</c> naming convention</b>, which is a
/// real limitation and is stated rather than hidden: this suite touches no database and no key ring, so it cannot
/// observe a column's actual contents. What it does guarantee is that a new credential-shaped column cannot be
/// added without somebody typing either that suffix or a reason here. The figure that proves ciphertext really is
/// ciphertext is <c>verify-schema</c>'s <c>secrets-protected-under-current-ring</c>, on a live deployment.</para>
///
/// <para><b>No database is touched</b>: Npgsql needs a syntactically valid connection string to build a model,
/// never a reachable server.</para>
/// </summary>
public class SecretProtectionCoverageTests
{
    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// The columns whose name looks like a credential and which are nonetheless stored in the clear, each with
    /// the reason. A mandatory reason rather than a bare list, on <c>AllowsWithoutSubscription</c>'s precedent:
    /// every entry has to answer « why is this readable off a stolen disk? » where a reader finds it, and a bare
    /// set would grow by copy-paste.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PlaintextByDecision =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{nameof(Clinic)}.{nameof(Clinic.GoogleRefreshToken)}"] =
                "FR-3.4's legacy column, emptied clinic by clinic by the startup backfill and dropped in a later "
                + "migration once verify-schema's google-token-protected reads zero on the live deployment. "
                + "Nothing writes it any more.",

            [$"{nameof(User)}.{nameof(User.PasswordHash)}"] =
                "A PBKDF2 hash, not a credential: it is deliberately not reversible, and encrypting it would put "
                + "sign-in behind the key ring — so a lost ring would lock every account out of a deployment "
                + "instead of costing only the second factor.",

            [$"{nameof(PlatformAccount)}.{nameof(PlatformAccount.PasswordHash)}"] =
                "The console's twin of the row above, and the same reasoning.",

            [$"{nameof(ClinicSignup)}.{nameof(ClinicSignup.PasswordHash)}"] =
                "The password a visitor chose, hashed the same way and for the same reason as the two rows "
                + "above — it becomes User.PasswordHash verbatim when the signup is verified.",

            [$"{nameof(DeviceRegistration)}.{nameof(DeviceRegistration.Token)}"] =
                "The OS-issued push routing token. It is not our credential — sending to it requires the "
                + "deployment's own FCM/APNs keys, which are configuration and never in this database — and it "
                + "carries a UNIQUE index, which is what makes rebinding a shared reception tablet one "
                + "deterministic write instead of a conflict. Ciphertext is not equality-searchable, so "
                + "encrypting it would trade nothing for that guarantee.",

            // The four below hold no secret at all and are matched only because a marker appears in the name.
            // They are listed rather than pattern-excluded on purpose: a pattern that skipped « Version » or
            // « MustChange » would skip whatever is named that way next, which is how a real credential slips
            // through a guard whose whole job is to make somebody look.
            [$"{nameof(PlatformAccount)}.{nameof(PlatformAccount.MustChangePassword)}"] =
                "A boolean flag, matched only because « Password » appears in its name. It holds no secret and "
                + "the guard is deliberately crude — see SecretShapedNames.",

            [$"{nameof(User)}.{nameof(User.MustChangePassword)}"] =
                "The clinic twin of the flag above: a boolean saying a password change is owed, holding no secret.",

            [$"{nameof(PlatformAccount)}.{nameof(PlatformAccount.TokenVersion)}"] =
                "A monotonically increasing integer, not a token: bumping it is what REVOKES every token already "
                + "issued. It is compared on every request, so it could not be ciphertext even in principle.",

            [$"{nameof(User)}.{nameof(User.TokenVersion)}"] =
                "The clinic twin of the counter above, read on every authenticated request to revoke stale tokens.",

            [$"{nameof(SessionFamily)}.{nameof(SessionFamily.CurrentCredentialHash)}"] =
                "A hash of the refresh credential, kept so a REPLAYED one is detected (Part A) — the row is "
                + "already the non-reversible half, and it is matched by equality on every refresh, which "
                + "ciphertext is not.",

            [$"{nameof(SessionFamily)}.{nameof(SessionFamily.PreviousCredentialHash)}"] =
                "The superseded generation of the hash above, kept for the same detection and on the same terms.",

            [$"{nameof(ClinicSignup)}.{nameof(ClinicSignup.TokenHash)}"] =
                "A SHA-256 of the single-use verification token, which exists in plaintext only in the e-mail. "
                + "Encrypting a hash would buy nothing — the row is already the non-reversible half — while "
                + "putting the public signup path behind the key ring, so a ring problem would stop new "
                + "cabinets from being created at all.",

            [$"{nameof(PasswordResetRequest)}.{nameof(PasswordResetRequest.TokenHash)}"] =
                "The row above's twin, and the same reasoning: a SHA-256 of the single-use reset token, which "
                + "exists in plaintext only in the e-mail that carried it. Encrypting a hash would buy nothing — "
                + "the row is already the non-reversible half — while putting the one recovery path a locked-out "
                + "person can take alone behind the key ring, so a ring problem would turn a forgotten password "
                + "into a support call.",
        };

    /// <summary>Every persisted, non-shadow property whose name looks like a secret.</summary>
    private static List<string> Candidates(ApplicationDbContext db) =>
        db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(p => !p.IsShadowProperty() && SecretShapedNames.Matches(p.Name))
                .Select(p => $"{entity.ClrType.Name}.{p.Name}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The rule, as a pure function of the model and a decision map, so the red proof below can run the
    /// <b>real</b> classifier with a hole punched in it rather than asking a reviewer to break something by hand.
    /// </summary>
    private static List<string> Unaccounted(
        IEnumerable<string> candidates, IReadOnlyDictionary<string, string> decisions) =>
        candidates
            .Where(name => !LooksEncrypted(name) && !decisions.ContainsKey(name))
            .ToList();

    private static bool LooksEncrypted(string qualifiedName) =>
        qualifiedName.Contains("Protected", StringComparison.Ordinal)
        || qualifiedName.EndsWith("Encrypted", StringComparison.Ordinal);

    // Non-vacuity first: a reflection guard fails OPEN, so a renamed namespace or a changed model API would leave
    // every case below passing for ever while looking at nothing. SystemWideCallerCoverageTests learned this the
    // expensive way — its console-verb branch matched zero types for four features.
    [Fact]
    public void The_Guard_Actually_Finds_Credential_Shaped_Columns() // [FR-3.4]
    {
        using var db = Context();
        var candidates = Candidates(db);

        Assert.NotEmpty(candidates);
        Assert.Contains($"{nameof(Clinic)}.{nameof(Clinic.GoogleRefreshTokenProtected)}", candidates);
        Assert.Contains($"{nameof(User)}.{nameof(User.ProtectedTotpSecret)}", candidates);
        Assert.Contains(
            $"{nameof(ClinicReminderSettings)}.{nameof(ClinicReminderSettings.SmsApiKeyEncrypted)}", candidates);
    }

    // The guard itself. A new credential-shaped column fails this until somebody either encrypts it or writes
    // down why it may sit in the clear — which is the entire point: FR-3.4's own defect survived years of review
    // precisely because nothing anywhere asked the question.
    [Fact]
    public void Every_Credential_Shaped_Column_Is_Encrypted_Or_A_Named_Decision() // [FR-3.4]
    {
        using var db = Context();

        var unaccounted = Unaccounted(Candidates(db), PlaintextByDecision);

        Assert.True(unaccounted.Count == 0,
            "These columns look like credentials and are neither encrypted nor a named decision. Either store "
            + "ciphertext (a « Protected »/« Encrypted » property), or add an entry to PlaintextByDecision "
            + "saying why plaintext is acceptable:\n  - " + string.Join("\n  - ", unaccounted));
    }

    // The other direction. An entry for a column that no longer exists is a pre-approved hole: rename a future
    // property onto that name and it is exempt before anyone has looked at it. Same both-ways rule
    // PlatformReadShapeTests and ClinicArchiveScopeTests hold their own sets to.
    [Fact]
    public void No_Decision_Names_A_Column_That_Does_Not_Exist() // [FR-3.4]
    {
        using var db = Context();
        var candidates = Candidates(db).ToHashSet(StringComparer.Ordinal);

        var stale = PlaintextByDecision.Keys.Where(name => !candidates.Contains(name)).ToList();

        Assert.True(stale.Count == 0,
            "These decisions name columns the model no longer has. Remove them — an exemption for a column that "
            + "does not exist is a hole waiting for a rename:\n  - " + string.Join("\n  - ", stale));
    }

    // Every reason is a real sentence. A decision map whose entries say "ok" is a list again, and the reason is
    // the only part of an entry that survives contact with the person reading it two years later.
    [Fact]
    public void Every_Decision_Gives_A_Reason() // [FR-3.4]
    {
        Assert.All(PlaintextByDecision, entry =>
            Assert.True(entry.Value.Length >= 40,
                $"« {entry.Key} » is exempted with no real reason: « {entry.Value} »"));
    }

    // The executed red proof, so this class carries its own evidence rather than asking a reviewer to delete a
    // line and watch. It runs the REAL classifier over the REAL model with the Google-token decision removed —
    // which is exactly the shape of « somebody added a credential-shaped column and said nothing ».
    [Fact]
    public void The_Guard_Rejects_A_Credential_Column_Whose_Decision_Is_Removed() // [FR-3.4]
    {
        using var db = Context();

        var withAHole = PlaintextByDecision
            .Where(e => e.Key != $"{nameof(Clinic)}.{nameof(Clinic.GoogleRefreshToken)}")
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        var unaccounted = Unaccounted(Candidates(db), withAHole);

        Assert.Contains($"{nameof(Clinic)}.{nameof(Clinic.GoogleRefreshToken)}", unaccounted);
    }
}
