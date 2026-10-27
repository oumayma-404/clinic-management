namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// « Stop calling the SMS provider until T. » Tripped when the provider refuses for a reason that is ours to fix and
/// will not clear by retrying the next row — no credit, a key or sender it does not accept, the daily quota — so one
/// tick does not spend fifty calls learning the same answer, and the next ticks wait instead of hammering.
///
/// <para>A singleton because the state is about the vendor's one account, not about a request; the sender that reads
/// it is scoped. While it is open the rows stay <c>Pending</c> (the sender answers <c>Throttled</c>): nothing is lost,
/// and the existing « appointment already passed » rule drains the ones that wait too long.</para>
/// </summary>
public sealed class SmsProviderBreaker
{
    private readonly object _gate = new();
    private DateTime _openUntilUtc = DateTime.MinValue;
    private string? _reason;

    /// <summary>True while sends must not be attempted; <paramref name="reason"/> says why.</summary>
    public bool IsOpen(DateTime nowUtc, out string? reason)
    {
        lock (_gate)
        {
            reason = _reason;
            return nowUtc < _openUntilUtc;
        }
    }

    /// <summary>Opens the breaker for <paramref name="duration"/> from <paramref name="nowUtc"/>, never shortening it.</summary>
    public void Trip(DateTime nowUtc, TimeSpan duration, string reason)
    {
        lock (_gate)
        {
            var until = nowUtc + duration;
            if (until > _openUntilUtc)
            {
                _openUntilUtc = until;
                _reason = reason;
            }
        }
    }
}
