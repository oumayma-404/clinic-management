using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// A PC de secours holding a copy refuses every staff write with <b>423</b> <c>relay_standby</c> (<c>clinic-pc-copy</c>
/// Part 1). The copy is the cloud's: a row saved here would be overwritten by the next change the cloud sends, or never
/// reach the cloud at all, and either way the cabinet would lose work it was told was saved.
///
/// <para>Reads pass by construction, as on <see cref="SubscriptionGateMiddleware"/>; only the sign-in doors marked
/// <see cref="AllowedOnStandbyRelayAttribute"/> are writable. Part 2 lifts the refusal while the PC holds the lease.</para>
///
/// <para><b>On the cloud, the other half of the lease (D13, D15):</b> while a cabinet's PC de secours may be holding its
/// saves — armed, and silent past <see cref="ClinicWriteLease.CloudFencesAfter"/> — that cabinet's writes are refused
/// with <b>423</b> <c>relay_silent</c>, except the doors marked <see cref="AllowedWhileCloudFencedAttribute"/> (FR-11).
/// One indexed read per write, and none for a read or for a caller that is not a cabinet.</para>
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

    public async Task InvokeAsync(
        HttpContext context,
        DeploymentProfile deployment,
        IRelayLocalStatus relayLocal,
        ITenantScope tenantScope,
        IClinicRelayRepository relays)
    {
        // AC-8.1: a retired PC opens for administrators only. A colleague still signed in when the cloud retired it is
        // signed out (401) — reads included — and the sign-in form then gives the reason.
        if (deployment.MirrorsCloudClinic
            && context.Request.Path.StartsWithSegments(ApiPrefix)
            && relayLocal.IsRetired
            && IsSignedInNonAdmin(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ClinicAuthRefusals.MessageFor(ClinicAuthRefusals.RetiredRelayAdminsOnly),
                code = ClinicAuthRefusals.RetiredRelayAdminsOnly,
            });
            return;
        }

        if (FenceApplies(context, deployment, tenantScope)
            && ClinicWriteLease.IsCloudFenced(
                await relays.GetCurrentForClinicAsync(tenantScope.ClinicId!.Value, context.RequestAborted), DateTime.UtcNow))
        {
            context.Response.StatusCode = StatusCodes.Status423Locked;
            await context.Response.WriteAsJsonAsync(new { error = RelayRefusals.Silent, code = RelayRefusals.SilentCode });
            return;
        }

        if (!Applies(context, deployment))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status423Locked;
        await context.Response.WriteAsJsonAsync(new { error = RelayRefusals.Standby, code = RelayRefusals.StandbyCode });
    }

    /// <summary>
    /// A cabinet's write, on a cloud that publishes a change feed, through a door that is not FR-11's. ⚠️ A caller that
    /// is not a cabinet (the PC's own token, the vendor's console, an anonymous door) passes: it has no PC to wait for.
    /// </summary>
    private static bool FenceApplies(HttpContext context, DeploymentProfile deployment, ITenantScope tenantScope) =>
        deployment.PublishesChangeFeed
        && context.Request.Path.StartsWithSegments(ApiPrefix)
        && !IsRead(context.Request.Method)
        && tenantScope.Kind == TenantScopeKind.Clinic
        && tenantScope.ClinicId is not null
        && context.GetEndpoint() is { } endpoint
        && endpoint.Metadata.GetMetadata<AllowedWhileCloudFencedAttribute>() is null;

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

    /// <summary>The account's role as <c>AccountStateMiddleware</c> read it from the database, never the token's claim.</summary>
    private static bool IsSignedInNonAdmin(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
        && context.Items.TryGetValue(EffectiveRole.HttpContextItemKey, out var role)
        && role is string r
        && !string.Equals(r, Domain.Entities.User.RoleAdmin, StringComparison.OrdinalIgnoreCase);

    private static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
