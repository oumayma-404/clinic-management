using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>Windows' « heures d'activité » for a PC de secours: local hours, <c>End</c> exclusive (0–23).</summary>
public readonly record struct RelayActiveHours(int Start, int End);

/// <summary>
/// <c>clinic-pc-copy</c> AC-3.11: Windows Update never restarts the PC de secours during opening hours. Windows promises
/// no automatic restart inside its active hours, so they are set to cover the cabinet's — the earliest opening to the
/// latest closing over the week, with an hour each side — within Windows' own limit of 18 hours.
/// </summary>
public static class RelayUpdateHours
{
    /// <summary>The longest active-hours range Windows accepts.</summary>
    public const int MaxSpanHours = 18;

    /// <summary>The cabinet's hours, padded and capped; null when it is never open (nothing to protect).</summary>
    public static RelayActiveHours? Window(string? clinicHoursJson)
    {
        var read = WorkingHoursSerializer.Read(clinicHoursJson);
        var days = read.State == WorkingHoursReadState.Valid ? read.Days : RelayAlertRules.DefaultHours;

        int? opens = null, closes = null;
        foreach (var day in days)
        {
            if (!day.Enabled
                || !WorkingHoursSerializer.TryParseTime(day.From, out var from)
                || !WorkingHoursSerializer.TryParseTime(day.To, out var to))
            {
                continue;
            }

            var open = from.Hours;
            var close = to.Minutes > 0 || to.Seconds > 0 ? to.Hours + 1 : to.Hours;
            opens = opens is null ? open : Math.Min(opens.Value, open);
            closes = closes is null ? close : Math.Max(closes.Value, close);
        }

        if (opens is null || closes is null || closes <= opens)
        {
            return null;
        }

        var start = Math.Max(0, opens.Value - 1);
        var end = Math.Min(24, closes.Value + 1);
        if (end - start > MaxSpanHours)
        {
            end = start + MaxSpanHours;
        }

        return new RelayActiveHours(start, end % 24);
    }
}
