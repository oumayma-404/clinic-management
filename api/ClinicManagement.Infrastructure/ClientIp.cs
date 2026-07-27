using System.Net;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Infrastructure;

/// <summary>
/// Resolves the address to attribute a request to, for rate limiting and per-source login lockout
/// (security-hardening US-4).
///
/// <para><b>Why this exists.</b> In a Local install the browser does not reach the API directly:
/// <c>browser → Kestrel/YARP → Next → loopback → API</c>. The Next BFF posts the login server-side over
/// loopback, so without this every login arrives with <c>RemoteIpAddress == 127.0.0.1</c>. A per-IP limiter
/// keyed on that would bucket the <b>entire clinic as one source</b> — staff signing in at 08:00 would share
/// one budget and the limiter would become the clinic-wide lockout it exists to prevent.</para>
///
/// <para><b>Why not <c>UseForwardedHeaders</c>.</b> That middleware overwrites
/// <c>HttpContext.Connection.RemoteIpAddress</c>, which is what <see cref="LocalRequest.IsLoopback"/> reads to
/// gate the first-run <c>setup</c> endpoint and the Hangfire dashboard. Coupling those gates to
/// forwarded-header trust means a future topology change (a real reverse proxy, a widened
/// <c>KnownProxies</c>) silently turns them spoofable. Resolving the client address <i>separately</i> keeps
/// the loopback guarantee a property of the actual TCP peer — structural, not configuration-dependent.</para>
///
/// <para><b>Trust rule.</b> <c>X-Forwarded-For</c> is honoured <b>only</b> when the immediate peer is loopback
/// — i.e. our own front door or BFF. A LAN client's own header is therefore never trusted, so it cannot
/// impersonate another device to escape its rate-limit bucket, nor claim to be loopback.</para>
/// </summary>
public static class ClientIp
{
    /// <summary>The header the Kestrel front door (YARP) adds and the Next BFF passes through.</summary>
    public const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>
    /// Partition key used when no address can be determined at all. Such requests deliberately share one
    /// bucket rather than each getting an unlimited one — an unattributable request should be constrained,
    /// not exempt.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The address to attribute this request to. Returns the left-most <c>X-Forwarded-For</c> entry when the
    /// peer is loopback (our own hop), otherwise the real TCP peer.
    /// </summary>
    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var peer = context.Connection.RemoteIpAddress;

        if (peer is not null && IPAddress.IsLoopback(peer))
        {
            var forwarded = FirstForwardedEntry(context.Request.Headers[ForwardedForHeader].ToString());
            if (forwarded is not null)
            {
                return forwarded;
            }
        }

        return peer?.ToString() ?? Unknown;
    }

    /// <summary>
    /// The left-most entry of an <c>X-Forwarded-For</c> value — the original client. Everything to its right
    /// is an intermediate hop that appended itself. Returns <c>null</c> when the header is absent or holds
    /// nothing parseable, so the caller falls back to the peer rather than to a shared bucket.
    /// </summary>
    public static string? FirstForwardedEntry(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        foreach (var candidate in headerValue.Split(','))
        {
            var normalized = NormalizeEntry(candidate);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses one entry, tolerating the port forms proxies emit (<c>1.2.3.4:5678</c>, <c>[::1]:5678</c>).
    /// Anything that is not a valid address is rejected rather than used as a key — a garbage header must not
    /// be able to invent partitions.
    /// </summary>
    private static string? NormalizeEntry(string entry)
    {
        entry = entry.Trim();
        if (entry.Length == 0)
        {
            return null;
        }

        // Bare address (covers IPv4 and unbracketed IPv6, which contains multiple colons).
        if (IPAddress.TryParse(entry, out var direct))
        {
            return direct.ToString();
        }

        // Bracketed IPv6, optionally with a port: [::1] / [::1]:5678
        if (entry[0] == '[')
        {
            var close = entry.IndexOf(']');
            return close > 1 && IPAddress.TryParse(entry[1..close], out var bracketed)
                ? bracketed.ToString()
                : null;
        }

        // IPv4 with a port: exactly one colon (more than one would be a bare IPv6, handled above).
        var colon = entry.IndexOf(':');
        if (colon > 0 && entry.LastIndexOf(':') == colon &&
            IPAddress.TryParse(entry[..colon], out var withPort))
        {
            return withPort.ToString();
        }

        return null;
    }
}
