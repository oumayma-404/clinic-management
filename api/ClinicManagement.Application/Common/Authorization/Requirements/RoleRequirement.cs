using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.Application.Common.Authorization.Requirements;

public class RoleRequirement : IAuthorizationRequirement
{
    public string[] AllowedRoles { get; }

    public RoleRequirement(params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}





