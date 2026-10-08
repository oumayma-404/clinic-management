using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>One admin bell row the cabinet should carry right now.</summary>
public sealed record RelayAlertRow(RelayAlert Alert, string Title, string Message);

/// <summary>
/// Which PC de secours bell rows a cabinet's admins should see right now (<c>clinic-pc-copy</c> AC-2.2, EC-9, EC-10).
/// Pure: the watcher passes the relay, the clinic's hours and the rows already shown, and syncs the bell to the answer.
///
/// <para>⚠️ <b>« Not ready » is told only during opening hours</b> (AC-2.2): a PC switched off at closing is normal, and
/// a row every evening teaches admins to ignore this one. Once shown, a row stays while the problem lasts — after
/// closing too — and may change kind without a second wait, because withdrawing a still-true row is a lie.</para>
///
/// <para>A stopped copy (AC-9.4), an abandoned setup (EC-9) and a wrong clock (EC-10) are told at any hour: none of
/// them is the ordinary state of a PC outside opening hours.</para>
/// </summary>
public static class RelayAlertRules
{
    /// <summary>Off or late for this long, inside opening hours, before admins are told (FR-2 « après 15 min »).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    /// <summary>The PC's clock this far from the cloud's is « fausse » (D20b's threshold).</summary>
    public const int ClockToleranceSeconds = 30;

    /// <summary>AC-5.9: a return failing this long is told to admins and the vendor, at any hour.</summary>
    public static readonly TimeSpan ReturnStuckAfter = TimeSpan.FromMinutes(15);

    /// <summary>The PC holds the cut and has failed to hand it back for <see cref="ReturnStuckAfter"/>.</summary>
    public static bool IsReturnStuck(ClinicRelay relay, DateTime nowUtc) =>
        relay.PcHoldingSinceUtc is not null
        && relay.ReturnStuckSinceUtc is { } stuck
        && nowUtc - stuck >= ReturnStuckAfter;

    /// <summary>
    /// A cabinet that never set its hours: Monday to Saturday, 08:00–18:00. « Not configured » means « no booking
    /// restriction » to the agenda, but here it must not mean « never open », or such a cabinet is never told anything.
    /// </summary>
    public static readonly IReadOnlyList<WorkingDayDto> DefaultHours =
        WorkingHoursSerializer.Weekdays
            .Select(d => new WorkingDayDto { Day = d, Enabled = d != "Sunday", From = "08:00", To = "18:00" })
            .ToList();

    public static IReadOnlyList<RelayAlertRow> Wanted(
        ClinicRelay? relay, string? clinicHoursJson, IReadOnlyCollection<RelayAlert> shown, DateTime nowUtc,
        int pendingReviews = 0, int pendingReEntries = 0)
    {
        if (relay is null)
        {
            return Array.Empty<RelayAlertRow>();
        }

        var wanted = WantedForThePc(relay, clinicHoursJson, shown, nowUtc).ToList();

        // AC-5.6: what the return listed stays on the bell, at any hour, until every line is « Vu ».
        if (pendingReviews > 0)
        {
            wanted.Add(new(RelayAlert.ToReview, "Modifications à vérifier", Queries.RelayReviewLabels.BellMessage(pendingReviews)));
        }

        // AC-7.4: what an overruled PC held stays on the bell until every record is « Repris ».
        if (pendingReEntries > 0)
        {
            wanted.Add(new(RelayAlert.ToReEnter, "À reprendre", Queries.RelayReviewLabels.ReEnterBellMessage(pendingReEntries)));
        }

        return wanted;
    }

    private static IReadOnlyList<RelayAlertRow> WantedForThePc(
        ClinicRelay relay, string? clinicHoursJson, IReadOnlyCollection<RelayAlert> shown, DateTime nowUtc)
    {

        var reading = ClinicRelayHealth.Read(relay, nowUtc);
        var label = relay.Label;
        var rows = new List<RelayAlertRow>();

        // AC-6.4: an armed PC fell silent and the cabinet's saves are refused on the cloud — told at once, at any hour,
        // in place of « éteint » (the same PC, said twice). Not while the PC said it holds the saves: that is a cut.
        if (relay.PcHoldingSinceUtc is null && ClinicWriteLease.LockedSinceUtc(relay, nowUtc) is { } lockedSince)
        {
            rows.Add(new(RelayAlert.Silent, "PC de secours injoignable",
                $"Le PC de secours ne répond plus depuis {RelayLabels.Moment(lockedSince, nowUtc)} : le cabinet ne peut pas "
                + "enregistrer sur le cloud. Rallumez-le, ou « Reprendre la main » dans « Paramètres → PC de secours »."));
            if (HasWrongClock(relay, reading))
            {
                rows.Add(new(RelayAlert.ClockWrong, "Horloge du PC de secours fausse",
                    $"L'horloge de {label} est fausse : réglez la date et l'heure de Windows."));
            }

            return rows;
        }

        // AC-5.9: the cabinet works on the PC and its work is not reaching the cloud — told at any hour.
        if (IsReturnStuck(relay, nowUtc))
        {
            rows.Add(new(RelayAlert.ReturnStuck, "Retour au cloud bloqué",
                $"Depuis {RelayLabels.Moment(relay.ReturnStuckSinceUtc, nowUtc)}, {label} n'arrive pas à rendre au cloud le "
                + "travail fait pendant la coupure. Le cabinet continue de travailler sur le PC de secours, qui réessaie ; "
                + "le support est prévenu."));
        }

        switch (reading.State)
        {
            case ClinicRelayState.Abandoned:
                rows.Add(new(RelayAlert.Abandoned, "Installation abandonnée",
                    $"Installation abandonnée sur {label} : le PC de secours peut être installé sur un autre PC."));
                break;

            case ClinicRelayState.Stopped when relay.CutOverruledAtUtc is not null:
                rows.Add(new(RelayAlert.Stopped, "Copie du PC de secours arrêtée",
                    $"Le cloud a repris la main pendant la coupure : ce que le cabinet a enregistré sur {label} y est gardé, "
                    + "et la copie est arrêtée pour ne rien perdre. Contactez le support."));
                break;

            case ClinicRelayState.Stopped:
                rows.Add(new(RelayAlert.Stopped, "Copie du PC de secours arrêtée",
                    $"Le cloud est revenu à un état antérieur : la copie sur {label} est arrêtée pour ne rien perdre. "
                    + "Contactez le support."));
                break;

            case ClinicRelayState.Off or ClinicRelayState.Late or ClinicRelayState.DiskNearlyFull or ClinicRelayState.Mismatch:
                var notReady = NotReadyRow(reading, relay, label, nowUtc);
                if (shown.Any(IsNotReady) || DueInOpeningHours(reading, clinicHoursJson, nowUtc))
                {
                    rows.Add(notReady);
                }

                break;
        }

        if (HasWrongClock(relay, reading))
        {
            rows.Add(new(RelayAlert.ClockWrong, "Horloge du PC de secours fausse",
                $"L'horloge de {label} est fausse : réglez la date et l'heure de Windows."));
        }

        return rows;
    }

    public static bool IsNotReady(RelayAlert alert) =>
        alert is RelayAlert.Off or RelayAlert.Late or RelayAlert.DiskNearlyFull or RelayAlert.Mismatch;

    private static RelayAlertRow NotReadyRow(ClinicRelayHealthReading reading, ClinicRelay relay, string label, DateTime nowUtc) =>
        reading.State switch
        {
            ClinicRelayState.Off => new(RelayAlert.Off, "PC de secours éteint",
                $"{label} est éteint depuis {RelayLabels.Moment(reading.Since, nowUtc)} : il ne pourra pas prendre le relais si internet tombe."),
            ClinicRelayState.Late => new(RelayAlert.Late, "PC de secours en retard",
                $"La copie sur {label} est en retard depuis {RelayLabels.Moment(reading.Since, nowUtc)} : il ne pourra pas prendre le relais si internet tombe."),
            ClinicRelayState.DiskNearlyFull => new(RelayAlert.DiskNearlyFull, "Disque du PC de secours presque plein",
                $"Il reste {Math.Max(0, (relay.DiskFreeBytes ?? 0) / (1024L * 1024 * 1024))} Go sur {label} : libérez de la place."),
            _ => new(RelayAlert.Mismatch, "Copie du PC de secours à vérifier",
                $"La copie sur {label} ne correspond pas au cloud et n'a pas pu se réparer seule. Contactez le support."),
        };

    /// <summary>Inside opening hours — and, for « éteint » / « en retard », for <see cref="Grace"/> of them already.</summary>
    public static bool DueInOpeningHours(ClinicRelayHealthReading reading, string? clinicHoursJson, DateTime nowUtc)
    {
        var opened = OpenedAtUtc(clinicHoursJson, nowUtc);
        if (opened is null)
        {
            return false;
        }

        if (reading.State is not (ClinicRelayState.Off or ClinicRelayState.Late))
        {
            return true;
        }

        // Counted from the opening, not from last night: a PC switched on five minutes after opening is not a problem.
        var since = reading.Since ?? nowUtc;
        var start = since > opened.Value ? since : opened.Value;
        return nowUtc - start >= Grace;
    }

    /// <summary>When the opening window holding <paramref name="nowUtc"/> began (UTC), or null when the cabinet is closed.</summary>
    public static DateTime? OpenedAtUtc(string? clinicHoursJson, DateTime nowUtc)
    {
        var read = WorkingHoursSerializer.Read(clinicHoursJson);
        var days = read.State == WorkingHoursReadState.Valid ? read.Days : DefaultHours;

        var local = ClinicClock.ToClinicLocal(nowUtc);
        var dayName = WorkingHoursSerializer.Weekdays[(int)local.DayOfWeek];
        var day = days.FirstOrDefault(d => string.Equals(d.Day?.Trim(), dayName, StringComparison.OrdinalIgnoreCase));
        if (day is null || !day.Enabled
            || !WorkingHoursSerializer.TryParseTime(day.From, out var from)
            || !WorkingHoursSerializer.TryParseTime(day.To, out var to))
        {
            return null;
        }

        var time = local.TimeOfDay;
        if (time < from || time >= to)
        {
            return null;
        }

        var windowStart = from;
        if (WorkingHoursSerializer.TryParseTime(day.BreakFrom, out var breakFrom)
            && WorkingHoursSerializer.TryParseTime(day.BreakTo, out var breakTo))
        {
            if (time >= breakFrom && time < breakTo)
            {
                return null;
            }

            if (time >= breakTo)
            {
                windowStart = breakTo;
            }
        }

        return ClinicClock.ToUtc(local.Date + windowStart);
    }

    /// <summary>EC-10: measured at its last contact. Not asked of a setup that ended or a PC already retired.</summary>
    private static bool HasWrongClock(ClinicRelay relay, ClinicRelayHealthReading reading) =>
        reading.State is not (ClinicRelayState.None or ClinicRelayState.Abandoned or ClinicRelayState.Retired)
        && relay.ClockSkewSeconds is { } skew
        && Math.Abs(skew) > ClockToleranceSeconds;
}
