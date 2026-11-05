using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Demo;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// The public demo is read-only (<c>demo-cabinet</c> FR-2): every write is refused with 403 <c>demo_read_only</c>
/// unless the action carries <see cref="AllowedInDemoAttribute"/>, and the reads marked
/// <see cref="RefusedInDemoAttribute"/> are refused too. Inert on every other deployment.
/// </summary>
/// <remarks>
/// Registered after the token checks (a revoked token is still a 401) and before <c>IdempotencyMiddleware</c>, so a
/// refused write never claims an idempotency key. Writes the server makes by itself during a read stay allowed.
/// </remarks>
public class DemoReadOnlyMiddleware
{
    private const string ApiPrefix = "/api";

    private readonly RequestDelegate _next;

    public DemoReadOnlyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IDemoPolicy demo)
    {
        if (demo.RefusesWrites && IsRefused(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = DemoRefusals.ReadOnlyMessage, code = DemoRefusals.ReadOnlyCode });
            return;
        }

        await _next(context);
    }

    private static bool IsRefused(HttpContext context)
    {
        // An unroutable path is routing's 404 to answer, never a demo refusal.
        if (!context.Request.Path.StartsWithSegments(ApiPrefix) || context.GetEndpoint() is not { } endpoint)
        {
            return false;
        }

        return IsRead(context.Request.Method)
            ? endpoint.Metadata.GetMetadata<RefusedInDemoAttribute>() is not null
            : endpoint.Metadata.GetMetadata<AllowedInDemoAttribute>() is null;
    }

    private static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
