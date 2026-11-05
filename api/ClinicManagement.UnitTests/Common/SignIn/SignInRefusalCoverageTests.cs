using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using MediatR;
using Xunit;

namespace ClinicManagement.UnitTests.Common.SignIn;

/// <summary>
/// sign-in-alerts: every request that checks a password before its caller holds a session carries
/// <see cref="ICredentialAttempt"/>, so its refusals are written down and can reach the vendor. Derived from the
/// sources rather than listed — a seventh door is covered the day it is written, and one that forgets fails here.
/// </summary>
public class SignInRefusalCoverageTests
{
    // Re-checks made inside a session: a wrong current password there is not somebody trying to get in.
    private static readonly Dictionary<string, string> AuthenticatedRechecks = new(StringComparer.Ordinal)
    {
        ["ChangePasswordCommand"] = "the caller is signed in and confirms their current password",
        ["StepUpCommand"] = "the caller is signed in and confirms a sensitive action",
        ["ChangePlatformPasswordCommand"] = "the console account is signed in and changes its own password",
    };

    private static readonly Regex RequestDeclaration = new(
        @"\b(?:class|record)\s+(\w+)\b[^{;]*?\bIRequest<", RegexOptions.Compiled);

    [Fact]
    public void Every_Request_That_Checks_A_Password_With_No_Session_Records_Its_Refusals()
    {
        var checkers = PasswordCheckingRequests().ToList();

        // Six doors and three re-checks today: a scan that finds fewer is reading nothing, not finding nothing wrong.
        Assert.True(checkers.Count >= 9, $"The scan found {checkers.Count} requests checking a password.");
        Assert.Empty(Unrecorded(checkers));
    }

    [Fact]
    public void Every_Reviewed_Recheck_Still_Exists_And_Still_Checks_A_Password()
    {
        var checkers = PasswordCheckingRequests().ToHashSet();

        Assert.All(AuthenticatedRechecks.Keys, name => Assert.Contains(name, checkers));
    }

    // The other direction: a marker on a request that checks nothing would be a door that does not exist.
    [Fact]
    public void Every_Credential_Door_Checks_A_Password()
    {
        var checkers = PasswordCheckingRequests().ToHashSet();
        var doors = typeof(ICredentialAttempt).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICredentialAttempt).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.Equal(6, doors.Count);
        Assert.All(doors, name => Assert.Contains(name, checkers));
    }

    [Fact]
    public void The_Guard_Flags_A_New_Door_That_Forgets()
    {
        const string probe =
            "public class ProbeLoginCommand : IRequest<Result<LoginResultDto>>\n{\n}\n"
            + "public class ProbeLoginCommandHandler : IRequestHandler<ProbeLoginCommand, Result<LoginResultDto>>\n"
            + "{\n    void Check() => _auth.VerifyPassword(hash, password);\n}\n";

        var declared = RequestsDeclaredIn(probe).ToList();

        Assert.Equal(new[] { "ProbeLoginCommand" }, declared);
        Assert.Equal(declared, Unrecorded(declared));
    }

    private static IEnumerable<string> Unrecorded(IEnumerable<string> requestNames) =>
        requestNames.Where(name => !AuthenticatedRechecks.ContainsKey(name) && !IsCredentialDoor(name));

    // A name the assembly does not know is flagged too: the guard must fail rather than pass on what it cannot check.
    private static bool IsCredentialDoor(string requestName) =>
        typeof(ICredentialAttempt).Assembly.GetTypes()
            .Where(t => t.Name == requestName && typeof(IBaseRequest).IsAssignableFrom(t))
            .Any(t => typeof(ICredentialAttempt).IsAssignableFrom(t));

    private static IEnumerable<string> PasswordCheckingRequests()
    {
        var features = new DirectoryInfo(
            Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.Application", "Features"));

        return SolutionSources.CsFiles(features)
            .Select(File.ReadAllText)
            .Where(source => source.Contains("VerifyPassword(", StringComparison.Ordinal))
            .SelectMany(RequestsDeclaredIn)
            .Distinct();
    }

    private static IEnumerable<string> RequestsDeclaredIn(string source) =>
        RequestDeclaration.Matches(source).Select(m => m.Groups[1].Value);
}
