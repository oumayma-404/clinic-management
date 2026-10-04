using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// « Mode discret »: refuses every endpoint marked <see cref="HiddenWhenMoneyMaskedAttribute"/> with
/// <b>403 <c>money_hidden</c></b> while the cabinet has hidden its money. Unmarked endpoints are never read.
///
/// <para>Registered after <c>SubscriptionGateMiddleware</c> for the same reason it sits where it does: the tenant
/// scope is set and endpoint metadata is available.</para>
/// </summary>
public class MoneyMaskGateMiddleware
{
    private readonly RequestDelegate _next;

    public MoneyMaskGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantScope tenantScope, IClinicRepository clinics)
    {
        var marker = context.GetEndpoint()?.Metadata.GetMetadata<HiddenWhenMoneyMaskedAttribute>();

        if (marker is null
            || IsPatientScoped(context, marker)
            || tenantScope.Kind != TenantScopeKind.Clinic
            || tenantScope.ClinicId is not { } clinicId
            || !await clinics.IsMoneyHiddenAsync(clinicId, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = ClinicMoneyMask.Hidden, code = ClinicMoneyMask.HiddenCode });
    }

    private static bool IsPatientScoped(HttpContext context, HiddenWhenMoneyMaskedAttribute marker) =>
        marker.UnlessQuery is { } name && !string.IsNullOrWhiteSpace(context.Request.Query[name]);
}
