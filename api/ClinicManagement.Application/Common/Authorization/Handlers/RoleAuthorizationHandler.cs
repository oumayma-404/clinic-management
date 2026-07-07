using System.Security.Claims;
using ClinicManagement.Application.Common.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.Application.Common.Authorization.Handlers;

public class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        // Try multiple claim types/names for role (in order of likelihood)
        var userRole = context.User.FindFirst("https://clinic-management.com/role")?.Value
            ?? context.User.FindFirst("role")?.Value
            ?? context.User.FindFirst(ClaimTypes.Role)?.Value;

        if (userRole != null && requirement.AllowedRoles.Contains(userRole, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}



