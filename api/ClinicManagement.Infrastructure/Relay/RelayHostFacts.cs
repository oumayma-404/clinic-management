using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>What a PC de secours reports about the machine it runs on — read by pairing and by every heartbeat alike.</summary>
public static class RelayHostFacts
{
    /// <summary>This PC's IPv4 addresses on the cabinet's network, up to eight.</summary>
    public static IReadOnlyList<string> LanAddresses() =>
        PreferNetworkAddresses(NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n =>
            {
                var ip = n.GetIPProperties();
                return new HostAdapter(
                    ip.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                 && !g.Address.Equals(IPAddress.Any)),
                    ip.UnicastAddresses.Select(a => a.Address)
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        .Select(a => a.ToString())
                        .ToList());
            }));

    /// <summary>
    /// The cabinet's box as this PC sees it: the first IPv4 gateway of an adapter that is up (AC-6.2). A device counts
    /// toward unlocking the cloud only when its own gateway is this one. Null with no cable and no Wi-Fi.
    /// </summary>
    public static string? GatewayAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses.Select(g => g.Address))
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
            ?.ToString();

    /// <summary>One network adapter as the address choice sees it.</summary>
    public sealed record HostAdapter(bool HasGateway, IReadOnlyList<string> Ipv4Addresses);

    /// <summary>
    /// The addresses of adapters that reach a gateway — the cabinet's real network — before any other. A Hyper-V or
    /// WSL switch has no gateway, and its address is one no phone or PC in the cabinet can reach (found on the first
    /// end-to-end run, where it came first). Adapters with no gateway are kept only when there is nothing else.
    /// </summary>
    public static IReadOnlyList<string> PreferNetworkAddresses(IEnumerable<HostAdapter> adapters)
    {
        var list = adapters.ToList();
        var withGateway = list.Where(a => a.HasGateway).SelectMany(a => a.Ipv4Addresses).Distinct().ToList();
        var chosen = withGateway.Count > 0 ? withGateway : list.SelectMany(a => a.Ipv4Addresses).Distinct().ToList();
        return chosen.Take(8).ToList();
    }

    /// <summary>SHA-256 of this PC's server certificate, hex — what the clinic's devices will pin (D21). Null if unreadable.</summary>
    public static string? CertificateFingerprint(string? localDir = null)
    {
        try
        {
            var cert = new CertificateProvisioner(NullLogger<CertificateProvisioner>.Instance, localDir).EnsureServerCertificate();
            using var x509 = new X509Certificate2(cert.PfxPath, cert.Password, X509KeyStorageFlags.EphemeralKeySet);
            return Convert.ToHexString(SHA256.HashData(x509.RawData));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Free bytes on the drive holding <paramref name="path"/>, null when it cannot be read.</summary>
    public static long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
