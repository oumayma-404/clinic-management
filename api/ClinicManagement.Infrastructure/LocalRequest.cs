using System.Net;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Infrastructure;

/// <summary>
/// Shared, unit-testable check for "did this request originate from the server machine itself"
/// (loopback). Extracted verbatim from <c>AuthController.IsLocalRequest</c> so the same logic can
/// gate both the first-run setup endpoint (AC-1.2a) and the Hangfire dashboard (FR-E3) — and be
/// exercised by <c>ClinicManagement.UnitTests</c>, which references Infrastructure but not the API.
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
            return true; // in-process / no remote info
        }
        if (connection.LocalIpAddress is not null)
        {
            return remoteIp.Equals(connection.LocalIpAddress) || IPAddress.IsLoopback(remoteIp);
        }
        return IPAddress.IsLoopback(remoteIp);
    }
}
