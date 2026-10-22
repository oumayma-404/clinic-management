using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// <c>clinic-pc-copy</c> D21 / FR-7 — the PC de secours found again after the box gave it a new address. The shell
/// broadcasts « who is the PC de secours of relay X? » on the cabinet's network; the PC answers with where it is. The
/// server's half is <c>Infrastructure/Relay/RelayDiscovery.cs</c> — the request bytes, the port and the answer's
/// fields are the same there.
///
/// <para>⚠️ <b>An answer is a hint, never trust.</b> Only the addresses are taken from it: the port and the certificate
/// must be the ones this app already holds (learned from the cloud), and the new address is accepted only once a pinned
/// request reaches the PC through that certificate. A forged answer therefore leads nowhere.</para>
/// </summary>
public static class RelayDiscovery
{
    public const int Port = 47950;
    public const string RequestPrefix = "APEXA-RELAY-DISCOVER/1 ";
    private static readonly TimeSpan ListenFor = TimeSpan.FromSeconds(1.5);

    public static byte[] BuildRequest(Guid relayId) => Encoding.ASCII.GetBytes(RequestPrefix + relayId.ToString("D"));

    /// <summary>
    /// The addresses an answer offers for <paramref name="known"/> — only when it names the same relay, the same port
    /// and the same certificate, and only private ones. Empty for anything else.
    /// </summary>
    public static IReadOnlyList<string> AddressesFrom(string json, RelaySwitch.Target known)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var relayId = root.TryGetProperty("relayId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            var port = root.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
            var fingerprint = root.TryGetProperty("fingerprint", out var f) && f.ValueKind == JsonValueKind.String
                ? RelayProbe.NormalizeFingerprint(f.GetString())
                : null;
            if (known.RelayId is not { } id || !Guid.TryParse(relayId, out var answered) || answered != id
                || port != known.Port || fingerprint != known.Fingerprint
                || !root.TryGetProperty("addresses", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return list.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => a.GetString()!)
                .Where(a => IPAddress.TryParse(a, out var ip) && RelayProbe.IsPrivate(ip))
                .Distinct()
                .Take(8)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Broadcasts on every network this PC is on, collects the answers, and returns the PC at the first address that
    /// answers through its pinned certificate — or null. Never throws.
    /// </summary>
    public static async Task<RelaySwitch.Target?> FindAsync(RelaySwitch.Target known)
    {
        if (known.RelayId is not { } relayId)
        {
            return null;
        }

        var candidates = new List<string>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var request = BuildRequest(relayId);
            foreach (var destination in BroadcastAddresses())
            {
                try
                {
                    await udp.SendAsync(request, request.Length, new IPEndPoint(destination, Port));
                }
                catch (SocketException)
                {
                    // One network refusing a broadcast does not stop the others.
                }
            }

            using var timeout = new CancellationTokenSource(ListenFor);
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(timeout.Token);
                    candidates.AddRange(AddressesFrom(Encoding.UTF8.GetString(received.Buffer), known));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    // An ICMP « unreachable » from one network; keep listening for the others.
                }
            }
        }
        catch (SocketException)
        {
            return null;
        }

        foreach (var address in candidates.Distinct())
        {
            var target = known with { Address = address };
            if (await RelaySwitch.HoldingAsync(target) is not null)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>The limited broadcast plus each up IPv4 network's directed broadcast (some boxes drop the first).</summary>
    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var found = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses
                             .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask is not null))
                {
                    var ip = unicast.Address.GetAddressBytes();
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (var i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    }

                    found.Add(new IPAddress(broadcast));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // The limited broadcast alone.
        }

        return found.Distinct();
    }
}
