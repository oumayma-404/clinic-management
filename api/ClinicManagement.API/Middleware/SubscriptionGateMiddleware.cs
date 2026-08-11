using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// A cabinet past its entitlement becomes <b>read-only</b>: every read, every CSV export and every PDF keep working
/// exactly as before, and only writes are refused — with <b>402</b>, a machine-readable code and a French sentence
/// naming the date (US-4, FR-3).
///
/// <para><b>Reads are untouched by construction, not by a list.</b> The gate never inspects a GET, so AC-4.1 holds
/// for every read that exists and every read added later; an allow-list of readable endpoints would have to be kept
/// complete, and the day it was not, an expired cabinet would lose part of its own records.</para>
///
/// <para><b>⚠️ Registered after <c>LocalAuthEnforcementMiddleware</c> — last before <c>MapControllers</c> — and the
/// four lines matter.</b> That middleware enforces the two blocking preconditions of a self-issued JWT:
/// token-version revocation (<b>401</b>) and a pending forced password change (<b>403
/// <c>must_change_password</c></b>). Placed before it, this gate would answer <b>402</b> for both on an expired
/// cabinet — a deactivated colleague would be told the subscription had lapsed, and a user who must change their
/// password would be routed to « Abonnement » instead of to <c>/change-password</c>, stuck in both directions.
/// Placed after, it still has everything it needs: the tenant scope is set, the account is cached
/// (<see cref="RequestAccount"/>), and endpoint metadata is available because the implicit <c>UseRouting</c> runs
/// before <i>all</i> user middleware — the same reason <c>UseAuthorization</c> works here with no explicit call.</para>
///
/// <para><b>One indexed row per write.</b> The entitlement's <c>EndsOn</c> is a denormalised re-fold of the ledger
/// precisely so this path never folds anything.</para>
/// </summary>
public class SubscriptionGateMiddleware
{
    private const string ApiPrefix = "/api";

    private readonly RequestDelegate _next;

    public SubscriptionGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ISubscriptionPolicy policy,
        ITenantScope tenantScope,
        IClinicSubscriptionRepository subscriptions)
    {
        if (!Applies(context, policy))
        {
            await _next(context);
            return;
        }

        // ⚠️ A caller who is not a cabinet PASSES (FR-3). They have no entitlement to find, and refusing them under
        // subscription_missing would land that fault code on exactly the future vendor-console endpoints whose
        // purpose is to end a refusal. The anonymous case is already covered by authentication.
        if (tenantScope.Kind != TenantScopeKind.Clinic || tenantScope.ClinicId is not { } clinicId)
        {
            await _next(context);
            return;
        }

        var subscription = await subscriptions.GetByClinicAsync(clinicId, context.RequestAborted);

        if (subscription is null)
        {
            await RefuseAsync(context, SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
            return;
        }

        var status = SubscriptionStateReader.Read(subscription, ClinicClock.ClinicToday());

        if (status.AllowsWrites)
        {
            await _next(context);
            return;
        }

        // Which refusal this is comes from the one classifier the outbox gate also reads; only the wording below is
        // this surface's own.
        var (error, code) = SubscriptionStateReader.ClassifyRefusal(status) switch
        {
            SubscriptionRefusalKind.Suspended =>
                (SubscriptionRefusals.Suspended, SubscriptionRefusals.SuspendedCode),
            SubscriptionRefusalKind.Expired =>
                (SubscriptionRefusals.Required(status.EndsOn!.Value), SubscriptionRefusals.RequiredCode),
            _ => (SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode),
        };

        await RefuseAsync(context, error, code);
    }

    /// <summary>
    /// The cheap predicates, in order: is enforcement on at all (FR-11), is this even an API route (the front door
    /// also serves the web app), is it a read, and has the endpoint declared itself exempt (FR-3).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>No endpoint matched is not the same as « this endpoint declared no exemption »</b>, and conflating them
    /// answered <b>402</b> to a mistyped URL or to an old client calling a removed route. Naming the subscription is
    /// the loudest thing this gate can say — it fires <c>onSubscriptionRequired</c> on the client — so an unroutable
    /// path must fall through to routing's own 404.
    /// </remarks>
    private static bool Applies(HttpContext context, ISubscriptionPolicy policy) =>
        policy.RequiresSubscription
        && context.Request.Path.StartsWithSegments(ApiPrefix)
        && !IsRead(context.Request.Method)
        && context.GetEndpoint() is { } endpoint
        && endpoint.Metadata.GetMetadata<AllowsWithoutSubscriptionAttribute>() is null;

    private static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static Task RefuseAsync(HttpContext context, string error, string code)
    {
        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        return context.Response.WriteAsJsonAsync(new { error, code });
    }
}
