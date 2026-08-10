using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Enforces the two facts about a caller that the token cannot be trusted for — <b>is this account still
/// active</b>, and <b>what role does it actually hold</b> — on every deployment profile and every account type.
///
/// <para>
/// ⚠️ <b>This exists because offboarding did not work on <c>CloudBrowser</c>.</b> The only per-request reader of
/// live account state was <see cref="LocalAuthEnforcementMiddleware"/>, which is registered only where
/// <c>EnforcesTokenState</c> holds and which skips non-local accounts anyway. So on the Auth0 profile
/// <c>PUT /api/users/{id}/status</c> succeeded, bumped <c>TokenVersion</c>, showed « Désactivé » in the UI — and
/// the ex-employee's token kept working, while a fresh Auth0 login minted more. Nothing in any log said so, which
/// is what made it dangerous: the admin had every reason to believe access was revoked.
/// </para>
///
/// <para>
/// ⚠️ <b>Deliberately not gated on a deployment capability.</b> « A deactivated account cannot use the API » is
/// not a property of a topology, and the previous arrangement is what happens when it is treated as one.
/// <c>EnforcesTokenState</c> keeps its narrower and accurate meaning — token-version revocation, which only a
/// self-issued JWT can have — and stays with the middleware that implements it.
/// </para>
///
/// <para>
/// ⚠️ <b>Ordering matters: this must run before <c>UseAuthorization</c></b>, because the role it publishes is what
/// the authorization handler reads. That costs one account lookup on requests that authorization then refuses —
/// the deliberate trade for <see cref="TenantScopeMiddleware"/> sitting after it. Everything downstream reuses the
/// cached row through <see cref="RequestAccount"/>, so an allowed request pays exactly what it paid before.
/// </para>
/// </summary>
public class AccountStateMiddleware
{
    private readonly RequestDelegate _next;

    public AccountStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository users)
    {
        // A console request has no `User` row at all, so this would resolve nothing and pass through anyway —
        // but skipping it explicitly is what makes the omission a decision rather than a coincidence, and what
        // keeps PlatformAccountStateMiddleware's ownership of console state unambiguous.
        if (ClinicManagement.API.Startup.ConsolePortGate.IsConsolePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var account = await RequestAccount.ResolveAsync(context, users);

        // No row is not a refusal: a Cloud principal who has not joined a clinic yet legitimately has none, and
        // the onboarding endpoints are reached in exactly that state.
        if (account is not null)
        {
            if (!account.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Ce compte a été désactivé." });
                return;
            }

            // The role as the database holds it — see EffectiveRole for why the claim is not authoritative.
            context.Items[EffectiveRole.HttpContextItemKey] = account.Role;
        }

        await _next(context);
    }
}
