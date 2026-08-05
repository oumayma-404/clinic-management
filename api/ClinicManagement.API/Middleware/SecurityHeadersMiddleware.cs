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
/// <para><b>HSTS is off by default only where the certificate is self-signed</b> (AC-12.7). A self-generated CA
/// plus HSTS on a device that never imported it converts a bypassable certificate warning into a permanent hard
/// failure — so there it must be opted into explicitly, and only once every device trusts the CA. A deployment
/// served over a publicly-trusted certificate gets HSTS on, which is why this asks about the certificate rather
/// than about the login provider.</para>
/// </summary>
public class SecurityHeadersMiddleware
{
    /// <summary>Config key promoting the policy below from report-only to enforcing. Default <c>false</c>.</summary>
    public const string EnforceCspKey = "Security:EnforceCsp";

    /// <summary>
    /// The policy. <c>'unsafe-inline'</c> for styles is required by Next; <c>blob:</c> covers the client-side
    /// docx/file-saver exports; <c>object-src</c>/<c>frame-src 'self'</c> cover the inline PDF the document
    /// preview returns. Sent report-only unless <see cref="EnforceCspKey"/> says otherwise, so by default a miss
    /// is a console entry rather than a broken screen.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'unsafe-inline' 'unsafe-eval'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data: blob:; "
        + "font-src 'self' data:; "
        + "connect-src 'self'; "
        + "object-src 'self' blob:; "
        + "frame-src 'self' blob:; "
        + "frame-ancestors 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'";

    private readonly RequestDelegate _next;
    private readonly bool _hstsEnabled;
    private readonly bool _cspEnforced;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        // Opt-in where the certificate is self-signed, on everywhere else — see the CA interaction above.
        _hstsEnabled = !DeploymentProfile.Resolve(configuration).SelfSignsCertificate
                       || configuration.GetValue("Security:EnableHsts", false);

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
