using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// <c>relayProbe()</c> (<c>clinic-pc-copy</c> AC-6.2, since 1.5): while the cloud is locked for a PC de secours that
/// said nothing, does THIS device reach it on the cabinet's network — and which box is this device behind? The cloud
/// counts the answer only from the cabinet's own line and box; a browser cannot answer at all (it cannot tell an
/// untrusted certificate from a PC that is down), which is why only the apps report.
///
/// <para>⚠️ <b>The PC is recognised by its certificate, never by its address</b> (D21): the box may have handed that
/// address to another machine, and anything answering there without the PC's exact certificate is « not the PC ».
/// ⚠️ Only private addresses are tried — the cloud names them, and the shell must not become a way to make a clinic's
/// PC call arbitrary hosts.</para>
/// </summary>
public static class RelayProbe
{
    public sealed record Request(IReadOnlyList<IPAddress> Addresses, int Port, string Fingerprint);

    public sealed record Answer(bool Reached, IReadOnlyList<string> Gateways);

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// The page's request, refused unless every part is what the cloud sends: up to eight private addresses, a port, and
    /// a SHA-256 fingerprint (64 hex). Null on anything else — the page then sends no report.
    /// </summary>
    public static Request? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("addresses", out var list) || list.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("port", out var portValue) || !portValue.TryGetInt32(out var port)
                || port is < 1 or > 65535
                || !root.TryGetProperty("fingerprint", out var fp) || fp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var fingerprint = NormalizeFingerprint(fp.GetString());
            var addresses = list.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => IPAddress.TryParse(a.GetString(), out var ip) ? ip : null)
                .OfType<IPAddress>()
                .Where(IsPrivate)
                .Distinct()
                .Take(8)
                .ToList();
            return fingerprint is null || addresses.Count == 0 ? null : new Request(addresses, port, fingerprint);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A LAN address: RFC 1918, link-local, or an IPv6 unique-local / link-local one. Never a public host.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return (first & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
        }

        return false;
    }

    public static string? NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var hex = new string(fingerprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 64 ? hex : null;
    }

    /// <summary>The certificate presented is the PC's own: SHA-256 of its DER bytes, as the PC reports it.</summary>
    public static bool IsThePcsCertificate(byte[]? rawData, string fingerprint) =>
        rawData is { Length: > 0 }
        && string.Equals(Convert.ToHexString(SHA256.HashData(rawData)), fingerprint, StringComparison.Ordinal);

    /// <summary>This device's default gateways — its box. The cloud compares them with the PC's.</summary>
    public static IReadOnlyList<string> Gateways()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses.Select(g => g.Address))
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
                .Select(a => a.ToString())
                .Distinct()
                .Take(8)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Never throws: a probe that cannot run reached nothing.</summary>
    public static async Task<Answer> RunAsync(Request request)
    {
        var attempts = request.Addresses.Select(address => ReachesAsync(address, request.Port, request.Fingerprint));
        var reached = (await Task.WhenAll(attempts)).Any(r => r);
        return new Answer(reached, Gateways());
    }

    /// <summary>
    /// Any HTTP answer over a TLS session with the PC's own certificate is the PC alive — an error status included, since
    /// « its server answers » is the fact that matters. A refused, timed-out or foreign-certificate attempt is not.
    /// </summary>
    private static async Task<bool> ReachesAsync(IPAddress address, int port, string fingerprint)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                    IsThePcsCertificate(certificate?.RawData, fingerprint),
                UseProxy = false,
                AllowAutoRedirect = false,
            };
            using var client = new HttpClient(handler) { Timeout = AttemptTimeout };
            var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
            using var cts = new CancellationTokenSource(AttemptTimeout);
            using var response = await client.GetAsync($"https://{host}:{port}/health", cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
