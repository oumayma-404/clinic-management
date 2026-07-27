using System.Globalization;
using System.Threading.RateLimiting;
using ClinicManagement.Application.Common;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Request rate limiting (security-hardening US-4, audit § 2 finding 5 — <c>AddRateLimiter</c> was absent
/// entirely, so anyone who could reach the login endpoint could hammer it without limit).
///
/// <para>Two limiters, deliberately different in shape:</para>
/// <list type="bullet">
///   <item><b>Anonymous auth</b> — per client address, tight. This is the brute-force surface.</item>
///   <item><b>Authenticated API</b> — per user, generous. This is not traffic shaping; it exists to bound a
///   runaway client loop or scraping. Normal use must never reach it.</item>
/// </list>
///
/// <para><b>Exemptions matter as much as the limits.</b> The connectivity probe is polled every 15 s <i>per
/// browser tab</i>, and a 429 there would make the app look offline and disable AI + Google Calendar. The
/// SignalR hub is a long-lived connection. And in Local mode Kestrel is the front door for <i>all</i> traffic,
/// so a global limiter would throttle the proxied Next pages and their <c>_next</c> chunks — a page load fires
/// dozens. The global limiter is therefore scoped to <c>/api/*</c> only.</para>
///
/// <para>Applies in <b>both</b> auth modes: Cloud is internet-facing and needs this at least as much.</para>
/// </summary>
public static class RateLimiting
{
    /// <summary>Policy name for the anonymous auth endpoints, applied via <c>[EnableRateLimiting]</c>.</summary>
    public const string AnonymousAuthPolicy = "anonymous-auth";

    // Defaults are deliberately loose enough for a whole clinic starting its day behind one NAT address
    // (spec EC-6) while still being a hard brake on guessing. Operator-tunable — see the Configure summary.
    private const int DefaultAuthPermitLimit = 30;
    private const int DefaultAuthWindowSeconds = 300;
    private const int DefaultApiPermitLimit = 600;
    private const int DefaultApiWindowSeconds = 60;

    private const int SegmentsPerWindow = 6;

    private static readonly string[] ExemptPathPrefixes =
    {
        "/api/connectivity",                 // polled every 15 s per tab; a 429 reads as "offline"
        "/api/googlecalendar/callback",      // one-shot OAuth redirect we do not control the timing of
        "/hub",                              // long-lived SignalR connection
        "/hangfire"                          // loopback-only dashboard; its own polling must not self-limit
    };

    /// <summary>
    /// Registers both limiters. Tunable via <c>RateLimiting:Auth:{PermitLimit,WindowSeconds}</c> and
    /// <c>RateLimiting:Api:{PermitLimit,WindowSeconds}</c> so an unusually busy cabinet can be loosened
    /// without a rebuild (AC-4.6).
    /// </summary>
    public static void AddConfiguredRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        var authPermitLimit = Read(configuration, "RateLimiting:Auth:PermitLimit", DefaultAuthPermitLimit);
        var authWindow = TimeSpan.FromSeconds(
            Read(configuration, "RateLimiting:Auth:WindowSeconds", DefaultAuthWindowSeconds));
        var apiPermitLimit = Read(configuration, "RateLimiting:Api:PermitLimit", DefaultApiPermitLimit);
        var apiWindow = TimeSpan.FromSeconds(
            Read(configuration, "RateLimiting:Api:WindowSeconds", DefaultApiWindowSeconds));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Render the refusal in the canonical { error } shape the frontend already parses, and always
            // emit Retry-After so the UI can say how long to wait rather than "try again later" (AC-4.5).
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan fromLease)
                    ? fromLease
                    : authWindow;
                var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                await response.WriteAsJsonAsync(
                    new { error = TooManyRequestsMessage(seconds) },
                    cancellationToken);
            };

            options.AddPolicy(AnonymousAuthPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    // Partitioned on the RESOLVED client address, not the peer — behind the Local front door
                    // every login arrives from loopback, so keying on the peer would bucket the entire clinic
                    // as one source and turn this limiter into the lockout it prevents. See ClientIp.
                    ClientIp.Resolve(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitLimit,
                        Window = authWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (IsExempt(httpContext))
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(
                    PartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = apiPermitLimit,
                        Window = apiWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    });
            });
        });
    }

    /// <summary>The French 429 body. Rounded to minutes once past a minute so it reads naturally.</summary>
    public static string TooManyRequestsMessage(int retryAfterSeconds)
    {
        if (retryAfterSeconds < 60)
        {
            return $"Trop de tentatives. Veuillez réessayer dans {retryAfterSeconds} secondes.";
        }

        var minutes = (int)Math.Ceiling(retryAfterSeconds / 60.0);
        return minutes == 1
            ? "Trop de tentatives. Veuillez réessayer dans 1 minute."
            : $"Trop de tentatives. Veuillez réessayer dans {minutes} minutes.";
    }

    /// <summary>
    /// True for requests the global limiter must not touch: the connectivity poll, the OAuth callback, the
    /// SignalR hub, the Hangfire dashboard — and everything outside <c>/api</c>, which in Local mode is the
    /// proxied Next application (pages and <c>_next</c> chunks).
    /// </summary>
    public static bool IsExempt(HttpContext httpContext)
    {
        var path = httpContext.Request.Path;

        foreach (var prefix in ExemptPathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Per authenticated user where possible, else per resolved client address, so one signed-in client's
    /// runaway loop cannot consume another's allowance.
    /// </summary>
    private static string PartitionKey(HttpContext httpContext)
    {
        var subject = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? $"ip:{ClientIp.Resolve(httpContext)}"
            : $"user:{subject}";
    }

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var configured = configuration.GetValue<int?>(key);
        return configured is > 0 ? configured.Value : fallback;
    }
}
