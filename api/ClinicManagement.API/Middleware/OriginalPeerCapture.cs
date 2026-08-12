using ClinicManagement.Infrastructure;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Records the real TCP peer of every request (hosted-security-hardening Part 2, FR-2.4, risk R-5).
///
/// <para>⚠️ <b>It must be the FIRST middleware, and in any case before <c>UseForwardedHeaders</c></b>, which
/// overwrites <c>Connection.RemoteIpAddress</c>. After that point the original peer is unrecoverable, and the
/// two loopback-only gates — first-run <c>setup</c> and the Hangfire dashboard — would be decided by an address
/// a header can claim. See <see cref="OriginalPeer"/> for the whole argument.</para>
///
/// <para>Registered in <b>every</b> profile, not only where forwarded headers are honoured: where nothing
/// substitutes the peer the captured value is identical, so this costs one dictionary write and removes a
/// "which profiles is this true in?" question from every later reader of <c>LocalRequest</c>.</para>
/// </summary>
public static class OriginalPeerCapture
{
    public static IApplicationBuilder UseOriginalPeerCapture(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            OriginalPeer.Capture(context);
            await next();
        });
    }
}
