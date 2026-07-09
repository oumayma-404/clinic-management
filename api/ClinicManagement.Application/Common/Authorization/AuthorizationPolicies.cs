using ClinicManagement.Application.Common.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.Application.Common.Authorization;

public static class AuthorizationPolicies
{
    public const string DoctorOrSecretary = "DoctorOrSecretary";
    public const string DoctorOnly = "DoctorOnly";
    public const string SecretaryOnly = "SecretaryOnly";
    public const string AdminOnly = "AdminOnly";

    /// <param name="isLocalMode">
    /// When true (Local/offline mode — FR-E3 release gate) a <see cref="AuthorizationOptions.FallbackPolicy"/>
    /// of <c>RequireAuthenticatedUser()</c> is installed so every endpoint lacking an explicit
    /// <c>[AllowAnonymous]</c> fails <em>closed</em> (401) — covering the anonymous-by-omission controllers
    /// and any future controller that forgets <c>[Authorize]</c>. In Cloud mode the fallback stays null
    /// (named policies only) so Cloud behavior is byte-for-byte unchanged.
    /// </param>
    public static void ConfigurePolicies(AuthorizationOptions options, bool isLocalMode = false)
    {
        // Doctors and Secretaries can manage patients and appointments
        options.AddPolicy(DoctorOrSecretary, policy =>
            policy.Requirements.Add(new RoleRequirement("doctor", "secretary")));

        // Only doctors can perform certain medical operations
        options.AddPolicy(DoctorOnly, policy =>
            policy.Requirements.Add(new RoleRequirement("doctor")));

        // Only secretaries can schedule appointments
        options.AddPolicy(SecretaryOnly, policy =>
            policy.Requirements.Add(new RoleRequirement("secretary")));

        // Only admins can manage users and clinic settings
        options.AddPolicy(AdminOnly, policy =>
            policy.Requirements.Add(new RoleRequirement("admin")));

        if (isLocalMode)
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        }
    }
}





