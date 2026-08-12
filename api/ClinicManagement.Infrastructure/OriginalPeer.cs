using System.Net;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Infrastructure;

/// <summary>
/// The <b>real TCP peer</b> of a request, captured before anything is allowed to substitute it
/// (hosted-security-hardening Part 2, FR-2.4, risk R-5).
///
/// <para><b>Why this exists.</b> Part 2 registers <c>UseForwardedHeaders</c> on the hosted kinds, so the
/// reverse proxy's <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are honoured — which is what finally
/// gives the rate limiter, the per-account lockout and the OAuth state cookie's <c>Secure</c> flag the truth
/// about the caller. But that middleware <b>overwrites <c>Connection.RemoteIpAddress</c></b>, and that field is
/// what <see cref="LocalRequest.IsLoopback"/> reads to gate the first-run <c>setup</c> endpoint and the
/// Hangfire dashboard. Those two gates must remain a property of the actual TCP peer: an address a header can
/// claim is not a security boundary, whatever the trusted-proxy bound says.</para>
///
/// <para><b>So the peer is captured first and the gates read the capture</b>, while everything that legitimately
/// wants the client's own address keeps reading the substituted one through <see cref="ClientIp"/>. The two
/// deliberately answer differently, and pointing both at one value would either re-collapse every clinic into
/// one rate-limit bucket or make the loopback gates forgeable.</para>
///
/// <para>⚠️ <b>The capture middleware must be registered BEFORE <c>UseForwardedHeaders</c></b>, which is an
/// ordering obligation and therefore invisible to every behavioural test — so it is asserted against
/// <c>Program.cs</c>'s own source by <c>OriginalPeerTests</c>, on the precedent of
/// <c>SubscriptionGateMiddlewareTests</c> and <c>AccountStateEnforcementTests</c>.</para>
///
/// <para>⚠️ <see cref="Of"/> falls back to the live peer when nothing was captured — a hub invocation, a
/// profile where the capture is not registered, a hand-built <c>DefaultHttpContext</c> in a test. That
/// reproduces the pre-Part-2 behaviour exactly and never <i>invents</i> loopback: where no substitution
/// happens, the live peer IS the original peer.</para>
/// </summary>
public static class OriginalPeer
{
    /// <summary><c>HttpContext.Items</c> key holding the captured peer.</summary>
    public const string ItemsKey = "ClinicManagement.OriginalPeer";

    /// <summary>
    /// Records the current <c>Connection.RemoteIpAddress</c>. Idempotent and first-writer-wins: a second call
    /// after a substitution must not overwrite the truth with the substituted value.
    /// </summary>
    public static void Capture(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Items.ContainsKey(ItemsKey))
        {
            context.Items[ItemsKey] = context.Connection.RemoteIpAddress;
        }
    }

    /// <summary>
    /// The peer that actually opened the connection: the captured value when there is one, otherwise the live
    /// one. <c>null</c> only when the peer is genuinely unknown, which every caller must treat as "not local".
    /// </summary>
    public static IPAddress? Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(ItemsKey, out var captured)
            ? captured as IPAddress
            : context.Connection.RemoteIpAddress;
    }
}
