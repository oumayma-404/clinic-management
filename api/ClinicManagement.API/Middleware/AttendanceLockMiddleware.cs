using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// « Pointez votre arrivée » on the server (<c>pointage-obligatoire</c> AC-3): while <see cref="AttendanceLockRules.IsLocked"/>
/// holds for the caller, every write is refused with <b>423 <c>clock_in_required</c></b>. Reads pass by construction,
/// and the writes that must stay open carry <see cref="AllowsBeforeClockInAttribute"/>.
///
/// <para>Registered after <see cref="SubscriptionGateMiddleware"/>: a lapsed cabinet says « pay » first, and the rule
/// itself lifts the lock there (the borne would refuse the punch too). Only a tracked account can be locked, so an
/// admin's or a dentist's save costs no query.</para>
/// </summary>
public class AttendanceLockMiddleware
{
    private readonly RequestDelegate _next;

    public AttendanceLockMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(
        HttpContext context, ITenantScope tenantScope, IUserRepository users, AttendanceLockEvaluator evaluator) =>
        InvokeAtAsync(context, tenantScope, users, evaluator, DateTime.UtcNow);

    /// <summary>The same gate judged at <paramref name="nowUtc"/> — tests pin the instant against the opening hours.</summary>
    public async Task InvokeAtAsync(
        HttpContext context, ITenantScope tenantScope, IUserRepository users, AttendanceLockEvaluator evaluator, DateTime nowUtc)
    {
        if (!Applies(context, tenantScope)
            || await RequestAccount.ResolveAsync(context, users) is not { } account
            || !AttendanceLockEvaluator.IsLockable(account)
            || !await evaluator.IsLockedAsync(account, nowUtc, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status423Locked;
        await context.Response.WriteAsJsonAsync(new
        {
            error = AttendanceLockRules.ClockInRequired,
            code = AttendanceLockRules.ClockInRequiredCode,
        });
    }

    /// <remarks>⚠️ No endpoint matched passes, for <see cref="SubscriptionGateMiddleware"/>'s reason: a mistyped URL reaches routing's 404.</remarks>
    private static bool Applies(HttpContext context, ITenantScope tenantScope) =>
        context.Request.Path.StartsWithSegments(WriteGate.ApiPrefix)
        && !WriteGate.IsRead(context.Request.Method)
        && tenantScope.Kind == TenantScopeKind.Clinic
        && context.GetEndpoint() is { } endpoint
        && endpoint.Metadata.GetMetadata<AllowsBeforeClockInAttribute>() is null;

}
