using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>What <c>.local/relay-lease.json</c> holds: the last ack this PC received, and whether it holds the saves.</summary>
public sealed record RelayLeaseState
{
    /// <summary>The id of the last ack received — echoed on every heartbeat, which is how the cloud's clock moves (D14).</summary>
    public long LastAckSeq { get; init; }

    /// <summary>Whether that ack said « armé »: the PC may take over after a cut only if it did (AC-3.8).</summary>
    public bool LastAckArmed { get; init; }

    /// <summary>When that ack arrived, on this PC's wall clock (used only across a restart, capped by uptime).</summary>
    public DateTime? LastAckReceivedAtUtc { get; init; }

    /// <summary>Since when this PC holds the cabinet's saves (D13); null while it follows the cloud.</summary>
    public DateTime? HoldingSinceUtc { get; init; }

    /// <summary>The ack the takeover happened under — the last word the cloud had sent.</summary>
    public long HoldingUnderAckSeq { get; init; }

    /// <summary>
    /// Since when this PC keeps work saved during a cut that never reached the cloud — set when the cloud took the
    /// cabinet back while this PC held it (AC-8.6). Cleared only by the return; until then nothing may erase this copy.
    /// </summary>
    public DateTime? UnreturnedSinceUtc { get; init; }
}

/// <summary>
/// The PC de secours's half of the write lease (<c>clinic-pc-copy</c> D13, D14): the last ack it received, and whether
/// it holds the cabinet's saves — in memory for the gate and the copy loop, on disk so a restart during a cut resumes in
/// charge (AC-3.11).
///
/// <para>⚠️ <b>Not in <c>relay-state.json</c>.</b> The copy loop rewrites that file from the copy it loaded at the start
/// of its tick, and the lease moves <i>during</i> a tick (a pulse, a takeover): saved there, it would be put back by the
/// tick's next save — and a PC that forgets it holds the cabinet's work would follow the cloud again over it. Here every
/// change is made under one lock, from memory, and written before it is true.</para>
///
/// <para>⚠️ An unreadable file reads as <b>holding</b>: forgetting would let the copy loop overwrite the cut's work,
/// while holding by mistake only keeps the cloud read-only until somebody looks.</para>
///
/// <para>Every heartbeat goes through <see cref="ExchangeAsync"/> — one at a time (the cloud refuses two concurrent
/// saves of one PC's row), bounded by <see cref="ExchangeTimeout"/>, and recorded: an ack resets the takeover clock on
/// the monotonic clock, and an exchange that brought none is what <see cref="RelayLeaseKeeper"/> needs before it may
/// conclude the cloud is gone rather than that this PC was busy.</para>
/// </summary>
public sealed class RelayLease
{
    public const string FileName = "relay-lease.json";

    /// <summary>A heartbeat is a few hundred bytes: one not answered in this long is a cloud that is not answering.</summary>
    public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A copy tick busy longer than this (a big radiograph, a re-seed) gets a heartbeat beside it.</summary>
    public static readonly TimeSpan PulseAfter = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan> _monotonic;
    private readonly TimeSpan _exchangeTimeout;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _exchange = new(1, 1);
    private RelayLeaseState _state;
    private TimeSpan? _lastAckAt;
    private TimeSpan? _lastExchangeAt;
    private bool _unansweredSinceAck;

    public RelayLease(
        string? localDir = null, Func<DateTime>? utcNow = null, Func<TimeSpan>? monotonic = null, TimeSpan? exchangeTimeout = null)
    {
        _path = Path.Combine(localDir ?? LocalInstallPaths.LocalDir, FileName);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _monotonic = monotonic ?? SinceProcessStart();
        _exchangeTimeout = exchangeTimeout ?? ExchangeTimeout;
        _state = Load(_path);
    }

    public RelayLeaseState Current
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsHolding => Current.HoldingSinceUtc is not null;

    public DateTime? HoldingSinceUtc => Current.HoldingSinceUtc;

    /// <summary>
    /// This copy holds work the cloud never received — while holding, or after the cloud took the cabinet back. Erasing
    /// it or pairing over it would lose that work for good, so both refuse (AC-7.3: it is « À reprendre »).
    /// </summary>
    public bool HoldsUnreturnedWork => Current is { } state && (state.HoldingSinceUtc is not null || state.UnreturnedSinceUtc is not null);

    /// <summary>An exchange since the last ack brought no ack — the cloud was asked and did not answer.</summary>
    public bool UnansweredSinceLastAck
    {
        get
        {
            lock (_gate)
            {
                return _unansweredSinceAck;
            }
        }
    }

    /// <summary>How long since an exchange last started; null before the first.</summary>
    public TimeSpan? SinceLastExchange
    {
        get
        {
            lock (_gate)
            {
                return _lastExchangeAt is { } at ? _monotonic() - at : null;
            }
        }
    }

    /// <summary>How long since the last ack arrived, never over-counted (<see cref="ClinicWriteLease.SinceLastAckReceived"/>).</summary>
    public TimeSpan SinceLastAckReceived()
    {
        lock (_gate)
        {
            var now = _monotonic();
            return ClinicWriteLease.SinceLastAckReceived(
                _utcNow(), _state.LastAckReceivedAtUtc, _lastAckAt is { } at ? now - at : null, now);
        }
    }

    /// <summary>One heartbeat, alone and bounded; its ack (or the lack of one) is recorded before the caller sees it.</summary>
    public async Task<RelayCall<RelayHeartbeatAck>> ExchangeAsync(
        Func<CancellationToken, Task<RelayCall<RelayHeartbeatAck>>> heartbeat, CancellationToken cancellationToken)
    {
        await _exchange.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                _lastExchangeAt = _monotonic();
            }

            RelayCall<RelayHeartbeatAck> call;
            using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                bounded.CancelAfter(_exchangeTimeout);
                try
                {
                    call = await heartbeat(bounded.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    call = new RelayCall<RelayHeartbeatAck>(RelayCallStatus.Unreachable, null, "Le cloud n'a pas répondu à temps.");
                }
            }

            if (call.IsOk)
            {
                if (call.Value!.AckSeq > 0)
                {
                    Received(call.Value.AckSeq, call.Value.Armed);
                }
            }
            else if (call.Status != RelayCallStatus.Released)
            {
                lock (_gate)
                {
                    _unansweredSinceAck = true;
                }
            }

            return call;
        }
        finally
        {
            _exchange.Release();
        }
    }

    /// <summary>Takes the cabinet's saves. Written to disk before it is true in memory, so no write is accepted unrecorded.</summary>
    public void TakeOver()
    {
        lock (_gate)
        {
            if (_state.HoldingSinceUtc is not null)
            {
                return;
            }

            Write(_state with { HoldingSinceUtc = _utcNow(), HoldingUnderAckSeq = _state.LastAckSeq });
        }
    }

    /// <summary>
    /// Stops accepting the cabinet's saves because the cloud took it back (retired, lost, promoted) — the work saved here
    /// meanwhile is kept, and marked as never returned. The return proper (D18) arrives in a later slice.
    /// </summary>
    public void End()
    {
        lock (_gate)
        {
            if (_state.HoldingSinceUtc is not { } since)
            {
                return;
            }

            Write(_state with
            {
                HoldingSinceUtc = null,
                HoldingUnderAckSeq = 0,
                UnreturnedSinceUtc = _state.UnreturnedSinceUtc ?? since,
            });
        }
    }

    /// <summary>A new pairing starts unarmed, under no ack. Refused while this copy holds work the cloud never received.</summary>
    public void ResetForNewPairing()
    {
        lock (_gate)
        {
            if (_state.HoldingSinceUtc is not null || _state.UnreturnedSinceUtc is not null)
            {
                throw new InvalidOperationException(RelayRefusals.CutWorkKept);
            }

            Write(new RelayLeaseState());
            _lastAckAt = null;
            _unansweredSinceAck = false;
        }
    }

    private void Received(long seq, bool armed)
    {
        lock (_gate)
        {
            Write(_state with { LastAckSeq = seq, LastAckArmed = armed, LastAckReceivedAtUtc = _utcNow() });
            _lastAckAt = _monotonic();
            _unansweredSinceAck = false;
        }
    }

    private void Write(RelayLeaseState next)
    {
        AtomicFile.Write(_path, JsonSerializer.Serialize(next, Json));
        _state = next;
    }

    private static Func<TimeSpan> SinceProcessStart()
    {
        var start = Stopwatch.GetTimestamp();
        return () => Stopwatch.GetElapsedTime(start);
    }

    private static RelayLeaseState Load(string path)
    {
        if (!File.Exists(path))
        {
            return new RelayLeaseState();
        }

        try
        {
            return JsonSerializer.Deserialize<RelayLeaseState>(File.ReadAllText(path), Json) ?? Unreadable();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Unreadable();
        }

        static RelayLeaseState Unreadable() => new() { HoldingSinceUtc = DateTime.UnixEpoch };
    }
}

/// <summary>Whether the cabinet's internet box answers this PC (AC-6.6): a PC cut off from it must never take over.</summary>
public interface IRelayBoxProbe
{
    Task<bool> AnswersAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The box is the default gateway of an adapter that is up. It answers when it replies to a ping, or when a TCP
/// connection to it is accepted or actively refused — a refusal is an answer too, and some boxes drop pings. No gateway
/// at all (cable out, Wi-Fi lost) is « no ».
/// </summary>
public sealed class GatewayBoxProbe : IRelayBoxProbe
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly int[] Ports = { 80, 443, 53 };

    private readonly ILogger _logger;

    public GatewayBoxProbe(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> AnswersAsync(CancellationToken cancellationToken)
    {
        var gateways = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses.Select(g => g.Address))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
            .Distinct()
            .ToList();

        foreach (var gateway in gateways)
        {
            if (await PingsAsync(gateway) || await AcceptsOrRefusesAsync(gateway, cancellationToken))
            {
                return true;
            }
        }

        _logger.LogWarning("PC de secours: the cabinet's box does not answer ({Count} gateway(s)).", gateways.Count);
        return false;
    }

    private static async Task<bool> PingsAsync(IPAddress gateway)
    {
        try
        {
            using var ping = new Ping();
            return (await ping.SendPingAsync(gateway, (int)PingTimeout.TotalMilliseconds)).Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    private static async Task<bool> AcceptsOrRefusesAsync(IPAddress gateway, CancellationToken cancellationToken)
    {
        foreach (var port in Ports)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                await client.ConnectAsync(gateway, port, timeout.Token);
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// The PC's takeover decision, every few seconds (<c>clinic-pc-copy</c> FR-3, D13). It takes the cabinet's saves when
/// all of these hold: the copy is seeded and not stopped, the last ack said « armé », the cloud was asked since and did
/// not answer, that ack is <see cref="ClinicWriteLease.PcTakesOverAfter"/> old on this PC's clock, and the cabinet's
/// box still answers.
///
/// <para>⚠️ « Asked and not answered » is the condition a clock alone cannot give: a PC that slept, or whose copy tick
/// was busy, has an old ack without the cloud being gone — and the first heartbeat after it wakes would have been
/// answered. It only ever makes the takeover later, never earlier, so the cloud has always fenced first.</para>
/// </summary>
public sealed class RelayLeaseKeeper
{
    private readonly RelayLease _lease;
    private readonly RelayFollowerStateStore _states;
    private readonly IRelayBoxProbe _box;
    private readonly ILogger _logger;

    public RelayLeaseKeeper(RelayLease lease, RelayFollowerStateStore states, IRelayBoxProbe box, ILogger logger)
    {
        _lease = lease;
        _states = states;
        _box = box;
        _logger = logger;
    }

    /// <summary>One decision; true when this PC took the cabinet's saves just now.</summary>
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var state = _states.Load();

        if (_lease.IsHolding)
        {
            // The cloud took the cabinet back while this PC held it (« Retirer », « perdu ou volé », a promotion): the
            // cut's work stays here, readable by the administrators, and this PC stops accepting saves. The return
            // proper arrives with D18.
            if (state.Released || state.ErasedAtUtc is not null)
            {
                _logger.LogWarning("PC de secours: released while holding the cabinet's saves; it stops accepting them.");
                _lease.End();
            }

            return false;
        }

        var ack = _lease.Current;
        if (state.Released || state.ErasedAtUtc is not null || !state.RowsSeeded || state.StoppedReason is not null
            || !ack.LastAckArmed || ack.LastAckReceivedAtUtc is null || !_lease.UnansweredSinceLastAck)
        {
            return false;
        }

        var since = _lease.SinceLastAckReceived();
        if (since < ClinicWriteLease.PcTakesOverAfter
            || !ClinicWriteLease.PcMayTakeOver(since, ack.LastAckArmed, await _box.AnswersAsync(cancellationToken)))
        {
            return false;
        }

        _lease.TakeOver();
        _logger.LogWarning("PC de secours: no answer from the cloud for {Seconds} s — this PC now holds the cabinet's saves.",
            (int)since.TotalSeconds);
        return true;
    }
}
