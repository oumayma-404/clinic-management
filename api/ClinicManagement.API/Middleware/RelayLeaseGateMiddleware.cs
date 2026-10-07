using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// A PC de secours holding a copy refuses every staff write with <b>423</b> <c>relay_standby</c> (<c>clinic-pc-copy</c>
/// Part 1). The copy is the cloud's: a row saved here would be overwritten by the next change the cloud sends, or never
/// reach the cloud at all, and either way the cabinet would lose work it was told was saved.
///
/// <para>Reads pass by construction, as on <see cref="SubscriptionGateMiddleware"/>; only the sign-in doors marked
/// <see cref="AllowedOnStandbyRelayAttribute"/> are writable. Part 2 lifts the refusal while the PC holds the lease and
/// adds the cloud's own fence (<c>clinic_on_relay</c>).</para>
///
/// <para>⚠️ <b>Registered after <c>LocalAuthEnforcementMiddleware</c> and before the subscription gate.</b> After, so a
/// revoked token still answers 401 and a pending password change 403; before, so a copy of an expired cabinet says
/// « this is a copy » rather than « pay » — the sentence that names the real reason (AC-4.3).</para>
/// </summary>
public class RelayLeaseGateMiddleware
{
    private const string ApiPrefix = "/api";

    private readonly RequestDelegate _next;

    public RelayLeaseGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DeploymentProfile deployment)
    {
        if (!Applies(context, deployment))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status423Locked;
        await context.Response.WriteAsJsonAsync(new { error = RelayRefusals.Standby, code = RelayRefusals.StandbyCode });
    }

    /// <remarks>
    /// ⚠️ No endpoint matched passes, for <see cref="SubscriptionGateMiddleware"/>'s reason: a mistyped URL must reach
    /// routing's own 404, not be told the PC is a copy.
    /// </remarks>
    private static bool Applies(HttpContext context, DeploymentProfile deployment) =>
        deployment.MirrorsCloudClinic
        && context.Request.Path.StartsWithSegments(ApiPrefix)
        && !IsRead(context.Request.Method)
        && context.GetEndpoint() is { } endpoint
        && endpoint.Metadata.GetMetadata<AllowedOnStandbyRelayAttribute>() is null;

    private static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
