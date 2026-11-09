using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// « Pointez votre arrivée » (<c>pointage-obligatoire</c> AC-1): the <b>one</b> predicate behind the lock screen and the
/// 423 on a save. Pure — <see cref="AttendanceLockEvaluator"/> gathers the facts, this decides.
/// </summary>
public static class AttendanceLockRules
{
    public const string ClockInRequiredCode = "clock_in_required";
    public const string ClockInRequired = "Pointez votre arrivée à la borne avant d'enregistrer.";

    public const string PreviousDepartureMissingCode = "previous_departure_missing";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« Votre départ d'hier … », or « … du vendredi 30 octobre … » after a weekend.</summary>
    public static string PreviousDepartureMissing(DateOnly day, DateOnly today) =>
        $"Votre départ {(day == today.AddDays(-1) ? "d'hier" : "du " + day.ToString("dddd d MMMM", French))} n'a pas été pointé. Indiquez votre heure de départ.";

    public const string DeclaredDepartureInvalidCode = "declared_departure_invalid";

    /// <summary>The reminder waits this long after closing (AC-6).</summary>
    public static readonly TimeSpan ReminderDelay = TimeSpan.FromMinutes(15);

    public const string ReminderTitle = "Départ non pointé";

    /// <summary>« Arrivée à 13:20 : pointez votre départ à la borne « Accueil ». » — no gendered word, the bornes named.</summary>
    public static string ReminderMessage(string since, IReadOnlyList<string> bornes) =>
        bornes.Count == 0
            ? $"Arrivée à {since} : pointez votre départ à la borne."
            : $"Arrivée à {since} : pointez votre départ à la borne {string.Join(" ou ", bornes.Select(StaffLeaveNotices.Quote))}.";

    /// <summary>The journal's rows that are not cabinet work: the pointage, sign-in and phone bookkeeping, a congé asked from home.</summary>
    public static readonly IReadOnlyCollection<string> NotWorkInTheJournal = new[]
    {
        nameof(Domain.Entities.AttendanceEntry),
        nameof(Domain.Entities.AttendanceTerminal),
        nameof(Domain.Entities.User),
        nameof(Domain.Entities.DeviceRegistration),
        nameof(Domain.Entities.StaffLeaveRequest),
    };

    /// <summary>« L'heure de départ doit être après l'arrivée (08:02). » — the borne's own words for the same refusal.</summary>
    public static string DeclaredDepartureInvalid(string arrival) =>
        $"L'heure de départ doit être après l'arrivée ({arrival}).";

    /// <summary>
    /// Every lifting condition of AC-1, AC-4 and the three a lock nobody can lift implies: the cabinet on its PC de
    /// secours, a cabinet that may not record new work (the borne refuses the punch too) and a cabinet with no borne —
    /// and it never starts mid-day: only from the first opening after it was armed and a borne went live.
    /// </summary>
    public static bool IsLocked(AttendanceLockFacts facts) =>
        facts.AttendanceEnabled
        && facts.LockEnabled
        && facts.Tracked
        && facts.InsideOpeningHours
        && !facts.HasLineToday
        && !facts.OnLeaveToday
        && !facts.OnPcDeSecours
        && facts.MayRecordWork
        && facts.HasActiveBorne
        && facts.LockStarted;

    public const string LockStateOff = "off";
    public const string LockStateActive = "active";
    public const string LockStateFrom = "from";
    public const string LockStateInactive = "inactive";
    public const string InactiveHoursUnset = "hours_unset";
    public const string InactiveNoBorne = "no_borne";
    public const string InactiveBornePending = "borne_pending";

    /// <summary>The later of « lock armed » and « first borne live »; null when neither was recorded (long ago).</summary>
    public static DateTime? LockStartsAfterUtc(DateTime? armedAtUtc, DateTime? firstBorneLiveAtUtc) =>
        armedAtUtc is { } armed && firstBorneLiveAtUtc is { } live ? (armed > live ? armed : live)
        : armedAtUtc ?? firstBorneLiveAtUtc;

    /// <summary>The cabinet's opening on <paramref name="day"/> as a UTC instant, or null on a day with no hours.</summary>
    public static DateTime? OpeningUtc(DateOnly day, string? workingHoursJson) =>
        HoursOn(day, workingHoursJson) is { } hours ? ClinicClock.ToUtc(day.ToDateTime(TimeOnly.MinValue).Add(hours.From)) : null;

    /// <summary>The first opening at or after <paramref name="fromUtc"/>, within a week; null when no day has hours.</summary>
    public static DateTime? FirstOpeningFromUtc(DateTime fromUtc, string? workingHoursJson)
    {
        var day = AttendanceRules.LocalDayOf(fromUtc);
        for (var i = 0; i <= 7; i++)
        {
            if (OpeningUtc(day.AddDays(i), workingHoursJson) is { } opening && opening >= fromUtc)
                return opening;
        }
        return null;
    }

    /// <summary>Some day of the week has valid opening hours — what « inside opening hours » needs to be answerable.</summary>
    public static bool HasOpeningHours(string? workingHoursJson)
    {
        var monday = new DateOnly(2026, 1, 5);
        return Enumerable.Range(0, 7).Any(i => HoursOn(monday.AddDays(i), workingHoursJson) is not null);
    }

    /// <summary>
    /// The lock has begun: it holds from the first opening at or after <paramref name="startsAfterUtc"/> (null = long ago),
    /// so a borne going live or the switch turned on mid-day never locks anyone before the next opening.
    /// </summary>
    public static bool HasLockStarted(DateTime? startsAfterUtc, string? workingHoursJson, DateTime nowUtc) =>
        startsAfterUtc is not { } start
        || FirstOpeningFromUtc(start, workingHoursJson) is { } opening && opening <= nowUtc;

    /// <summary>
    /// Where the lock stands for the setup checklist, from the facts <see cref="IsLocked"/> reads at cabinet level:
    /// the switch, the hours, a live borne, and whether it has begun.
    /// </summary>
    public static (string State, DateTime? ActiveFrom, string? InactiveReason) LockSetup(
        bool attendanceEnabled, bool lockEnabled, string? workingHoursJson, LiveBornes bornes, bool bornePending,
        DateTime? armedAtUtc, DateTime nowUtc)
    {
        if (!attendanceEnabled || !lockEnabled)
            return (LockStateOff, null, null);
        if (!HasOpeningHours(workingHoursJson))
            return (LockStateInactive, null, InactiveHoursUnset);
        if (!bornes.Any)
            return (LockStateInactive, null, bornePending ? InactiveBornePending : InactiveNoBorne);
        var start = LockStartsAfterUtc(armedAtUtc, bornes.FirstActivatedAtUtc);
        return HasLockStarted(start, workingHoursJson, nowUtc)
            ? (LockStateActive, null, null)
            : (LockStateFrom, FirstOpeningFromUtc(start!.Value, workingHoursJson), null);
    }

    /// <summary>Today's open hours as cabinet-local times, or null: hours unset or unreadable, or a closed day.</summary>
    public static (TimeSpan From, TimeSpan To)? HoursOn(DateOnly day, string? workingHoursJson)
    {
        var read = WorkingHoursSerializer.Read(workingHoursJson);
        if (read.State != WorkingHoursReadState.Valid)
            return null;
        var hours = WorkingHoursSerializer.DayOf(read.Days, day.DayOfWeek);
        if (hours is not { Enabled: true }
            || !WorkingHoursSerializer.TryParseTime(hours.From, out var from)
            || !WorkingHoursSerializer.TryParseTime(hours.To, out var to)
            || to <= from)
            return null;
        return (from, to);
    }

    /// <summary>
    /// Between opening and closing on an open day, the lunch break included — she has to have arrived to work at all.
    /// Hours not set: never (the spec's edge — « inside opening hours » cannot be answered).
    /// </summary>
    public static bool IsInsideOpeningHours(string? workingHoursJson, DateTime nowUtc)
    {
        var local = ClinicClock.ToClinicLocal(nowUtc);
        return HoursOn(DateOnly.FromDateTime(local), workingHoursJson) is { } hours
               && local.TimeOfDay >= hours.From && local.TimeOfDay < hours.To;
    }

    /// <summary>When the reminder is due on <paramref name="day"/> (closing + 15 min, as UTC), or null on a day with no hours.</summary>
    public static DateTime? ReminderDueUtc(DateOnly day, string? workingHoursJson) =>
        HoursOn(day, workingHoursJson) is { } hours
            ? ClinicClock.ToUtc(day.ToDateTime(TimeOnly.MinValue).Add(hours.To)) + ReminderDelay
            : null;

    /// <summary>« 2026-11-04 » — the day key the borne's declaration step names.</summary>
    public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>What <see cref="AttendanceLockRules.IsLocked"/> is decided on — each one an AC-1 condition.</summary>
public sealed record AttendanceLockFacts(
    bool AttendanceEnabled,
    bool LockEnabled,
    bool Tracked,
    bool InsideOpeningHours,
    bool HasLineToday,
    bool OnLeaveToday,
    bool OnPcDeSecours,
    bool MayRecordWork,
    bool HasActiveBorne,
    bool LockStarted);
