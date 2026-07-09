using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Authorization;

/// <summary>
/// The Phase 4 release gate (FR-E3). In Local mode <see cref="AuthorizationPolicies.ConfigurePolicies"/>
/// must install a fail-closed <see cref="AuthorizationOptions.FallbackPolicy"/> requiring an authenticated
/// user; in Cloud mode the fallback must stay null so Cloud behavior is byte-for-byte unchanged.
/// </summary>
public class AuthorizationPoliciesTests
{
    private static AuthorizationOptions Configure(bool isLocalMode)
    {
        var options = new AuthorizationOptions();
        AuthorizationPolicies.ConfigurePolicies(options, isLocalMode);
        return options;
    }

    [Fact]
    public void Local_mode_installs_fallback_policy_requiring_authenticated_user() // [FR-E3]
    {
        var options = Configure(isLocalMode: true);

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy!.Requirements,
            r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void Cloud_mode_leaves_fallback_policy_null() // [FR-E3 / US-7 Cloud parity]
    {
        var options = Configure(isLocalMode: false);

        Assert.Null(options.FallbackPolicy);
    }

    [Fact]
    public void Named_role_policies_are_registered_in_both_modes()
    {
        foreach (var isLocal in new[] { true, false })
        {
            var options = Configure(isLocal);
            Assert.NotNull(options.GetPolicy(AuthorizationPolicies.AdminOnly));
            Assert.NotNull(options.GetPolicy(AuthorizationPolicies.DoctorOrSecretary));
            Assert.NotNull(options.GetPolicy(AuthorizationPolicies.DoctorOnly));
            Assert.NotNull(options.GetPolicy(AuthorizationPolicies.SecretaryOnly));
        }
    }
}
