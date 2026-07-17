namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Pure computation of a single reminder send time from tiered lead times (see the spec's "Reminder
/// scheduling" rule). No I/O — kept separate so it is trivially unit-testable.
/// </summary>
public static class ReminderSchedule
{
    /// <summary>
    /// Computes when a reminder should be sent for an appointment, in UTC:
    /// <list type="number">
    /// <item>the largest lead tier <c>T</c> where <c>appointment − T &gt; now</c> → send at <c>appointment − T</c>;</item>
    /// <item>else if <c>appointment − minLead &gt; now</c> → send promptly (returns <paramref name="nowUtc"/>);</item>
    /// <item>else (within the min-lead window, or in the past) → no reminder (<c>null</c>).</item>
    /// </list>
    /// </summary>
    public static DateTime? ComputeSendTimeUtc(
        DateTime appointmentUtc, DateTime nowUtc, IReadOnlyList<int> leadTimesHours, int minLeadHours)
    {
        foreach (var hours in leadTimesHours.OrderByDescending(h => h))
        {
            var candidate = appointmentUtc - TimeSpan.FromHours(hours);
            if (candidate > nowUtc)
            {
                return candidate;
            }
        }

        if (appointmentUtc - TimeSpan.FromHours(minLeadHours) > nowUtc)
        {
            // Closer than the smallest tier but still outside the min-lead window → send on the next tick.
            return nowUtc;
        }

        return null;
    }
}
