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
///   <item><b>Anonymous auth</b> — per <b>submitted account</b>, tight. This is the brute-force surface.</item>
///   <item><b>Authenticated API</b> — per user, generous. This is not traffic shaping; it exists to bound a
///   runaway client loop or scraping. Normal use must never reach it.</item>
/// </list>
///
/// <para><b>⚠️ The auth limiter is keyed on the account, and the address is a second, looser ceiling</b>
/// (multi-tenant-cloud US-6). It used to be per address alone, which is a lockout waiting to happen the moment a
/// deployment is reached over the internet: a whole practice arrives through **one** public NAT address, so a
/// single colleague fat-fingering their password ten times spends everybody's budget and the receptionist is told
/// « trop de tentatives » for a password she typed correctly. Keying the tight window on the email puts the
/// guessing limit where guessing happens — one account — while the address keeps a ceiling several times higher,
/// so one source cannot walk a list of accounts unbounded. Both apply: the named policy partitions on the
/// account, the global limiter partitions the same request on its address (see <see cref="PartitionKey"/>).</para>
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

    // The tight window, now per submitted ACCOUNT (US-6). 30 attempts in five minutes is far more than a person
    // mistyping their own password and far less than a guessing run.
    private const int DefaultAuthPermitLimit = 30;
    private const int DefaultAuthWindowSeconds = 300;

    // The per-ADDRESS ceiling over the same window: five times the per-account limit, so a practice of a dozen
    // people signing in behind one NAT address cannot reach it in ordinary use, while a single source enumerating
    // accounts is still stopped. It is a ceiling, not the brake — the account window above is the brake.
    private const int DefaultAuthAddressPermitLimit = 150;

    private const int DefaultApiPermitLimit = 600;
    private const int DefaultApiWindowSeconds = 60;

    private const int SegmentsPerWindow = 6;

    /// <summary>
    /// Where <see cref="AuthAttemptAccount"/> leaves the submitted email for the partitioner. On
    /// <c>HttpContext.Items</c> because the limiter runs before model binding, so the value has to be lifted out
    /// of the body once and shared rather than re-read.
    /// </summary>
    public const string SubmittedAccountItemKey = "RateLimiting:SubmittedAccount";

    private static readonly string[] ExemptPathPrefixes =
    {
        "/api/connectivity",                 // polled every 15 s per tab; a 429 reads as "offline"
        "/api/googlecalendar/callback",      // one-shot OAuth redirect we do not control the timing of
        "/hub",                              // long-lived SignalR connection
        "/hangfire",                         // loopback-only dashboard; its own polling must not self-limit
        // Polled every few seconds for the life of the deployment, and a 429 reads to an orchestrator exactly
        // like « unhealthy ». Listed even though the not-/api rule below already covers it: this exemption must
        // hold because of what the endpoint IS, not because of where it happens to be mounted.
        HealthChecks.Path
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
        // Shares authWindow deliberately rather than taking a fourth knob: the two figures are only comparable —
        // « the address may spend five accounts' worth » — while they cover the same period.
        var authAddressPermitLimit = Read(
            configuration, "RateLimiting:Auth:AddressPermitLimit", DefaultAuthAddressPermitLimit);
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
                    AuthAttemptPartitionKey(httpContext),
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

                // An anonymous auth request is bounded on BOTH dimensions: the named policy above spends its
                // account's budget, this one spends its address's. Without the branch the address ceiling on a
                // login would be the API window (600/min), which against a run through a list of accounts is no
                // ceiling at all — so re-keying the policy on the account would have traded a lockout for a hole.
                if (IsAnonymousAuthPath(httpContext.Request.Path))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        $"authip:{ClientIp.Resolve(httpContext)}",
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = authAddressPermitLimit,
                            Window = authWindow,
                            SegmentsPerWindow = SegmentsPerWindow,
                            QueueLimit = 0
                        });
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
    /// True for the anonymous auth surface — the paths whose requests are bounded on both the account and the
    /// address. A <b>prefix</b> and not a list of the four actions carrying
    /// <c>[EnableRateLimiting(AnonymousAuthPolicy)]</c>, deliberately: a list is a second place to remember, and
    /// the fifth auth endpoint somebody adds would silently get the API ceiling instead of this one.
    /// </summary>
    public static bool IsAnonymousAuthPath(PathString path) =>
        path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The account this attempt is against, else the address.
    ///
    /// <para><b>Falling back to the address is what keeps an unreadable body from being either a bypass or a new
    /// lockout.</b> <c>POST auth/refresh</c> carries no email at all, and a malformed or oversized body yields
    /// none — so those requests are bounded exactly as every request was before this change. A shared
    /// « no-account » bucket would have been the alternative, and one attacker could empty it for everybody.</para>
    ///
    /// <para>The two forms are prefixed so an email can never collide with an address partition.</para>
    /// </summary>
    public static string AuthAttemptPartitionKey(HttpContext httpContext)
    {
        var account = httpContext.Items.TryGetValue(SubmittedAccountItemKey, out var captured)
            ? captured as string
            : null;

        return string.IsNullOrEmpty(account)
            ? $"ip:{ClientIp.Resolve(httpContext)}"
            : $"account:{account}";
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
