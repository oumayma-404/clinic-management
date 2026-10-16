using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The PC de secours answers the cabinet's apps on the LAN (<c>clinic-pc-copy</c> D21 / FR-7): « who is the PC de secours
/// of relay X? » → its addresses, HTTPS port and certificate fingerprint. The rules — who is answered and with what — are
/// <see cref="RelayDiscovery"/>; this only listens, reads what the PC knows about itself, and sends.
///
/// <para>⚠️ <b>Never fatal.</b> A port already taken, or a socket error, stops discovery only: apps still reach the PC
/// at the address the cloud last reported. ⚠️ One answer per asking address every two seconds — an answer is ~200
/// bytes, so a flood of requests cannot turn this PC into an amplifier on the cabinet's network.</para>
/// </summary>
public sealed class RelayDiscoveryResponder : BackgroundService
{
    private static readonly TimeSpan FactsLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PerSourceInterval = TimeSpan.FromSeconds(2);

    private readonly IDataProtectionProvider _protection;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RelayDiscoveryResponder> _logger;
    private readonly ConcurrentDictionary<IPAddress, DateTime> _lastAnswer = new();

    private Facts? _facts;

    public RelayDiscoveryResponder(
        IDataProtectionProvider protection, IConfiguration configuration, ILogger<RelayDiscoveryResponder> logger)
    {
        _protection = protection;
        _configuration = configuration;
        _logger = logger;
    }

    private sealed record Facts(DateTime ReadAtUtc, Guid? RelayId, int HttpsPort, string? Fingerprint, IReadOnlyList<string> Addresses);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = _configuration.GetValue<int?>("Relay:DiscoveryPort") ?? RelayDiscovery.DefaultPort;
        if (port <= 0)
        {
            return;
        }

        UdpClient udp;
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Découverte du PC de secours indisponible : le port UDP {Port} n'a pas pu être ouvert.", port);
            return;
        }

        using (udp)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(stoppingToken);
                    var answer = AnswerFor(received.Buffer, received.RemoteEndPoint.Address);
                    if (answer is not null)
                    {
                        await udp.SendAsync(answer, received.RemoteEndPoint, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    // A peer's ICMP « port unreachable » surfaces here on Windows; the next datagram is unaffected.
                    _logger.LogDebug(ex, "Relay discovery: socket error, carrying on.");
                }
            }
        }
    }

    private byte[]? AnswerFor(byte[] datagram, IPAddress from)
    {
        var asked = RelayDiscovery.ParseRequest(datagram);
        if (asked is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (_lastAnswer.TryGetValue(from, out var last) && now - last < PerSourceInterval)
        {
            return null;
        }

        var facts = CurrentFacts(now);
        var answer = RelayDiscovery.Answer(asked, facts.RelayId, from, facts.HttpsPort, facts.Fingerprint, facts.Addresses);
        if (answer is not null)
        {
            _lastAnswer[from] = now;
            if (_lastAnswer.Count > 256)
            {
                _lastAnswer.Clear();
            }
        }

        return answer;
    }

    /// <summary>Who this PC is and where it answers — read at most once a minute (the certificate and the pairing can change).</summary>
    private Facts CurrentFacts(DateTime now)
    {
        var facts = _facts;
        if (facts is not null && now - facts.ReadAtUtc < FactsLifetime)
        {
            return facts;
        }

        facts = new Facts(
            now,
            new RelayCredentialStore(_protection).TryLoad()?.RelayId,
            _configuration.GetValue<int?>("Hosting:HttpsPort") ?? 5001,
            RelayHostFacts.CertificateFingerprint(),
            RelayHostFacts.LanAddresses());
        _facts = facts;
        return facts;
    }
}
