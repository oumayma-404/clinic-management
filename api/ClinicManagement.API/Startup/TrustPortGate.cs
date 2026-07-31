using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Makes "the trust port serves <b>only</b> the trust page" a property of the request pipeline (P8, risk R-11).
///
/// <para><b>Why this type has to exist.</b> The trust page must be reachable over <i>plain HTTP</i> — a phone
/// that does not trust the server's certificate yet cannot be asked to fetch the fix over that certificate. So
/// P8 binds a third Kestrel listener with <c>ListenAnyIP(trustPort)</c> and no <c>UseHttps</c>. But a Kestrel
/// listener is not scoped to a subset of routes: <b>every</b> endpoint the app maps answers on <b>every</b> port
/// it binds. Adding that listener and stopping there would therefore publish the entire cleartext API on the
/// LAN — including <c>POST /api/auth/login</c> — which is precisely the exposure that keeps
/// <c>Hosting:HttpPort</c> (5000) on <c>ListenLocalhost</c> (Phase 4, Finding 2). The plan's own R-11 states the
/// requirement ("trust port serves only the Local-only trust controller"); this is the mechanism that delivers
/// it, and without it P8 would have reopened Finding 2 under a different port number.</para>
///
/// <para>The restriction is <b>one-way on purpose</b>: the trust paths stay reachable on the HTTPS front door
/// too. They serve only public material (a CA's public certificate, install instructions, a QR of an address),
/// and an operator sitting at the server PC should be able to open the page over the normal front door. What
/// must never happen is the reverse — anything <i>other</i> than those paths answering on the cleartext port.</para>
///
/// <para>Kept as a static predicate over primitives so it is unit-testable without a host: the interesting
/// cases are boundary ones (a path that merely starts with the same letters, a request on the HTTPS port, the
/// feature switched off) and each is a plain assertion.</para>
/// </summary>
public static class TrustPortGate
{
    /// <summary>
    /// The route prefix the trust page and its assets live under. It is inside <c>/api</c> because the Local
    /// front door's YARP catch-all forwards everything outside <c>/api</c> to the Next server — a
    /// <c>/trust</c> route would be proxied to a web app that knows nothing about it.
    /// </summary>
    public const string TrustPathPrefix = "/api/trust";

    /// <summary>
    /// The trust port when <c>Hosting:TrustPort</c> is not set. Defined once and read by <b>both</b> the
    /// Kestrel bind and the page that prints/encodes its own address — if those two disagreed the QR would
    /// advertise a port nothing listens on, and the failure would look like a broken phone rather than a
    /// configuration mismatch. Set the key to <c>0</c> to switch the trust page off entirely.
    /// </summary>
    public const int DefaultPort = 5080;

    /// <summary>
    /// True when the request must be refused outright: it arrived on the cleartext trust port and asked for
    /// something that is not the trust page.
    /// </summary>
    /// <param name="localPort">The local port the connection was accepted on.</param>
    /// <param name="trustPort">The configured trust port; <c>0</c> or less means the feature is off.</param>
    /// <param name="path">The request path.</param>
    public static bool ShouldRefuse(int localPort, int trustPort, PathString path)
    {
        if (trustPort <= 0 || localPort != trustPort)
        {
            return false;
        }

        // StartsWithSegments, not StartsWith: a hypothetical "/api/trusted-devices" shares the prefix as text
        // but is a different endpoint, and letting it answer here would be the whole hole reopened by typo.
        return !path.StartsWithSegments(TrustPathPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
