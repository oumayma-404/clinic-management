using System.Diagnostics;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// One line per <c>/api</c> request (time + SQL count); <c>Information</c> from <c>Diagnostics:SlowRequestMs</c> (500, 0 = all).
/// ⚠️ Route template only — a raw path carries patient ids, and <c>/hub</c>'s query string carries the token.
/// </summary>
public class RequestTimingMiddleware
{
    private const int DefaultSlowRequestMs = 500;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public RequestTimingMiddleware(
        RequestDelegate next, ILogger<RequestTimingMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, RequestQueryMetrics metrics)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var slowRequestMs = _configuration.GetValue<int?>("Diagnostics:SlowRequestMs") ?? DefaultSlowRequestMs;
            var level = elapsedMs >= slowRequestMs ? LogLevel.Information : LogLevel.Debug;

            if (_logger.IsEnabled(level))
            {
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";

                _logger.Log(
                    level,
                    "{Method} {Route} → {StatusCode} in {ElapsedMs:F0} ms · {Queries} queries ({DbMs:F0} ms)",
                    context.Request.Method,
                    route,
                    context.Response.StatusCode,
                    elapsedMs,
                    metrics.Commands,
                    metrics.DbTime.TotalMilliseconds);
            }
        }
    }
}
