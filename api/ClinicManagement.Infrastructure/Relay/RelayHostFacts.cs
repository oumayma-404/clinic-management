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
    /// <summary>This PC's IPv4 LAN addresses, up to eight.</summary>
    public static IReadOnlyList<string> LanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Select(a => a.ToString())
            .Distinct()
            .Take(8)
            .ToList();

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
