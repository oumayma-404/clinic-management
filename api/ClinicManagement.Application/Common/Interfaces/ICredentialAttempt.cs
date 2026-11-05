using ClinicManagement.Application.Common.SignIn;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// A request that checks somebody's credentials before they hold a session. Its refusals are written down and may
/// alert the vendor (<c>SignInRefusalBehavior</c>); <c>SignInRefusalCoverageTests</c> fails on a request that checks a
/// password anonymously without carrying this.
/// </summary>
public interface ICredentialAttempt
{
    /// <summary>The address as typed — only ever written down through <see cref="SignInRefusalRules.ShownEmail"/>.</summary>
    string? Email { get; }

    SignInDoor Door { get; }
}
