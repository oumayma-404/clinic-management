using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>No path that issues a session reaches an administrator without a verified second factor</b>, where the
/// deployment requires one (<c>hosted-security-hardening</c> FR-1.1).
///
/// <para><b>Why a derived guard and not a behavioural test.</b> <c>ClinicTotpAuthTests</c> proves the ladder
/// refuses today. What it cannot prove is that the ladder is the <i>only</i> way in: the defect this catches is
/// a <b>new</b> session-issuing path — a magic-link sign-in, an SSO exchange, an impersonation verb — written
/// next year by somebody who never read the ladder. Every one of those is a `fixes-dont-propagate` shape, and
/// the failure is silent: the new door works, so nothing looks wrong.</para>
///
/// <para>The candidate set is therefore derived from <b>what issues a durable credential</b>
/// (<c>GenerateRefreshToken</c>), not from a list of files somebody remembered to write down.</para>
/// </summary>
public class SecondFactorCoverageTests
{
    /// <summary>
    /// Files that mint a refresh credential without consulting the requirement, each with the reason.
    ///
    /// <para>⚠️ Asserted equal in <b>both</b> directions, so a stale entry fails too — an exemption that no
    /// longer names a real minting path is a pre-approved hole standing open for whatever is written next.</para>
    /// </summary>
    private static readonly Dictionary<string, string> MintsWithoutCheckingByDesign = new(StringComparer.Ordinal)
    {
        ["RefreshTokenCommand.cs"] =
            "Renews an EXISTING session rather than opening one. The factor was presented when that session was "
            + "established, and re-demanding it on a silent 30-minute renewal would prompt a dentist for a code "
            + "several times a day. What this path does enforce instead is the per-request requirement in "
            + "LocalAuthEnforcementMiddleware — so a session predating the rule still cannot outlive it — plus "
            + "TokenVersion, which a reset or a promotion bumps.",

        ["RedeemRecoveryCodeCommand.cs"] =
            "The recovery code IS the second factor for this sign-in (FR-1.4). It is single-use, spent even when "
            + "the sign-in then fails, and only reachable by a caller who also proved the password — so demanding "
            + "an authenticator code as well would make the one way back the user can take alone unusable, which "
            + "is exactly what AC-7 forbids.",
    };

    /// <summary>The call that means « this path is handing out a durable session ».</summary>
    private static readonly Regex MintsACredential = new(
        @"GenerateRefreshToken\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Consulting the requirement. Either asking the capability, or reading the account's own enrolment — a path
    /// doing neither cannot be refusing an unenrolled administrator.
    /// </summary>
    private static readonly Regex ConsultsTheRequirement = new(
        @"RequiresAdminSecondFactor|IsTotpEnrolled|SecondFactorApplies", RegexOptions.Compiled);

    // [FR-1.1] The guarantee.
    [Fact]
    public void Every_Session_Issuing_Path_Consults_The_Second_Factor_Requirement()
    {
        var minting = MintingFiles();

        // "Found nothing" must not read as "nothing was wrong": a renamed method or a broken scan would
        // otherwise report this contract as satisfied while checking no paths at all.
        Assert.True(
            minting.Count >= 3,
            $"Only {minting.Count} session-issuing path(s) found — the scan is broken, so this guard is checking "
            + "nothing. Fix it rather than trusting the green.");

        var unchecked_ = minting
            .Where(f => !ConsultsTheRequirement.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .Select(n => n!)
            .Where(name => !MintsWithoutCheckingByDesign.ContainsKey(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unchecked_.Count == 0,
            "These path(s) issue a session without consulting the second-factor requirement: "
            + string.Join(", ", unchecked_)
            + ". An administrator on a deployment that requires one could sign in through them with a password "
            + "alone. Either consult ISecondFactorPolicy/IsTotpEnrolled, or add the file below with its reason.");
    }

    // [FR-1.1] The other direction — a stale exemption is a hole waiting for the next path written near it.
    [Fact]
    public void Every_Exemption_Still_Names_A_Path_That_Mints_A_Credential()
    {
        var mintingNames = MintingFiles()
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        var stale = MintsWithoutCheckingByDesign.Keys
            .Where(name => !mintingNames.Contains(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These exemption(s) no longer name a path that issues a session: " + string.Join(", ", stale)
            + ". Remove them.");
    }

    // [FR-1.1] The red proof, executed. A guard nobody has seen fail is a guard nobody knows works — and this
    // one is a source scan, whose failure mode is silence rather than noise.
    [Fact]
    public void The_Guard_Rejects_A_Minting_Path_That_Ignores_The_Requirement()
    {
        const string ignoresIt = """
            public class MagicLinkSignInCommandHandler
            {
                public void Handle(User user)
                {
                    var refreshToken = _localAuthService.GenerateRefreshToken(user, null);
                }
            }
            """;

        Assert.Matches(MintsACredential, ignoresIt);
        Assert.DoesNotMatch(ConsultsTheRequirement, ignoresIt);

        // And a path that DOES consult it passes, so the check is discriminating rather than rejecting
        // anything that mints a credential.
        const string consultsIt = """
            if (user.IsTotpEnrolled) { /* … */ }
            var refreshToken = _localAuthService.GenerateRefreshToken(user, family.Id);
            """;

        Assert.Matches(MintsACredential, consultsIt);
        Assert.Matches(ConsultsTheRequirement, consultsIt);
    }

    /// <summary>
    /// Every production source that mints a refresh credential.
    ///
    /// <para>Interfaces and the issuer itself are excluded: <c>ILocalAuthService</c> declares the method and
    /// <c>LocalAuthService</c> implements it — neither <i>decides</i> whether a session may be issued, which is
    /// what this guard is about. Test sources are excluded for the obvious reason.</para>
    /// </summary>
    private static IReadOnlyList<string> MintingFiles()
    {
        var root = SolutionSources.Root();

        return SolutionSources.CsFiles(root)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.UnitTests{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(f => Path.GetFileName(f) is not ("ILocalAuthService.cs" or "LocalAuthService.cs"))
            .Where(f => MintsACredential.IsMatch(File.ReadAllText(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}
