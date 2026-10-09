using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// <c>clinic-pc-copy</c> D21 / FR-7 — the cabinet's apps find the PC de secours again after its address changed (the box
/// restarted — the first thing people do when the internet drops). An app broadcasts « who is the PC de secours of relay
/// X? » on the LAN; the PC paired as X answers with its addresses, port and certificate fingerprint.
///
/// <para>⚠️ <b>The answer is never trusted on its own.</b> It says where to look; the app then reaches that address
/// through the certificate fingerprint it ALREADY holds (learned from the cloud), exactly as before — so a forged answer
/// leads nowhere. The PC answers only to the relay id it was paired as, only to a private address, and says nothing the
/// cloud does not already hand every signed-in device of the cabinet.</para>
/// </summary>
public static class RelayDiscovery
{
    /// <summary>The UDP port the PC listens on — the same number in both shells.</summary>
    public const int DefaultPort = 47950;

    public const string RequestPrefix = "APEXA-RELAY-DISCOVER/1 ";

    /// <summary>A request is « prefix + a GUID »; anything larger is not one.</summary>
    public const int MaxRequestBytes = 128;

    public static byte[] BuildRequest(Guid relayId) => Encoding.ASCII.GetBytes(RequestPrefix + relayId.ToString("D"));

    /// <summary>The relay id asked about, or null for anything that is not a well-formed request.</summary>
    public static Guid? ParseRequest(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length == 0 || datagram.Length > MaxRequestBytes)
        {
            return null;
        }

        string text;
        try
        {
            text = Encoding.ASCII.GetString(datagram);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return text.StartsWith(RequestPrefix, StringComparison.Ordinal)
               && Guid.TryParseExact(text[RequestPrefix.Length..].Trim(), "D", out var id)
            ? id
            : null;
    }

    /// <summary>
    /// The answer to send, or null when this PC must stay silent: not paired, asked about another relay, no certificate
    /// yet, or asked from an address outside the cabinet's private networks.
    /// </summary>
    public static byte[]? Answer(
        Guid? asked, Guid? pairedAs, IPAddress from, int httpsPort, string? fingerprint, IReadOnlyList<string> addresses)
    {
        if (asked is null || pairedAs is null || asked != pairedAs || string.IsNullOrWhiteSpace(fingerprint)
            || httpsPort is <= 0 or > 65535 || !IsPrivate(from))
        {
            return null;
        }

        var json = JsonSerializer.Serialize(new
        {
            v = 1,
            relayId = pairedAs.Value.ToString("D"),
            port = httpsPort,
            fingerprint,
            addresses = addresses.Take(8).ToArray(),
        });
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>RFC 1918, link-local, IPv6 ULA / link-local — the cabinet's own networks. Loopback counts (a test on the PC itself).</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
               && (address.IsIPv6LinkLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC);
    }
}
