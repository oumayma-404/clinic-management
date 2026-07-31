using System.Net;
using System.Net.Sockets;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// The machine's own non-loopback IPv4 addresses — the single authority for "which addresses is this server
/// reachable at on the LAN?" (P8, AC-44/AC-45).
///
/// It exists as a shared type rather than a private helper because <b>two</b> things have to agree about that
/// answer, and they fail silently when they don't:
/// <list type="bullet">
///   <item><see cref="CertificateProvisioner"/> writes them into the server leaf's <c>subjectAltName</c>, and</item>
///   <item>the trust page advertises one of them (in its QR and its instructions) as the address a phone
///         should use.</item>
/// </list>
/// If those two sets diverge, a device installs the CA successfully and <i>still</i> gets a certificate error,
/// because the address it was told to use is not one the certificate claims. That is the same class of defect
/// as any duplicated calculation: not wrong on the day it is written, wrong the first time one copy moves.
///
/// ⚠️ <b>The answer is captured at a moment in time, not watched.</b> The certificate is minted once and then
/// reused idempotently, so an address obtained here at generation time is frozen into the leaf. If DHCP later
/// hands the server a different lease, the new address is <i>not</i> in the SAN set and HTTPS fails on every
/// device even though the CA is correctly installed. That is a documented failure state
/// (see <c>packaging/README.md</c> § « Le serveur a changé d'adresse ») whose fix is a static/reserved lease,
/// not a code change here.
///
/// ⚠️ <b>IPv4 only, and no mDNS.</b> There is no IPv6 SAN and no <c>.local</c> name, so a device must reach the
/// server by IPv4 literal or by a hostname the local network already resolves.
/// </summary>
public static class LanAddresses
{
    /// <summary>
    /// The non-loopback IPv4 addresses of this machine, deduplicated. Empty when the host's addresses cannot
    /// be resolved — callers treat that as "nothing to advertise", never as an error, because a server with no
    /// LAN address is a legitimate state (an unplugged offline PC still serves <c>localhost</c>).
    /// </summary>
    public static IReadOnlyList<IPAddress> IPv4()
    {
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(Dns.GetHostName());
        }
        catch (SocketException)
        {
            return Array.Empty<IPAddress>();
        }

        return addresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Distinct()
            .ToList();
    }
}
