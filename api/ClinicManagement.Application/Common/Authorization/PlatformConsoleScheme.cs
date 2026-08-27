namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// The name of the console's own JWT bearer authentication scheme (<c>platform-console</c> AC-1.4).
///
/// <para><b>Why it is not a constant on <see cref="AuthorizationPolicies"/>.</b>
/// <c>ControllerAuthorizationCoverageTests</c> derives the product's policy vocabulary by reflecting over
/// <i>every public string constant</i> on that class, so a scheme name parked there would be read as a fifth
/// policy — one applied nowhere and registered by nothing, failing two of that guard's assertions at once. The
/// guard is right and the constant is in the wrong place; this file is the right place.</para>
///
/// <para>It lives in Application because <see cref="AuthorizationPolicies.ConfigurePolicies"/> pins the scheme on
/// the console policy, and that method is here. The scheme is <i>registered</i> in <c>Program.cs</c>, where the
/// signing key and the host builder are.</para>
/// </summary>
public static class PlatformConsoleScheme
{
    public const string Name = "PlatformConsole";
}
