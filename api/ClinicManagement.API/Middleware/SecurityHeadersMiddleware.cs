using ClinicManagement.Infrastructure.Auth;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Adds the baseline browser-protection headers to every response (security-hardening US-12, audit § 2
/// finding 13 — the only <c>nosniff</c> in the whole codebase was set inline on one endpoint).
///
/// <para><b>Placement matters.</b> Registered before the reverse proxy, so in Local mode — where Kestrel is
/// the single browser-facing endpoint and proxies every non-<c>/api</c> route to Next — this covers the
/// application's pages as well as the API (AC-12.5). Headers are written on response start rather than after
/// <c>next()</c>, because the response may already be streaming by then.</para>
///
/// <para><b>The CSP ships report-only first</b> (AC-12.2). Next.js needs inline styles and its own hydration
/// payload, so an enforcing policy would risk a visually broken screen for a clinic rather than a console
/// warning. Flipping it to enforcing is a deliberate follow-up step once the page walk is clean (AC-12.4).</para>
///
/// <para><b>HSTS is Cloud-only by default</b> (AC-12.7). The Local build uses a self-generated CA, and HSTS on
/// a device that never imported it converts a bypassable certificate warning into a permanent hard failure —
/// so in Local it must be opted into explicitly, and only after every device trusts the CA.</para>
/// </summary>
public class SecurityHeadersMiddleware
{
    /// <summary>
    /// Report-only policy. <c>'unsafe-inline'</c> for styles is required by Next; <c>blob:</c> covers the
    /// client-side docx/file-saver exports; <c>object-src</c>/<c>frame-src 'self'</c> cover the inline PDF the
    /// document preview returns. Report-only, so a miss here is a console entry rather than a broken screen.
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

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        // Cloud: on. Local: opt-in only, because of the self-signed CA interaction described above.
        _hstsEnabled = LocalAuthConfig.IsLocalMode(configuration)
            ? configuration.GetValue("Security:EnableHsts", false)
            : true;
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
                headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicy;
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
