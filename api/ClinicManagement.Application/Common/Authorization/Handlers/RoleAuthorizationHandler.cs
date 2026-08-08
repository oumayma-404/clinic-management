using System.Security.Claims;
using ClinicManagement.Application.Common.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Application.Common.Authorization.Handlers;

/// <summary>
/// Grants a <see cref="RoleRequirement"/> from the caller's <b>effective</b> role.
///
/// <para>
/// ⚠️ <b>The database wins over the token.</b> The request pipeline publishes the role from the caller's own
/// <c>User</c> row (see <see cref="EffectiveRole"/>); the JWT claim is consulted only when no row exists. That
/// ordering is the fix for a real defect: in <c>CloudBrowser</c> the claim comes from Auth0 <c>app_metadata</c>
/// written by an Action outside this repository, and demoting a user updates the row without updating Auth0 — so
/// a demoted admin passed <c>AdminOnly</c> for ever, on new tokens as well as old.
/// </para>
///
/// <para>
/// ⚠️ <b>The claim fall-back is required, not lenient.</b> Cloud onboarding — <c>POST /clinics</c>,
/// <c>POST /clinics/join</c>, <c>user-status</c> — is reached by a principal who has no <c>User</c> row yet, and
/// is precisely why the role-less <c>Authenticated</c> policy exists. Those endpoints require no role, so the
/// fall-back grants nothing on its own; removing it would break signing up without closing anything.
/// </para>
/// </summary>
public class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RoleAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        var userRole = EffectiveRoleFromRequest() ?? RoleFromClaims(context.User);

        if (userRole != null && requirement.AllowedRoles.Contains(userRole, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>The DB-resolved role, published by <c>AccountStateMiddleware</c>. Null when the caller has no row.</summary>
    private string? EffectiveRoleFromRequest() =>
        _httpContextAccessor.HttpContext?.Items.TryGetValue(EffectiveRole.HttpContextItemKey, out var role) == true
            ? role as string
            : null;

    // Several claim types/names, in order of likelihood — Auth0's namespaced claim first.
    private static string? RoleFromClaims(ClaimsPrincipal principal) =>
        principal.FindFirst("https://clinic-management.com/role")?.Value
        ?? principal.FindFirst("role")?.Value
        ?? principal.FindFirst(ClaimTypes.Role)?.Value;
}
