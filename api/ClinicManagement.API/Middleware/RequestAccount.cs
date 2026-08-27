using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// The authenticated caller's own <c>User</c> row, resolved <b>once</b> per request.
///
/// <para>Two middlewares need it — <see cref="TenantScopeMiddleware"/> for the clinic the query filter scopes
/// to, and <see cref="LocalAuthEnforcementMiddleware"/> for the token version, the active flag and the pending
/// forced password change — and each issuing its own query would double the per-request account lookup on every
/// authenticated call. Caching on <see cref="HttpContext.Items"/> also means neither has to assume it runs
/// first.</para>
/// </summary>
internal static class RequestAccount
{
    private const string ItemKey = "clinic-management.request-account";

    /// <summary>
    /// The caller's account, or null when the request is anonymous, carries no subject claim, or the subject
    /// has no <c>User</c> row yet — which is the ordinary state of a Cloud principal who has not joined a
    /// clinic, and must stay a non-error.
    /// </summary>
    public static async Task<User?> ResolveAsync(HttpContext context, IUserRepository users)
    {
        if (context.Items.TryGetValue(ItemKey, out var cached))
        {
            return cached as User;
        }

        User? account = null;

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!string.IsNullOrEmpty(subject))
            {
                account = await users.GetByAuth0SubAsync(subject, context.RequestAborted);
            }
        }

        // Cached even when null: "this request has no account" is an answer worth not asking twice.
        context.Items[ItemKey] = account;
        return account;
    }
}
