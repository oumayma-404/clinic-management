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
/// <see cref="AllowedOnStandbyRelayAttribute"/> are writable.</para>
///
/// <para><b>While the PC holds the cabinet's saves (D13)</b> the refusal is lifted — the PC is the cabinet's server for
/// the cut — except FR-5's « online only » list (<see cref="OnlineOnlyAttribute"/>), refused with <b>423</b>
/// <c>online_only</c> whatever the method: what the cloud keeps writing during a cut (FR-11) must not be written here
/// too, or the return would bring back two versions of one account.</para>
///
/// <para><b>On the cloud, the other half of the lease (D13, D15):</b> while a cabinet's PC de secours may be holding its
/// saves — armed, and silent past <see cref="ClinicWriteLease.CloudFencesAfter"/> — that cabinet's writes are refused
/// with <b>423</b> <c>relay_silent</c>, and once the PC has said it holds them, with <c>clinic_on_relay</c> and the
/// cabinet's time of the takeover (AC-4.2) — except the doors marked <see cref="AllowedWhileCloudFencedAttribute"/>
/// (FR-11). One indexed read per write, and none for a read or for a caller that is not a cabinet.</para>
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

        if (FenceApplies(context, deployment, tenantScope))
        {
            var relay = await relays.GetCurrentForClinicAsync(tenantScope.ClinicId!.Value, context.RequestAborted);
            var now = DateTime.UtcNow;
            if (ClinicWriteLease.IsCloudFenced(relay, now))
            {
                var (error, code) = RelayRefusals.ForFencedCloud(relay!.PcHoldingSinceUtc, now, relay.IsReturning);
                context.Response.StatusCode = StatusCodes.Status423Locked;
                await context.Response.WriteAsJsonAsync(new { error, code });
                return;
            }
        }

        if (OnlineOnlyApplies(context, deployment, relayLocal))
        {
            context.Response.StatusCode = StatusCodes.Status423Locked;
            await context.Response.WriteAsJsonAsync(new { error = RelayRefusals.OnlineOnly, code = RelayRefusals.OnlineOnlyCode });
            return;
        }

        if (!Applies(context, deployment, relayLocal))
        {
            await _next(context);
            return;
        }

        // A retired PC never accepts a save again: « pendant une coupure d'internet » would promise one.
        var (refusal, refusalCode) = RelayRefusals.ForPcNotHolding(relayLocal.IsRetired, relayLocal.IsHandingBack);
        context.Response.StatusCode = StatusCodes.Status423Locked;
        await context.Response.WriteAsJsonAsync(new { error = refusal, code = refusalCode });
    }

    /// <summary>FR-5 on a PC holding the cabinet's saves: refused whatever the method — an archive download is a GET.</summary>
    private static bool OnlineOnlyApplies(HttpContext context, DeploymentProfile deployment, IRelayLocalStatus relayLocal) =>
        deployment.MirrorsCloudClinic
        && context.Request.Path.StartsWithSegments(ApiPrefix)
        && relayLocal.IsHolding
        && context.GetEndpoint() is { } endpoint
        && endpoint.Metadata.GetMetadata<OnlineOnlyAttribute>() is not null;

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
    private static bool Applies(HttpContext context, DeploymentProfile deployment, IRelayLocalStatus relayLocal) =>
        deployment.MirrorsCloudClinic
        && context.Request.Path.StartsWithSegments(ApiPrefix)
        && !relayLocal.IsHolding
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
