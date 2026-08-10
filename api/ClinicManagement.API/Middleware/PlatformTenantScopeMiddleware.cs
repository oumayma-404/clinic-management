using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Declares <c>UseSystemWide("platform console")</c> for console requests, so the EF global query filters let a
/// cross-cabinet read through (<c>platform-console</c> EC-12, risk R-4). See <see cref="PlatformTenantScope"/>
/// for why an <c>Unset</c> scope here would be zero rows and no error.
///
/// <para>⚠️ <b>It must run before <see cref="TenantScopeMiddleware"/>, and that one must skip console
/// requests.</b> <c>ITenantScope</c> is single-assignment in both directions, so a clinic scope set first would
/// make this throw — and today it would not even get that far: a console principal has no <c>User</c> row, so
/// <c>TenantScopeMiddleware</c> silently sets nothing and leaves the scope <c>Unset</c>. Both orderings are
/// pinned by <c>PlatformAccountStateTests</c>' source-level guard.</para>
///
/// <para>⚠️ It declares on <b>every</b> console request, including the anonymous auth ones. Those read
/// <see cref="Domain.Entities.PlatformAccount"/>, which carries no <c>ClinicId</c> and is therefore unfiltered —
/// so the declaration buys them nothing and costs nothing, and making it conditional on « is this authenticated »
/// would add a branch whose only failure mode is the silent one this exists to prevent.</para>
/// </summary>
public class PlatformTenantScopeMiddleware
{
    private readonly RequestDelegate _next;

    public PlatformTenantScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantScope scope)
    {
        if (ConsolePortGate.IsConsolePath(context.Request.Path))
        {
            PlatformTenantScope.Declare(scope);
        }

        await _next(context);
    }
}
