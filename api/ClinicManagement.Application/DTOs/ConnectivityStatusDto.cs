namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Server-reported connectivity. Server reachability is implied by the poll getting any HTTP response; this
/// DTO carries the internet-egress bit the server probed.
/// </summary>
public class ConnectivityStatusDto
{
    /// <summary>
    /// Whether the server reached the internet. <b><c>null</c> means this deployment publishes no egress
    /// reading at all</b> — which is a different statement from <c>false</c>, and conflating the two is what
    /// AC-63 exists to prevent: an absent signal read as "offline" pinned a hosted clinic's Google controls
    /// off permanently. A datacentre has no offline story to tell, so it tells none.
    /// </summary>
    public bool? InternetReachable { get; set; }
}
