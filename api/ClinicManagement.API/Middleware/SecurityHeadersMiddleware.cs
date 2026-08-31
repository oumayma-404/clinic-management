using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Adds the baseline browser-protection headers to every response (security-hardening US-12, audit § 2
/// finding 13 — the only <c>nosniff</c> in the whole codebase was set inline on one endpoint).
///
/// <para><b>Placement matters.</b> Registered before the reverse proxy, so where the front door is self-hosted —
/// Kestrel being the single browser-facing endpoint, proxying every non-<c>/api</c> route to Next — this covers
/// the application's pages as well as the API (AC-12.5). Headers are written on response start rather than after
/// <c>next()</c>, because the response may already be streaming by then.</para>
///
/// <para><b>The CSP ships report-only, and enforcing it is one config key away</b> (AC-12.2 / AC-12.4, key added
/// by multi-tenant-cloud US-6). Next.js needs inline styles and its own hydration payload, so an enforcing policy
/// would risk a visually broken screen for a clinic rather than a console warning. <c>Security:EnforceCsp</c> is
/// therefore the operator's switch for *after* the page walk is clean, and it defaults to <b>false in every
/// profile</b>: the deciding fact is whether these pages have been walked in this deployment, which is not
/// something the topology can answer.</para>
///
/// <para>⚠️ <b>The policy was checked against Next's own before the key was added</b>, since two CSP headers make
/// the browser enforce their intersection rather than either one (plan risk R-13). <c>web/next.config.ts</c> emits
/// <i>no</i> CSP at all — only <c>nosniff</c>, <c>X-Frame-Options</c> and <c>Referrer-Policy</c>, and only where
/// <c>AUTH_MODE != local</c> — so there is nothing here to intersect with. The <c>ContainsKey</c> guard below stays
/// anyway, because it defends against whatever upstream component sets one next.</para>
///
/// <para><b>HSTS is emitted only where this process is the browser-facing edge</b>, and there it is opt-in
/// (AC-12.7): a self-generated CA plus HSTS on a device that never imported it converts a bypassable certificate
/// warning into a permanent hard failure, so it must be opted into explicitly and only once every device trusts
/// the CA.</para>
///
/// <para>⚠️ <b>Behind a reverse proxy the edge owns the header, and that became load-bearing in
/// hosted-security-hardening Part 2.</b> Until then this middleware's HSTS was unreachable on the hosted kinds
/// for an accidental reason — <c>Request.IsHttps</c> is false for a proxied request and nothing consumed
/// <c>X-Forwarded-Proto</c> — so <c>deploy/Caddyfile</c> sets the header itself and says « HSTS belongs HERE, not
/// in the API ». Part 2 registers <c>UseForwardedHeaders</c>, which makes <c>IsHttps</c> true and would have
/// emitted a <b>second</b> header beside Caddy's: verified by reproducing the shipped directive over an upstream
/// that sets its own, and the client received <b>two</b> — Caddy appends rather than replaces. RFC 6797 § 8.1
/// then has the browser honour only the first, so it was not a downgrade; it was a malformed response and a
/// header whose value nothing in the deployment could predict. Asking <c>SelfHostsFrontDoor</c> keeps the
/// Caddyfile's stated rule true in one place instead of stripping the duplicate at three proxy blocks.</para>
///
/// <para>The observable behaviour is therefore <b>unchanged in every profile</b>: what changed is that the reason
/// hosted deployments do not emit it is now stated rather than incidental.</para>
/// </summary>
public class SecurityHeadersMiddleware
{
    /// <summary>Config key promoting the policy below from report-only to enforcing. Default <c>false</c>.</summary>
    public const string EnforceCspKey = "Security:EnforceCsp";

    /// <summary>Config key opting HSTS in where the certificate is self-signed. Default <c>false</c>.</summary>
    public const string EnableHstsKey = "Security:EnableHsts";

    /// <summary>
    /// The policy. <c>'unsafe-inline'</c> for styles is required by Next; <c>blob:</c> covers the client-side
    /// docx/file-saver exports; <c>object-src</c>/<c>frame-src 'self'</c> cover the inline PDF the document
    /// preview returns. Sent report-only unless <see cref="EnforceCspKey"/> says otherwise, so by default a miss
    /// is a console entry rather than a broken screen.
    ///
    /// <para>⚠️ <b>What enforcing this buys, stated plainly, because the flag's name oversells it.</b>
    /// <c>script-src</c> carries <c>'unsafe-inline'</c>, which permits inline <c>&lt;script&gt;</c> and
    /// <c>javascript:</c> handlers — so against the attack CSP exists to
    /// mitigate, script injection into a product rendering free-text clinical notes and patient names, this policy
    /// is close to no script policy at all. Turning the key on constrains resource <b>origins</b> (where images,
    /// fonts, frames and XHR may come from, and who may frame us); it does not stop XSS. Getting there means Next's
    /// nonce/hash support with <c>strict-dynamic</c>, which is a change with its own page walk and is
    /// deliberately not smuggled in behind this flag.</para>
    ///
    /// <para>⚠️ <b><c>'wasm-unsafe-eval'</c> is here for the coffre, and it is NOT <c>'unsafe-eval'</c>.</b> It
/// permits <c>WebAssembly.compile</c> and nothing else — no <c>eval()</c>, no <c>new Function()</c>, no string
/// <c>setTimeout</c>. `clinic-file-vault` hashes a file <b>incrementally</b> while streaming it disk-to-disk,
/// which is what makes a 25 Go study recordable at all; <c>crypto.subtle.digest</c> cannot do that (it needs the
/// whole buffer in memory), so the hash comes from a WebAssembly SHA-256. Without this token every vault file
/// failed at the very first step, on every deployment where the policy is enforced — and because the hosted
/// upload path has no wasm in it, ordinary files kept working and nothing looked broken.</para>
///
/// <para>⚠️ <b>The vendor console does not need it and carries it anyway</b>, because these four copies are
/// asserted byte-identical and one policy that cannot drift is worth more than one token of extra surface on a
/// site that has no WebAssembly to compile. Revisit if the console ever diverges for a better reason.</para>
///
/// <para>⚠️ <b>This covers only what Kestrel serves.</b> Behind the hosted reverse proxy that is <c>/api/*</c>
    /// alone, so the page-side copy of this policy lives in <c>deploy/Caddyfile</c>'s page-response block. The two
    /// are byte-identical and must be changed together.</para>
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data: blob:; "
        + "font-src 'self' data:; "
        + "connect-src 'self'; "
        + "object-src 'self' blob:; "
        + "frame-src 'self' blob:; "
        + "frame-ancestors 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "report-uri /api/csp-report; "
        + "report-to csp-endpoint";

    /// <summary>
    /// Where a violation is sent (FR-4.5). <c>report-to</c> is the current mechanism and <c>report-uri</c> the
    /// deprecated one, and <b>both</b> are in the policy above deliberately: Chromium honours the first, Firefox
    /// and Safari still only implement the second, and a report nobody receives is the state this replaces.
    ///
    /// <para>⚠️ The reports are <b>scrubbed and bounded</b> at the endpoint — see <c>CspReportController</c>. A
    /// report body carries the page address, and this application's addresses contain patient identifiers, so
    /// reports are themselves subject to FR-4.4.</para>
    /// </summary>
    private const string ReportingEndpoints = "csp-endpoint=\"/api/csp-report\"";

    /// <summary>
    /// What this application never asks the browser for. An empty allow-list is stronger than an origin list:
    /// there is no camera, microphone, geolocation or payment surface in the product, so the honest declaration
    /// is that nobody may use them — including an injected script that got past everything above.
    /// </summary>
    private const string PermissionsPolicy =
        "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), fullscreen=(self), "
        + "geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), "
        + "picture-in-picture=(), publickey-credentials-get=(), screen-wake-lock=(), usb=(), xr-spatial-tracking=()";

    private readonly RequestDelegate _next;
    private readonly bool _hstsEnabled;
    private readonly bool _cspEnforced;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        // Only where this process is the edge, and opt-in there — see the two paragraphs above.
        _hstsEnabled = DeploymentProfile.Resolve(configuration).SelfHostsFrontDoor
                       && configuration.GetValue(EnableHstsKey, false);

        // Opt-in everywhere, and NOT derived from the profile: what makes enforcing safe is that somebody has
        // walked these pages in this deployment, and no capability knows that. Read once — a per-request read
        // would let a mid-session config reload change the header a page's assets are already loading under.
        _cspEnforced = configuration.GetValue(EnforceCspKey, false);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Reporting-Endpoints"] = ReportingEndpoints;

            // Cross-Origin-Opener-Policy severs the `window.opener` link, so a page this app opens — or one that
            // opened it — cannot reach into its context. Cross-Origin-Resource-Policy stops another site
            // embedding this one's responses as a subresource, which is what makes a PDF or a radiograph
            // readable cross-origin despite every other header here.
            //
            // ⚠️ `same-site`, not `same-origin`, on the second: the Local front door serves the pages and the
            // API from one origin, but the hosted deployment's console site is a different origin on the same
            // registrable domain, and `same-origin` would break it while looking like a tightening.
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            // Do not overwrite a policy an upstream component already set — in Cloud, Next emits its own for
            // page responses, and two CSP headers make the browser enforce the INTERSECTION rather than
            // either policy (plan risk R-13).
            if (!headers.ContainsKey("Content-Security-Policy")
                && !headers.ContainsKey("Content-Security-Policy-Report-Only"))
            {
                headers[_cspEnforced ? "Content-Security-Policy" : "Content-Security-Policy-Report-Only"] =
                    ContentSecurityPolicy;
            }

            // Never on the plain-HTTP loopback hop the Next BFF uses — HSTS is meaningless there and would
            // be recorded against localhost.
            if (_hstsEnabled && context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
