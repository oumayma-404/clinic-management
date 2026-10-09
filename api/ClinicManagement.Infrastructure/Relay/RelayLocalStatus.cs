using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// <see cref="IRelayLocalStatus"/> from the PC's position file (<c>clinic-pc-copy</c> AC-8.1). Re-read at most every few
/// seconds — it is asked on every sign-in and every request, and the copy loop writes it every ten seconds anyway.
/// Always « not retired » on an install that is not a PC de secours, without touching the disk.
/// </summary>
public sealed class RelayLocalStatus : IRelayLocalStatus
{
    private static readonly TimeSpan ReReadAfter = TimeSpan.FromSeconds(5);

    private readonly bool _isRelay;
    private readonly RelayFollowerStateStore _store;
    private readonly RelayLease? _lease;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private RelayFollowerState? _state;
    private DateTime _readAtUtc;

    public RelayLocalStatus(DeploymentProfile profile, RelayLease? lease)
        : this(profile.MirrorsCloudClinic, new RelayFollowerStateStore(), () => DateTime.UtcNow, lease)
    {
    }

    public RelayLocalStatus(bool isRelay, RelayFollowerStateStore store, Func<DateTime> utcNow, RelayLease? lease = null)
    {
        _isRelay = isRelay;
        _store = store;
        _utcNow = utcNow;
        _lease = lease;
    }

    public bool IsRetired => Current()?.Released == true;

    public DateTime? RetiredAtUtc => Current() is { Released: true } state ? state.ReleasedAtUtc : null;

    /// <summary>The lease is in memory (one writer, under its own lock): no file is read on this, the gate's hot path.</summary>
    public bool IsHolding => _isRelay && _lease?.AcceptsSaves == true;

    public bool IsHandingBack => _isRelay && _lease?.IsHandingBack == true;

    public string? CutCause => _isRelay && _lease?.IsHolding == true ? _lease.Current.CutCause : null;

    private RelayFollowerState? Current()
    {
        if (!_isRelay)
        {
            return null;
        }

        lock (_gate)
        {
            var now = _utcNow();
            if (_state is null || now - _readAtUtc >= ReReadAfter)
            {
                _state = _store.Load();
                _readAtUtc = now;
            }

            return _state;
        }
    }
}
