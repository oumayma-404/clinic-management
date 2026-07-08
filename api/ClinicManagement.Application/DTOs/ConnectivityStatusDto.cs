namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Server-reported connectivity for the Local (offline-LAN) mode. Server reachability is implied by
/// the poll getting any HTTP response; this DTO carries the internet-egress bit the server probed.
/// </summary>
public class ConnectivityStatusDto
{
    public bool InternetReachable { get; set; }
}
