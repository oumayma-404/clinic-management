using ClinicManagement.Application.Common;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// One reminder to enqueue: which lead tier it speaks for, and when it goes out (UTC).
///
/// <para><see cref="LeadHours"/> is the tier's <b>identity</b>, not decoration — it is what makes « the 24 h
/// reminder » and « the 6 h reminder » two different things rather than two rows that happen to differ.
/// <see cref="PromptLeadHours"/> marks the fallback tier (« as soon as the dispatcher next runs »), which is not
/// a configured tier at all.</para>
/// </summary>
public readonly record struct ReminderSendTime(int LeadHours, DateTime SendAtUtc)
{
    /// <summary>The "send on the next tick" fallback — closer than the smallest tier, still outside min-lead.</summary>
    public const int PromptLeadHours = 0;
}

/// <summary>
/// Pure computation of the reminder send times for one appointment from tiered lead times, with a clinic-local
/// quiet-hours floor. No I/O — kept separate so it is trivially unit-testable.
/// </summary>
public static class ReminderSchedule
{
    /// <summary>
    /// Every tier that should still be sent for this appointment, earliest first.
    ///
    /// <list type="number">
    /// <item>one entry per lead tier <c>T</c> where <c>appointment − T &gt; now</c>, sent at <c>appointment − T</c>;</item>
    /// <item>if <b>no</b> tier is still future but <c>appointment − minLead &gt; now</c> → a single
    ///   <see cref="ReminderSendTime.PromptLeadHours"/> entry due immediately;</item>
    /// <item>else (within the min-lead window, or in the past) → nothing.</item>
    /// </list>
    ///
    /// <para><b>All the tiers, not the largest one.</b> This used to return a single <c>DateTime?</c> — the
    /// biggest future tier won and every other was discarded — while the settings screen invited
    /// « Séparez les paliers (heures) par des virgules · Ex. 24, 6 » and said nothing about only one firing. For
    /// a no-show problem the 6 h nudge is the one that works, and it was the one being thrown away.</para>
    ///
    /// <para>Each candidate then passes through the <b>quiet-hours</b> floor (see
    /// <see cref="ApplyQuietHours"/>). A tier the floor cannot place is dropped rather than moved into the
    /// window — which is why the result can be shorter than the tier list even when every tier is future.</para>
    /// </summary>
    public static IReadOnlyList<ReminderSendTime> ComputeSendTimesUtc(
        DateTime appointmentUtc,
        DateTime nowUtc,
        IReadOnlyList<int> leadTimesHours,
        int minLeadHours,
        (int StartHour, int EndHour) quietHoursLocal)
    {
        var results = new List<ReminderSendTime>();

        // Descending so the tiers are considered largest-first (the order the old single-value contract used),
        // then de-duplicated on the resolved instant: two tiers the quiet-hours floor pushes onto the same
        // boundary are one message, not two identical ones seconds apart.
        foreach (var hours in leadTimesHours.Where(h => h > 0).Distinct().OrderByDescending(h => h))
        {
            var candidate = appointmentUtc - TimeSpan.FromHours(hours);
            if (candidate <= nowUtc)
            {
                continue;
            }

            var placed = ApplyQuietHours(candidate, appointmentUtc, nowUtc, minLeadHours, quietHoursLocal);
            if (placed == null || results.Any(r => r.SendAtUtc == placed.Value))
            {
                continue;
            }

            results.Add(new ReminderSendTime(hours, placed.Value));
        }

        if (results.Count > 0)
        {
            return results.OrderBy(r => r.SendAtUtc).ToList();
        }

        // Closer than the smallest tier but still outside the min-lead window → send on the next tick. The
        // quiet-hours floor is applied here too: « due now » at 02:00 is exactly the 02:00 message the floor
        // exists to prevent.
        if (appointmentUtc - TimeSpan.FromHours(minLeadHours) > nowUtc)
        {
            var prompt = ApplyQuietHours(nowUtc, appointmentUtc, nowUtc, minLeadHours, quietHoursLocal);
            if (prompt != null)
            {
                return new[] { new ReminderSendTime(ReminderSendTime.PromptLeadHours, prompt.Value) };
            }
        }

        return Array.Empty<ReminderSendTime>();
    }

    /// <summary>
    /// Moves a send time out of the clinic's quiet hours, or reports that it cannot be placed.
    ///
    /// <para><b>Earlier first, later second.</b> For the case that motivates the floor — an 08:00 appointment
    /// booked ~22 h ahead, whose 24 h tier lands at 02:00 — pulling back to 21:00 the previous evening reaches
    /// the patient the night before, whereas pushing forward to 08:00 is the appointment itself. So: the quiet
    /// window's own start, if that is still in the future; else its end, if that still clears
    /// <paramref name="minLeadHours"/> before the appointment; else the tier is dropped. A reminder is never
    /// moved <i>into</i> the window and never sent after the visit it is reminding about.</para>
    /// </summary>
    /// <summary>
    /// How far before the quiet window a pulled-back send lands. One minute: « 20:59 » is unambiguously outside
    /// « pas d'envoi à partir de 21 h », and anything larger would start second-guessing the operator's own hour.
    /// </summary>
    private static readonly TimeSpan JustBeforeQuietHours = TimeSpan.FromMinutes(1);

    private static DateTime? ApplyQuietHours(
        DateTime candidateUtc,
        DateTime appointmentUtc,
        DateTime nowUtc,
        int minLeadHours,
        (int StartHour, int EndHour) quiet)
    {
        // Equal bounds mean "no quiet hours" — the only way to switch the floor off, and it must not silently
        // become a 24-hour window.
        if (quiet.StartHour == quiet.EndHour)
        {
            return candidateUtc;
        }

        var local = ClinicClock.ToClinicLocal(candidateUtc);
        if (!IsQuiet(local, quiet))
        {
            return candidateUtc;
        }

        // ⚠️ The target is the last minute BEFORE the window opens, not the window's own start. The start hour is
        // itself quiet (`IsQuiet` is `[start, end)`), so returning it would move the send from one quiet instant
        // to another and call the job done — which is precisely what
        // `ReminderScheduleTests.Never_Places_A_Send_Inside_Quiet_Hours` caught: an 01:00-local tier was
        // "corrected" to exactly 21:00.
        var earlier = ClinicClock.ToUtc(PrecedingQuietStart(local, quiet)) - JustBeforeQuietHours;
        if (earlier > nowUtc)
        {
            return earlier;
        }

        var later = ClinicClock.ToUtc(NextQuietEnd(local, quiet));
        if (later > nowUtc && later <= appointmentUtc - TimeSpan.FromHours(minLeadHours))
        {
            return later;
        }

        return null;
    }

    /// <summary>
    /// Is this clinic-local instant inside the quiet window? Handles the ordinary <b>wrapping</b> window
    /// (21:00 → 08:00 crosses midnight) as well as a same-day one (e.g. 01:00 → 06:00).
    /// </summary>
    private static bool IsQuiet(DateTime local, (int StartHour, int EndHour) quiet) =>
        quiet.StartHour > quiet.EndHour
            ? local.Hour >= quiet.StartHour || local.Hour < quiet.EndHour
            : local.Hour >= quiet.StartHour && local.Hour < quiet.EndHour;

    /// <summary>The start of the quiet window <paramref name="local"/> falls inside, as clinic-local time.</summary>
    private static DateTime PrecedingQuietStart(DateTime local, (int StartHour, int EndHour) quiet)
    {
        // Wrapping window, and we are past midnight (00:xx–07:xx of a 21→08 window): the window opened the
        // previous evening.
        var day = quiet.StartHour > quiet.EndHour && local.Hour < quiet.EndHour
            ? local.Date.AddDays(-1)
            : local.Date;
        return day.AddHours(quiet.StartHour);
    }

    /// <summary>The end of the quiet window <paramref name="local"/> falls inside, as clinic-local time.</summary>
    private static DateTime NextQuietEnd(DateTime local, (int StartHour, int EndHour) quiet)
    {
        // Wrapping window, and we are still in the evening (21:xx–23:xx of a 21→08 window): it closes tomorrow.
        var day = quiet.StartHour > quiet.EndHour && local.Hour >= quiet.StartHour
            ? local.Date.AddDays(1)
            : local.Date;
        return day.AddHours(quiet.EndHour);
    }
}
