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
/// must not be influenced by anything a client can send. That is also why the codebase resolves the
/// rate-limiting client address through <see cref="ClientIp"/> instead of <c>UseForwardedHeaders</c>, which
/// would overwrite <c>RemoteIpAddress</c> and make this gate depend on header trust.</para>
/// </summary>
public static class LocalRequest
{
    /// <summary>True when the request originates from the server machine itself (loopback).</summary>
    public static bool IsLoopback(HttpContext context)
    {
        var connection = context.Connection;
        var remoteIp = connection.RemoteIpAddress;
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
