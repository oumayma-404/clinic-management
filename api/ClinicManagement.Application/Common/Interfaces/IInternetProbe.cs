namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Probes whether the <b>server</b> itself has working internet egress. Implemented in Infrastructure.
/// Backs the Local (offline-LAN) connectivity signal: the .NET server — not the browser — makes the
/// outbound AI + Google Calendar calls, so "internet reachable" must reflect the server's egress.
/// </summary>
public interface IInternetProbe
{
    /// <summary>
    /// True when the server can reach the configured probe URL. The result is cached for a short TTL,
    /// so a burst of polling clients collapses into a single outbound probe per window.
    /// </summary>
    Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken = default);
}
