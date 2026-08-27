using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Authorization.Requirements;
using ClinicManagement.Domain.Entities;
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

    // ⚠️ The « are the policies registered? » assertion that used to live here is GONE, deliberately (I2).
    //
    // It named five policies and asserted each was non-null — and stayed green for the entire life of the product
    // while three of those five (`DoctorOnly`, `SecretaryOnly`, `DoctorOrSecretary`) were applied to **nothing**
    // and 33 endpoints ran on a bare `[Authorize]`. Asserting a policy exists cannot distinguish a policy that is
    // enforced everywhere from one that is enforced nowhere, which made it worse than no test: it read as
    // coverage. Its replacement is derived and lives with the surface it describes —
    // `Api/ControllerAuthorizationCoverageTests`: every action resolves to a named policy, the defined and
    // applied sets are equal in **both** directions, and every defined policy is registered in both modes.
    //
    // What stays here is what is genuinely about this file: the mode-branched fallback, and the role *contents*
    // of the two policies that carry the feature's central distinction.

    // [I1] The distinction the whole feature turns on. `AnyClinicRole` must admit the admin — see the constant's
    // own remarks: `CreateClinicCommand` makes a clinic's creator an admin and links the single dentist's Doctor
    // record to that same account, so a literal {doctor, secretary} policy on the agenda or the till would lock
    // the owner out of their own practice. There is no implicit admin in RoleAuthorizationHandler.
    [Fact]
    public void AnyClinicRole_admits_all_three_roles_including_admin()
    {
        var options = Configure(isLocalMode: true);
        var policy = options.GetPolicy(AuthorizationPolicies.AnyClinicRole);

        Assert.NotNull(policy);
        var roleRequirement = Assert.Single(policy!.Requirements.OfType<RoleRequirement>());
        Assert.Contains(User.RoleAdmin, roleRequirement.AllowedRoles);
        Assert.Contains(User.RoleDoctor, roleRequirement.AllowedRoles);
        Assert.Contains(User.RoleSecretary, roleRequirement.AllowedRoles);
    }

    // [I1] `Authenticated` is authenticated-but-role-less on purpose (Cloud writes the role into app_metadata
    // only after the clinic is joined, so onboarding is reached by a principal that has none). Pinning the
    // ABSENCE of a role requirement is the point: adding one here would break Cloud onboarding outright, and it
    // would look like a tightening.
    [Fact]
    public void Authenticated_policy_requires_a_user_but_no_role()
    {
        var options = Configure(isLocalMode: true);
        var policy = options.GetPolicy(AuthorizationPolicies.Authenticated);

        Assert.NotNull(policy);
        Assert.Empty(policy!.Requirements.OfType<RoleRequirement>());
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    // [I1] AdminOnly is exactly one role — the destructive operations and user management.
    [Fact]
    public void AdminOnly_policy_admits_only_admin()
    {
        var options = Configure(isLocalMode: true);
        var policy = options.GetPolicy(AuthorizationPolicies.AdminOnly);

        Assert.NotNull(policy);
        var roleRequirement = Assert.Single(policy!.Requirements.OfType<RoleRequirement>());
        Assert.Equal(new[] { User.RoleAdmin }, roleRequirement.AllowedRoles);
    }

    // [DEV-2] The three policies that had zero usages are gone, not parked. Asserted by name because the
    // both-directions guard can only compare what exists — it cannot know these three ever did, and a
    // reintroduced-but-unapplied policy would fail there with a less obvious message than this one.
    [Theory]
    [InlineData("DoctorOnly")]
    [InlineData("SecretaryOnly")]
    [InlineData("DoctorOrSecretary")]
    public void Retired_policies_are_not_reintroduced(string retired)
    {
        var stillDefined = typeof(AuthorizationPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Contains(retired, StringComparer.Ordinal);

        Assert.False(stillDefined,
            $"'{retired}' is back. It had zero usages for the life of the product; if it is genuinely needed "
            + "now, apply it — the both-directions guard will otherwise fail on it.");
    }

    // [AC-6] Cancelling an invoice is limited to admin/doctor — the AdminOrDoctor policy allows exactly those roles.
    [Fact]
    public void AdminOrDoctor_policy_allows_admin_and_doctor_roles()
    {
        var options = Configure(isLocalMode: true);
        var policy = options.GetPolicy(AuthorizationPolicies.AdminOrDoctor);

        Assert.NotNull(policy);
        var roleRequirement = Assert.Single(policy!.Requirements.OfType<RoleRequirement>());
        Assert.Contains("admin", roleRequirement.AllowedRoles);
        Assert.Contains("doctor", roleRequirement.AllowedRoles);
        Assert.DoesNotContain("secretary", roleRequirement.AllowedRoles);
    }
}
