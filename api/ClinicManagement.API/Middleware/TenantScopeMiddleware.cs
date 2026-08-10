using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Sets the request's <see cref="ITenantScope"/> from the caller's <b>DB-resolved</b> <c>User.ClinicId</c>, so
/// the EF Core global query filter knows whose rows this request may see.
///
/// <para><b>From the database, never from the JWT claim (amendment C3′).</b> The claim was good enough while
/// the filter was fail-open — a missing or stale <c>clinic_id</c> just switched the backstop off and the
/// per-handler check still returned the right rows. Now that the filter refuses, the same divergence would be
/// <b>zero rows and no error</b>: in Cloud the claim is the namespaced
/// <c>https://clinic-management.com/clinic_id</c>, written by an Auth0 tenant Action that does not live in this
/// repository, and any token minted before a user's clinic changed diverges until it is refreshed.</para>
///
/// <para><b>An unresolvable caller leaves the scope Unset, deliberately.</b> Anonymous requests, the proxied
/// web pages, and a principal with no <c>User</c> row yet (Cloud onboarding, before <c>POST /clinics</c> or
/// <c>/clinics/join</c>) all land there — and all of them work, because <c>User</c> and <c>Clinic</c> carry no
/// query filter. Refusing the request instead would break onboarding outright.</para>
///
/// <para>⚠️ HTTP middleware does not run per SignalR hub invocation, so a hub method lands in
/// <c>Unset</c> — see the note on <c>ClinicHub</c>.</para>
/// </summary>
public class TenantScopeMiddleware
{
    private readonly RequestDelegate _next;

    public TenantScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantScope scope, IUserRepository users)
    {
        // A console request declared UseSystemWide upstream (PlatformTenantScopeMiddleware), and ITenantScope is
        // single-assignment — narrowing it to a clinic here would throw. It has no `User` row to narrow to either.
        if (ClinicManagement.API.Startup.ConsolePortGate.IsConsolePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var account = await RequestAccount.ResolveAsync(context, users);

        if (account is not null)
        {
            scope.UseClinic(account.ClinicId);
        }

        await _next(context);
    }
}
