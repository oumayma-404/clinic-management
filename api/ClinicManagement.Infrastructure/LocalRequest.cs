using System.Net;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Infrastructure;

/// <summary>
/// Shared, unit-testable check for "did this request originate from the server machine itself"
/// (loopback). Extracted from <c>AuthController.IsLocalRequest</c> so the same logic can
/// gate both the first-run setup endpoint (AC-1.2a) and the Hangfire dashboard (FR-E3) — and be
/// exercised by <c>ClinicManagement.UnitTests</c>, which references Infrastructure but not the API.
///
/// <para><b>Deliberately reads the raw TCP peer, never a forwarded header.</b> This is a security gate, so it
/// must not be influenced by anything a client can send.</para>
///
/// <para>⚠️ <b>It reads <see cref="OriginalPeer"/> rather than <c>Connection.RemoteIpAddress</c> directly, and
/// since hosted-security-hardening Part 2 that difference is load-bearing.</b> That part registers
/// <c>UseForwardedHeaders</c> on the hosted kinds — bounded by <c>Security:TrustedProxies</c> — and that
/// middleware <i>overwrites</i> <c>RemoteIpAddress</c> with whatever the trusted hop forwarded. Reading the
/// field directly would therefore make this gate decidable by a header the moment the proxy bound was wrong
/// or widened, which is precisely what a loopback gate must not be (risk R-5). <see cref="OriginalPeer"/> is
/// captured before that substitution; where no substitution happens it is the same address, so nothing about
/// <c>SelfHostedLan</c> changes.</para>
/// </summary>
public static class LocalRequest
{
    /// <summary>True when the request originates from the server machine itself (loopback).</summary>
    public static bool IsLoopback(HttpContext context)
    {
        var connection = context.Connection;
        var remoteIp = OriginalPeer.Of(context);
        if (remoteIp is null)
        {
            // Fail CLOSED. A security gate must deny on missing or ambiguous information, not allow: this
            // one opens the first-run setup endpoint and the Hangfire dashboard. Kestrel over TCP always
            // populates the peer, so a null here means an unexpected hosting topology — exactly the case
            // where guessing "it must be local" is wrong. (Previously returned true.)
            return false;
        }
        if (connection.LocalIpAddress is not null)
        {
            return remoteIp.Equals(connection.LocalIpAddress) || IPAddress.IsLoopback(remoteIp);
        }
        return IPAddress.IsLoopback(remoteIp);
    }
}
