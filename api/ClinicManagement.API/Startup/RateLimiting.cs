using System.Globalization;
using System.Threading.RateLimiting;
using ClinicManagement.Application.Common;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
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
/// <para><b>⚠️ The auth limiter is keyed on the account <i>and</i> the address, with the address alone as a second,
/// looser ceiling</b> (multi-tenant-cloud US-6, tightened by review finding 8). It used to be per address alone,
/// which is a lockout waiting to happen the moment a deployment is reached over the internet: a whole practice
/// arrives through **one** public NAT address, so a single colleague fat-fingering their password ten times spends
/// everybody's budget and the receptionist is told « trop de tentatives » for a password she typed correctly.
/// Including the email puts the guessing limit where guessing happens — one account from one place.</para>
///
/// <para><b>⚠️ The account alone was not enough, and the address alone is what saves it.</b> The permit is spent
/// <i>before</i> authentication, on every attempt whatever its outcome, so a window keyed on the account alone hands
/// a lockout to whoever merely <b>names</b> it — and staff emails are printed on ordonnances, certificats and
/// invoices. Keying on (account, address) means an attacker exhausts only their own bucket while the victim keeps
/// theirs; the per-address ceiling below is then what stops one source walking a list of accounts, which is the hole
/// a compound key would otherwise open. Both bounds are required — neither alone is sound.</para>
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

    /// <summary>
    /// Policy name for the full-cabinet archive — download and restore (<c>hosted-security-hardening</c> FR-4.2).
    ///
    /// <para><b>It had none, and the general limiter is no limit at all here.</b> The archive fell to the
    /// authenticated API window — 600 requests per 60 s per <c>sub</c> — so one administrator could pull
    /// <b>six hundred complete copies of a practice's medical records a minute</b>. Each one is built into a temp
    /// file and streamed, so that is also the cheapest way to fill a hosted deployment's disk.</para>
    /// </summary>
    public const string ArchivePolicy = "clinic-archive";

    /// <summary>
    /// Policy name for the device grant→token exchange (<c>clinic-archive-auto-copy</c>).
    ///
    /// <para>⚠️ <b>Separate from <see cref="ArchivePolicy"/>, and it was not — which is what made
    /// <c>patient-file-mirror</c> unusable.</b> That policy is « three exports in ten minutes », sized for an act
    /// that builds a whole cabinet into a temp file and streams it. The exchange is neither expensive nor an
    /// export: it is a cheap auth call that mints an ordinary short-lived token. Sharing one budget meant a single
    /// « Copier maintenant » — which legitimately takes the archive AND refreshes the file mirror — spent it, so
    /// clicking twice in ten minutes was answered 429 on the exchange and read to the user as « ce poste n'est
    /// plus autorisé ».</para>
    ///
    /// <para>⚠️ Still tightly bounded, because the endpoint is <c>[AllowAnonymous]</c> and the grant secret is
    /// the only thing standing in front of it. That secret is <b>32 CSPRNG bytes</b>, so no per-window figure in a
    /// sane range moves the guessing odds at all; what this bounds is a flood, not a search.</para>
    /// </summary>
    public const string ArchiveGrantTokenPolicy = "clinic-archive-grant-token";

    /// <summary>The PC de secours's anonymous pairing and token exchange (clinic-pc-copy), on the archive grant's terms.</summary>
    public const string RelayTokenPolicy = "clinic-relay-token";

    /// <summary>
    /// The PC de secours installer download (clinic-pc-copy). ⚠️ <b>Not <see cref="RelayTokenPolicy"/></b>, and it was:
    /// a PC fetches the 361 Mo file in 8 Mo pieces, one request each, so twenty requests per ten minutes stopped every
    /// update half-way with 429s and stretched it over half an hour (prod, 2026-10-12). The file is the same public
    /// product for every cabinet and names nothing secret; what this bounds is a flood, per address.
    /// </summary>
    public const string RelayInstallerPolicy = "clinic-relay-installer";

    /// <summary>
    /// « Exporter » on a list screen — the patient roster and the agenda, as CSV.
    ///
    /// <para>⚠️ <b>Separate from <see cref="ArchivePolicy"/> on purpose.</b> The data is comparably sensitive —
    /// the patient CSV carries date of birth, address, identifiant CNAM, antécédents médicaux and allergies —
    /// but the *cadence* is not: an archive is a rare, deliberate act, while a practice exports a recall list or
    /// a contact list for its accountant in the ordinary course of a week. Sharing the archive's budget of three
    /// per ten minutes would mean one afternoon's normal work spends it and the next export is refused with a
    /// message about a limit nobody understands.</para>
    ///
    /// <para>What it is for is the bulk case: this endpoint returns the WHOLE filtered set with no paging, so an
    /// unbounded one is a loop that walks a cabinet's entire identified medical dataset out of the building.</para>
    /// </summary>
    public const string ListExportPolicy = "list-export";

    /// <summary>
    /// Policy name for the CSP violation sink (FR-4.5). Anonymous and unauthenticated, so it is bounded per
    /// address — and generously, because one page load can legitimately produce several reports while one
    /// misbehaving browser extension can produce one per navigation for ever. Excess is <b>dropped</b>: a
    /// diagnostic feed that fills a disk is worse than one with holes in it.
    /// </summary>
    public const string CspReportPolicy = "csp-report";

    /// <summary>
    /// Writes that send an APEXA e-mail to an address the caller typed or to the vendor — « Renvoyer l'e-mail » and
    /// « Donner mon avis ». Per user: a self-signed-up cabinet must not be a free mailer.
    /// </summary>
    public const string OutboundEmailPolicy = "outbound-email";

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

    // Three exports in ten minutes. Taking a copy is an occasional, deliberate act — an owner does it before a
    // migration, or when their accountant asks — and three leaves room for a failed download and a retry without
    // ever resembling a bulk pull. Deliberately per USER rather than per address: this endpoint is behind
    // AdminOnly *and* a step-up, so the actor is always identified, and per-address would bound a whole practice
    // sharing one NAT address on the actions of one of them.
    private const int DefaultArchivePermitLimit = 3;
    private const int DefaultListExportPermitLimit = 10;
    private const int DefaultListExportWindowSeconds = 600;
    private const int DefaultArchiveWindowSeconds = 600;

    // Twenty exchanges in ten minutes. Generous next to the three EXPORTS above because this mints a token and
    // nothing else; it leaves an operator room to set the feature up, click « Copier maintenant » a few times to
    // verify it, and still have the scheduled copy work.
    private const int DefaultArchiveGrantTokenPermitLimit = 20;
    private const int DefaultArchiveGrantTokenWindowSeconds = 600;

    // Five whole installers (44 pieces each) per address in ten minutes: several PCs behind one cabinet line, retries.
    private const int DefaultRelayInstallerPermitLimit = 240;
    private const int DefaultRelayInstallerWindowSeconds = 600;

    private const int DefaultCspReportPermitLimit = 60;
    private const int DefaultOutboundEmailPermitLimit = 20;
    private const int DefaultOutboundEmailWindowSeconds = 3600;
    private const int DefaultCspReportWindowSeconds = 60;

    /// <summary>The demo's PDFs, per visit (<c>demo-cabinet</c> AC-5.6); no limiter on any other deployment.</summary>
    public const string DemoPdfPolicy = "demo-pdf";

    /// <summary>The code on the demo's PDF refusal, so the browser never has to read the sentence.</summary>
    public const string DemoPdfLimitedCode = "demo_pdf_limited";

    // A visitor opening, downloading and printing every document of the cabinet stays well under it; a script does not.
    private const int DefaultDemoPdfPermitLimit = 30;
    private const int DefaultDemoPdfWindowSeconds = 600;

    // On the demo the visit key is read unvalidated, so a forged token is a fresh bucket; this per-address ceiling bounds that.
    private const int DefaultDemoAddressPermitLimit = 3000;

    private const int SegmentsPerWindow = 6;

    /// <summary>
    /// Where <see cref="AuthAttemptAccount"/> leaves the submitted email for the partitioner. On
    /// <c>HttpContext.Items</c> because the limiter runs before model binding, so the value has to be lifted out
    /// of the body once and shared rather than re-read.
    /// </summary>
    public const string SubmittedAccountItemKey = "RateLimiting:SubmittedAccount";

    /// <summary>
    /// Where <see cref="AuthAttemptAccount"/> leaves the <b>hashed</b> refresh token, for the one auth route that
    /// carries no email — <c>POST auth/refresh</c>. See <see cref="AuthAttemptPartitionKey"/>.
    /// </summary>
    public const string SubmittedSessionItemKey = "RateLimiting:SubmittedSession";

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
        var archivePermitLimit = Read(
            configuration, "RateLimiting:Archive:PermitLimit", DefaultArchivePermitLimit);
        var listExportPermitLimit = Read(
            configuration, "RateLimiting:ListExport:PermitLimit", DefaultListExportPermitLimit);
        var listExportWindow = TimeSpan.FromSeconds(
            Read(configuration, "RateLimiting:ListExport:WindowSeconds", DefaultListExportWindowSeconds));
        var archiveWindow = TimeSpan.FromSeconds(
            Read(configuration, "RateLimiting:Archive:WindowSeconds", DefaultArchiveWindowSeconds));

        var grantTokenPermitLimit = Read(
            configuration, "RateLimiting:ArchiveGrantToken:PermitLimit", DefaultArchiveGrantTokenPermitLimit);
        var grantTokenWindow = TimeSpan.FromSeconds(Read(
            configuration, "RateLimiting:ArchiveGrantToken:WindowSeconds", DefaultArchiveGrantTokenWindowSeconds));
        var relayInstallerPermitLimit = Read(
            configuration, "RateLimiting:RelayInstaller:PermitLimit", DefaultRelayInstallerPermitLimit);
        var relayInstallerWindow = TimeSpan.FromSeconds(Read(
            configuration, "RateLimiting:RelayInstaller:WindowSeconds", DefaultRelayInstallerWindowSeconds));
        var cspReportPermitLimit = Read(
            configuration, "RateLimiting:CspReport:PermitLimit", DefaultCspReportPermitLimit);
        var cspReportWindow = TimeSpan.FromSeconds(
            Read(configuration, "RateLimiting:CspReport:WindowSeconds", DefaultCspReportWindowSeconds));

        var outboundEmailPermitLimit = Read(
            configuration, "RateLimiting:OutboundEmail:PermitLimit", DefaultOutboundEmailPermitLimit);
        var outboundEmailWindow = TimeSpan.FromSeconds(Read(
            configuration, "RateLimiting:OutboundEmail:WindowSeconds", DefaultOutboundEmailWindowSeconds));

        // Resolved once: behind a reverse proxy every peer is the proxy container, so without this every partition
        // below is one bucket for the whole deployment (review finding 1).
        var trustedProxies = TrustedProxies.FromConfiguration(configuration);

        // Every demo visitor signs in as the same dentist, so on the demo a « user » bucket is one visit (its family).
        var isDemo = DeploymentProfile.Resolve(configuration).IssuesDemoSessions;
        var demoPdfPermitLimit = Read(configuration, "RateLimiting:DemoPdf:PermitLimit", DefaultDemoPdfPermitLimit);
        var demoPdfWindow = TimeSpan.FromSeconds(
            Read(configuration, "RateLimiting:DemoPdf:WindowSeconds", DefaultDemoPdfWindowSeconds));
        var demoAddressPermitLimit = Read(
            configuration, "RateLimiting:Api:DemoAddressPermitLimit", DefaultDemoAddressPermitLimit);

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

                if (IsDemoPdfRequest(context.HttpContext))
                {
                    await response.WriteAsJsonAsync(
                        new { error = DemoPdfLimitedMessage(seconds), code = DemoPdfLimitedCode },
                        cancellationToken);
                    return;
                }

                await response.WriteAsJsonAsync(
                    new { error = TooManyRequestsMessage(seconds) },
                    cancellationToken);
            };

            options.AddPolicy(DemoPdfPolicy, httpContext =>
                isDemo
                    ? RateLimitPartition.GetSlidingWindowLimiter(
                        $"demo-pdf:{DemoSessionPartitionKey(httpContext, trustedProxies)}",
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = demoPdfPermitLimit,
                            Window = demoPdfWindow,
                            SegmentsPerWindow = SegmentsPerWindow,
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("not-demo"));

            options.AddPolicy(AnonymousAuthPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    AuthAttemptPartitionKey(httpContext, trustedProxies),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitLimit,
                        Window = authWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            // FR-4.2's tight bound on the full-cabinet archive. Applied *in addition* to the global limiter
            // below, which still bounds the same request on its own window — a named policy narrows, it does not
            // replace.
            options.AddPolicy(ArchivePolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"archive:{ArchivePartitionKey(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = archivePermitLimit,
                        Window = archiveWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(ListExportPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"list-export:{ArchivePartitionKey(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = listExportPermitLimit,
                        Window = listExportWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(OutboundEmailPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"outbound-email:{PartitionKey(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = outboundEmailPermitLimit,
                        Window = outboundEmailWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(ArchiveGrantTokenPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    // The caller is anonymous by construction — the whole point is that it has no session — so
                    // this partitions on the address. `ArchivePartitionKey` would fall back to the same thing.
                    $"archive-grant-token:{ClientIp.Resolve(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = grantTokenPermitLimit,
                        Window = grantTokenWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(RelayTokenPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"relay-token:{ClientIp.Resolve(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = grantTokenPermitLimit,
                        Window = grantTokenWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(RelayInstallerPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"relay-installer:{ClientIp.Resolve(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = relayInstallerPermitLimit,
                        Window = relayInstallerWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(CspReportPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"csp:{ClientIp.Resolve(httpContext, trustedProxies)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = cspReportPermitLimit,
                        Window = cspReportWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            var perCaller = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (IsExempt(httpContext))
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }

                // An anonymous auth request is bounded on BOTH dimensions: the named policy above spends its
                // (account, address) budget, this one spends its address's. Without the branch the address ceiling
                // on a login would be the API window (600/min), which against a run through a list of accounts is
                // no ceiling at all — so keying the policy on the account would have traded a lockout for a hole.
                if (IsAnonymousAuthAttempt(httpContext))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        $"authip:{ClientIp.Resolve(httpContext, trustedProxies)}",
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = authAddressPermitLimit,
                            Window = authWindow,
                            SegmentsPerWindow = SegmentsPerWindow,
                            QueueLimit = 0
                        });
                }

                return RateLimitPartition.GetSlidingWindowLimiter(
                    isDemo ? DemoSessionPartitionKey(httpContext, trustedProxies) : PartitionKey(httpContext, trustedProxies),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = apiPermitLimit,
                        Window = apiWindow,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    });
            });

            options.GlobalLimiter = isDemo
                ? PartitionedRateLimiter.CreateChained(perCaller, PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    IsExempt(httpContext) || IsAnonymousAuthAttempt(httpContext)
                        ? RateLimitPartition.GetNoLimiter("exempt")
                        : RateLimitPartition.GetSlidingWindowLimiter(
                            DemoAddressKey(httpContext, trustedProxies),
                            _ => new SlidingWindowRateLimiterOptions
                            {
                                PermitLimit = demoAddressPermitLimit,
                                Window = apiWindow,
                                SegmentsPerWindow = SegmentsPerWindow,
                                QueueLimit = 0
                            })))
                : perCaller;
        });
    }

    /// <summary>The French 429 body. Rounded to minutes once past a minute so it reads naturally.</summary>
    public static string TooManyRequestsMessage(int retryAfterSeconds) =>
        $"Trop de tentatives. Veuillez réessayer dans {Wait(retryAfterSeconds)}.";

    /// <summary>The demo's PDF refusal: the visitor did nothing wrong, the demo just is not a free printer.</summary>
    public static string DemoPdfLimitedMessage(int retryAfterSeconds) =>
        $"La démo limite le nombre de documents ouverts. Réessayez dans {Wait(retryAfterSeconds)}.";

    private static string Wait(int seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds} secondes";
        }

        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    /// <summary>
    /// Who a demo request is charged to: the visit (its session family), since every visitor is the same dentist;
    /// the address when no visit is named.
    /// </summary>
    public static string DemoSessionPartitionKey(HttpContext httpContext, TrustedProxies trustedProxies)
    {
        var family = httpContext.User?.FindFirst(LocalAuthClaims.SessionFamily)?.Value ?? FamilyFromBearer(httpContext);
        return string.IsNullOrWhiteSpace(family)
            ? $"ip:{ClientIp.Resolve(httpContext, trustedProxies)}"
            : $"visit:{family}";
    }

    // The limiter runs before authentication, so the visit is read off the bearer unvalidated; a forged one still fails authentication right after.
    private static string? FamilyFromBearer(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = header["Bearer ".Length..].Trim().Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var claims = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
            return claims.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && claims.RootElement.TryGetProperty(LocalAuthClaims.SessionFamily, out var family)
                && family.ValueKind == System.Text.Json.JsonValueKind.String
                    ? family.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The demo's ceiling over every visit from one line (see <c>DefaultDemoAddressPermitLimit</c>).</summary>
    public static string DemoAddressKey(HttpContext httpContext, TrustedProxies trustedProxies) =>
        $"demoip:{ClientIp.Resolve(httpContext, trustedProxies)}";

    private static bool IsDemoPdfRequest(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == DemoPdfPolicy;

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
        path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        // The vendor console's sign-in (platform-console AC-1.5). A prefix the limiter does not know gets the
        // loose API ceiling (600/60 s) instead of the tight auth window — which on the product's
        // highest-privilege credential is not a decision anybody would take deliberately. ⚠️ Widening this one
        // predicate widens the ACCOUNT capture too: AuthAttemptAccount asks IsAnonymousAuthAttempt rather than
        // repeating the terms, so the limiter and the capture cannot disagree about what an auth attempt is.
        || path.StartsWithSegments("/api/platform/auth", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A request the tight auth bounds apply to: a <b>POST</b> to that surface.
    ///
    /// <para>⚠️ The method test is the point (review finding 24). The prefix alone also caught
    /// <c>GET auth/mode</c> — read on every app start by <c>/join</c> and <c>/users</c> — and the authenticated
    /// <c>POST auth/change-password</c>, dropping both from 600 permits / 60 s to 150 / 300 s, a 20× cut in
    /// sustained rate on two routes that are not a brute-force surface. <see cref="AuthAttemptAccount"/> asks this
    /// same question, so the limiter and the capture cannot disagree about what an auth attempt is.</para>
    /// </summary>
    public static bool IsAnonymousAuthAttempt(HttpContext httpContext) =>
        HttpMethods.IsPost(httpContext.Request.Method)
        && IsAnonymousAuthPath(httpContext.Request.Path);

    /// <summary>
    /// The account this attempt is against <b>together with</b> the address it came from, else the address alone.
    ///
    /// <para><b>Both dimensions, because either alone is exploitable.</b> Per address alone locks out a whole
    /// practice behind one NAT address (US-6's reason for moving off it); per <i>account</i> alone hands a
    /// permanent lockout to anyone who knows a staff email, since the permit is spent before authentication and
    /// regardless of outcome (review finding 8). Keyed on the pair, an attacker exhausts only their own bucket and
    /// the account's real owner keeps theirs — and the separate per-address ceiling in
    /// <see cref="AddConfiguredRateLimiter"/> is what stops one source walking a list of accounts, which is the
    /// hole this compound key would otherwise open.</para>
    ///
    /// <para><b>Falling back to the address is what keeps an unreadable body from being either a bypass or a new
    /// lockout.</b> <c>POST auth/refresh</c> carries no email at all, and a malformed or oversized body yields
    /// none — so those requests are bounded exactly as every request was before this change. A shared
    /// « no-account » bucket would have been the alternative, and one attacker could empty it for everybody.</para>
    ///
    /// <para><b>And then the session, for <c>POST auth/refresh</c>.</b> That route carries no email, so it fell to
    /// the address — one bucket of 30 per 300 s for a whole practice's <i>silent token renewal</i> behind a single
    /// NAT address, on the one auth route where a refusal is invisible to the user and shows up as a loop of 401s
    /// instead. Keying on the session gives each signed-in browser its own budget. It is <b>not</b> a bypass: a
    /// caller with no readable session still falls to the address below, and the separate per-address ceiling in
    /// <see cref="AddConfiguredRateLimiter"/> bounds this route as it bounds every other on the auth surface. The
    /// value is a HASH of the token, never the token — see <c>AuthAttemptAccount.ReadSessionKey</c>.</para>
    ///
    /// <para>⚠️ The session key deliberately does NOT include the address. A refresh is issued to a browser, and a
    /// laptop that moves between the cabinet's Wi-Fi and a mobile hotspot would otherwise get a fresh budget on
    /// each move — which is the hole, not the feature. One session, one budget, wherever it is.</para>
    ///
    /// <para>The three forms are prefixed so an email, a session and an address can never collide.</para>
    /// </summary>
    public static string AuthAttemptPartitionKey(HttpContext httpContext, TrustedProxies trustedProxies)
    {
        var account = httpContext.Items.TryGetValue(SubmittedAccountItemKey, out var captured)
            ? captured as string
            : null;

        var address = ClientIp.Resolve(httpContext, trustedProxies);

        if (!string.IsNullOrEmpty(account))
        {
            return $"account:{account}|{address}";
        }

        // Then the session, for `POST auth/refresh` — the one auth route with no email and no anonymous caller.
        var session = httpContext.Items.TryGetValue(SubmittedSessionItemKey, out var capturedSession)
            ? capturedSession as string
            : null;

        return string.IsNullOrEmpty(session)
            ? $"ip:{address}"
            : $"session:{session}";
    }

    /// <summary>
    /// Who an archive request is charged to. The signed-in user, which every request here has — the endpoints are
    /// <c>AdminOnly</c> behind a step-up — falling back to the address only so an unauthenticated probe cannot
    /// share one unbounded bucket with every other.
    /// </summary>
    public static string ArchivePartitionKey(HttpContext httpContext, TrustedProxies trustedProxies) =>
        PartitionKey(httpContext, trustedProxies);

    /// <summary>
    /// Per authenticated user where possible, else per resolved client address, so one signed-in client's
    /// runaway loop cannot consume another's allowance.
    /// </summary>
    private static string PartitionKey(HttpContext httpContext, TrustedProxies trustedProxies)
    {
        var subject = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? $"ip:{ClientIp.Resolve(httpContext, trustedProxies)}"
            : $"user:{subject}";
    }

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var configured = configuration.GetValue<int?>(key);
        return configured is > 0 ? configured.Value : fallback;
    }
}
