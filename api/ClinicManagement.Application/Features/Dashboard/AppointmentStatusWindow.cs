using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>How wide one column of « Rendez-vous par statut » is.</summary>
public enum AppointmentBucketGranularity
{
    Day = 1,
    Week = 2,
    Month = 3
}

/// <summary>
/// The window « Rendez-vous par statut » covers, the granularity it is drawn at, and the buckets themselves — the
/// single authority on this card's period arithmetic, the way <see cref="DashboardPeriod"/> is for the rest of the
/// page.
///
/// <para><b>Why the card has its own window type at all.</b> The card carries its own period control (Semaine /
/// Mois / Personnalisé), so its window is not <see cref="DashboardPeriod"/>'s. Everything else on the page still
/// reads the page's period; this type exists so the card's arithmetic is written once and can be tested, rather than
/// being spread between a controller, a reader and a browser.</para>
///
/// <para><b>The bounds arrive as bare day keys and never as instants.</b> <c>YYYY-MM-DD</c> carries no timezone, so
/// the clinic's day stays a fact about the clinic rather than about whoever is looking at it. Building the instants
/// in the browser is the defect <c>CaissePeriod</c> exists to have fixed (AC-6): <c>new Date(day + 'T00:00:00')</c>
/// is midnight on the <i>workstation</i>, so on a machine that is not UTC+1 « la semaine du 17 août » was a window
/// offset by hours.</para>
/// </summary>
/// <param name="FromLocalDate">Inclusive first clinic-local day of the window.</param>
/// <param name="ToLocalDate">Inclusive last clinic-local day of the window.</param>
/// <param name="Granularity">How wide one bucket is.</param>
public sealed record AppointmentStatusWindow(
    DateTime FromLocalDate,
    DateTime ToLocalDate,
    AppointmentBucketGranularity Granularity)
{
    /// <summary>
    /// The widest window the card will draw.
    ///
    /// <para>A free range with no ceiling is a read with no ceiling — and this one transfers a row per appointment
    /// rather than an aggregate, because the clinic-local bucketing has to happen in C# (see
    /// <see cref="Readers.DashboardAppointmentStatusReader"/>). A year of a busy practice is a few thousand
    /// two-column rows, which is fine; five years is not, and the difference must be a refusal the user can read
    /// rather than a page that quietly takes ten seconds.</para>
    /// </summary>
    public const int MaxDays = 366;

    /// <summary>At or below this many days, one column is one day.</summary>
    private const int MaxDaysForDailyBuckets = 31;

    /// <summary>
    /// At or below this many days, one column is one week; above it, one month.
    ///
    /// <para>120 rather than something rounder because of what it costs at the far end: 120 daily columns at the
    /// 10 px floor a column needs to stay a column is ~1 400 px of horizontal scrolling on a phone, which stops
    /// being a chart and becomes a filmstrip. Weeks keep a four-month window at 18 columns.</para>
    /// </summary>
    private const int MaxDaysForWeeklyBuckets = 120;

    /// <summary>Inclusive day count of the window. One day is 1, never 0.</summary>
    public int DayCount => (ToLocalDate.Date - FromLocalDate.Date).Days + 1;

    /// <summary>The UTC instants the repository is queried with — clinic-local midnights, upper bound the last tick.</summary>
    public (DateTime From, DateTime ToInclusive) UtcRange => (
        ClinicClock.StartOfLocalDayUtc(FromLocalDate),
        ClinicClock.LastTickOfLocalDayUtc(ToLocalDate));

    /// <summary>
    /// The immediately preceding window of the <b>same length</b>, for the card's « comparé à » figure.
    ///
    /// <para>Same length rather than the same calendar unit, because a free range has no calendar unit. For
    /// « Cette semaine » that gives exactly last week, and for « Ce mois » it gives the same number of days ending
    /// the day before — which is not the previous calendar month. The card says so in words rather than implying a
    /// month-to-month comparison it is not making.</para>
    /// </summary>
    public (DateTime From, DateTime ToInclusive) PreviousUtcRange
    {
        get
        {
            var previousEnd = FromLocalDate.Date.AddDays(-1);
            var previousStart = previousEnd.AddDays(-(DayCount - 1));
            return (ClinicClock.StartOfLocalDayUtc(previousStart), ClinicClock.LastTickOfLocalDayUtc(previousEnd));
        }
    }

    /// <summary>
    /// Resolves the window from what the client sent.
    ///
    /// <para>With no bounds at all the window is the current clinic-local <b>week</b>, Monday-based — the card's
    /// default position. With one bound only it is refused rather than half-answered: a lone <c>to</c> is a client
    /// that lost half its state, and answering it would produce a plausible wrong number.</para>
    /// </summary>
    public static Result<AppointmentStatusWindow> Resolve(string? fromDay, string? toDay, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(fromDay) && string.IsNullOrWhiteSpace(toDay))
        {
            return Result<AppointmentStatusWindow>.Success(CurrentWeek(nowUtc));
        }

        var start = ParseDay(fromDay);
        var end = ParseDay(toDay);

        if (start is null || end is null)
        {
            return Result<AppointmentStatusWindow>.Failure(
                "Période invalide : indiquez un début et une fin au format AAAA-MM-JJ.");
        }

        if (end < start)
        {
            return Result<AppointmentStatusWindow>.Failure(
                "La date de fin doit être postérieure à la date de début.");
        }

        var days = (end.Value.Date - start.Value.Date).Days + 1;
        if (days > MaxDays)
        {
            // Stated and refused, never silently clamped: a window quietly narrowed to a year would report a total
            // for a period the user did not ask for, and nothing on the card would say so.
            return Result<AppointmentStatusWindow>.Failure(
                $"La période ne peut pas dépasser {MaxDays} jours. Choisissez une date de fin plus proche.");
        }

        return Result<AppointmentStatusWindow>.Success(
            new AppointmentStatusWindow(start.Value.Date, end.Value.Date, GranularityFor(days)));
    }

    /// <summary>The current clinic-local week, Monday to Sunday — the card's default window.</summary>
    public static AppointmentStatusWindow CurrentWeek(DateTime nowUtc)
    {
        var today = ClinicClock.ClinicToday(nowUtc);
        // Monday-based, matching DashboardPeriod.ResolveWeek and the agenda's date-fns `weekStartsOn: 1`. Two
        // different week starts in one product is a figure that never agrees with the screen beside it.
        var start = StartOfWeek(today);
        return new AppointmentStatusWindow(start, start.AddDays(6), AppointmentBucketGranularity.Day);
    }

    /// <summary>The current clinic-local calendar month.</summary>
    public static AppointmentStatusWindow CurrentMonth(DateTime nowUtc)
    {
        var today = ClinicClock.ClinicToday(nowUtc);
        var start = new DateTime(today.Year, today.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return new AppointmentStatusWindow(start, end, GranularityFor((end - start).Days + 1));
    }

    private static AppointmentBucketGranularity GranularityFor(int days) => days switch
    {
        <= MaxDaysForDailyBuckets => AppointmentBucketGranularity.Day,
        <= MaxDaysForWeeklyBuckets => AppointmentBucketGranularity.Week,
        _ => AppointmentBucketGranularity.Month
    };

    /// <summary>
    /// The buckets, in order, oldest first — <b>every</b> one of them, including those with no appointments.
    ///
    /// <para>A bucket with nothing in it is filled with zeros and never omitted. Dropping it is the defect
    /// <c>DashboardTrendReader</c> already records for the money series: the series silently shortens and every
    /// later point slides left, so a quiet week reads as though it never happened. Here it would also make a
    /// clinic's closed Sundays vanish and leave the week six columns wide.</para>
    ///
    /// <para>The first and last buckets are <b>clamped to the window</b>. A week-granular window starting on a
    /// Thursday has a first bucket of four days, and it is labelled as those four days rather than as a full week
    /// the read does not cover.</para>
    /// </summary>
    public IReadOnlyList<(DateTime Start, DateTime EndInclusive)> Buckets()
    {
        var buckets = new List<(DateTime Start, DateTime EndInclusive)>();
        var cursor = FromLocalDate.Date;

        while (cursor <= ToLocalDate.Date)
        {
            var naturalEnd = Granularity switch
            {
                AppointmentBucketGranularity.Day => cursor,
                AppointmentBucketGranularity.Week => StartOfWeek(cursor).AddDays(6),
                _ => new DateTime(cursor.Year, cursor.Month, 1).AddMonths(1).AddDays(-1)
            };

            var end = naturalEnd > ToLocalDate.Date ? ToLocalDate.Date : naturalEnd;
            buckets.Add((cursor, end));
            cursor = end.AddDays(1);
        }

        return buckets;
    }

    /// <summary>
    /// Which bucket a clinic-local day falls in, or <c>-1</c> when it falls outside the window.
    ///
    /// <para>Derived from the same rule <see cref="Buckets"/> walks, rather than by scanning the list: the caller
    /// runs this once per appointment row, and a linear scan would make the fold quadratic on a year of a busy
    /// practice.</para>
    /// </summary>
    public int IndexOf(DateTime clinicLocalDate)
    {
        var day = clinicLocalDate.Date;
        if (day < FromLocalDate.Date || day > ToLocalDate.Date)
        {
            return -1;
        }

        return Granularity switch
        {
            AppointmentBucketGranularity.Day => (day - FromLocalDate.Date).Days,
            AppointmentBucketGranularity.Week => (StartOfWeek(day) - StartOfWeek(FromLocalDate.Date)).Days / 7,
            _ => ((day.Year - FromLocalDate.Year) * 12) + day.Month - FromLocalDate.Month
        };
    }

    /// <summary>Monday of the week containing <paramref name="day"/>. <c>DayOfWeek.Sunday</c> is 0, hence the shift.</summary>
    private static DateTime StartOfWeek(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// <c>YYYY-MM-DD</c> and nothing else — the same exact parse <c>CaissePeriod</c> uses, and for the same reason:
    /// <c>TryParse</c> with the invariant culture would also accept « 03/08/2026 », whose meaning changes with the
    /// locale. That ambiguity is the one thing a bare day key exists to remove.
    /// </summary>
    private static DateTime? ParseDay(string? value) =>
        DateTime.TryParseExact(
            value?.Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var day)
            ? day
            : null;
}
