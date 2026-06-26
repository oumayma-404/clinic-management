using ClinicManagement.Application.Common.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.Application.Common.Authorization;

public static class AuthorizationPolicies
{
    public const string DoctorOrSecretary = "DoctorOrSecretary";
    public const string DoctorOnly = "DoctorOnly";
    public const string SecretaryOnly = "SecretaryOnly";
    public const string AdminOnly = "AdminOnly";

    public static void ConfigurePolicies(AuthorizationOptions options)
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
    }
}





