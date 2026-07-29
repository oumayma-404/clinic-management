using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>
/// The window the dashboard is read over, <b>and the immediately-preceding equivalent window</b> every comparable
/// figure is measured against. The single authority on the dashboard's period arithmetic.
///
/// <para><b>Why both halves live in one type.</b> A comparison is only meaningful if its two sides were derived by
/// the same rule from the same clock. Letting the caller compute « ce mois » and the reader compute « le mois
/// dernier » is how a KPI ends up comparing 31 days against 30, or a clinic-local month against a UTC one. The
/// previous window is therefore not derivable by consumers — it is handed to them.</para>
///
/// <para><b>Why the client no longer sends the bounds.</b> The retired <c>GetDashboardStatsQuery</c> accepted
/// client-supplied day/week/month boundaries so its counts matched the agenda. That was correct when there was no
/// server-side clinic clock; <see cref="ClinicClock"/> now gives the same answer (Tunisia is UTC+1 with no DST) and
/// removes the possibility of the two sides disagreeing.</para>
///
/// <para>⚠️ <see cref="ToInclusive"/> is the last <b>tick</b> of the window, not the next midnight.
/// <c>ClinicClock.EndOfLocalDayUtc</c> returns an <i>exclusive</i> bound while the money reads
/// (<c>GetCollectedBetweenAsync</c> and friends) are inclusive on both ends, so handing them the exclusive instant
/// counts a payment recorded at exactly midnight in <b>both</b> adjacent periods — the defect already documented at
/// <c>GetCaisseSummaryQuery</c> (finding #20). The <c>Inclusive</c> suffix is deliberate: it is the one thing a
/// future caller must not get wrong.</para>
/// </summary>
/// <param name="Key">Which window this is.</param>
/// <param name="From">Inclusive start of the current window, as a UTC instant.</param>
/// <param name="ToInclusive">Inclusive end of the current window (last tick), as a UTC instant.</param>
/// <param name="PreviousFrom">Inclusive start of the preceding equivalent window, as a UTC instant.</param>
/// <param name="PreviousToInclusive">Inclusive end of the preceding window (last tick), as a UTC instant.</param>
public sealed record DashboardPeriod(
    DashboardPeriodKey Key,
    DateTime From,
    DateTime ToInclusive,
    DateTime PreviousFrom,
    DateTime PreviousToInclusive)
{
    /// <summary>How many months of history the « Tendance » sparkline covers, including the current one.</summary>
    public const int TrendMonths = 6;

    /// <summary>
    /// Resolves both windows for <paramref name="key"/> from the clinic's wall clock.
    /// </summary>
    /// <param name="nowUtc">
    /// The current instant. Injected rather than read from <c>DateTime.UtcNow</c> so the boundary behaviour is
    /// testable — the month-clamping and week-start rules below are the whole reason this type exists.
    /// </param>
    public static DashboardPeriod Resolve(DashboardPeriodKey key, DateTime nowUtc)
    {
        var today = ClinicClock.ClinicToday(nowUtc);

        return key switch
        {
            DashboardPeriodKey.Today => ForDays(key, today, today, today.AddDays(-1), today.AddDays(-1)),
            DashboardPeriodKey.Week => ResolveWeek(key, today),
            DashboardPeriodKey.Month => ResolveMonth(key, today),
            // A value outside the enum can only arrive by an explicit cast; treat it as the default window rather
            // than throwing, since this type is on the read path of the application's home screen.
            _ => ResolveMonth(DashboardPeriodKey.Month, today)
        };
    }

    private static DashboardPeriod ResolveWeek(DashboardPeriodKey key, DateTime clinicToday)
    {
        // Monday-based, matching the agenda's date-fns `weekStartsOn: 1`. DayOfWeek.Sunday is 0, so the shift maps
        // Monday => 0 … Sunday => 6.
        var offsetIntoWeek = ((int)clinicToday.DayOfWeek + 6) % 7;
        var weekStart = clinicToday.AddDays(-offsetIntoWeek);

        return ForDays(key, weekStart, weekStart.AddDays(6), weekStart.AddDays(-7), weekStart.AddDays(-1));
    }

    private static DashboardPeriod ResolveMonth(DashboardPeriodKey key, DateTime clinicToday)
    {
        var monthStart = new DateTime(clinicToday.Year, clinicToday.Month, 1);

        // The previous month is derived from its OWN first day, never by subtracting days or by AddMonths-ing the
        // current day. `AddMonths` clamps to the end of a shorter month, so on 31 March `today.AddMonths(-1)` is
        // 28 February and a naive "same day last month .. today last month" range would silently cover 28 Feb–28 Feb
        // instead of the whole of February. Anchoring on the first of the month and taking `AddDays(-1)` from the
        // current month's start gives the exact previous calendar month for every date, including the 29th–31st.
        var previousMonthStart = monthStart.AddMonths(-1);
        var previousMonthEnd = monthStart.AddDays(-1);

        return ForDays(key, monthStart, monthStart.AddMonths(1).AddDays(-1), previousMonthStart, previousMonthEnd);
    }

    /// <summary>
    /// Converts four clinic-local <b>dates</b> into the two UTC instant ranges. Every boundary goes through
    /// <see cref="ClinicClock"/>, and each upper bound is the last tick of its local day (see the type remarks).
    /// </summary>
    private static DashboardPeriod ForDays(
        DashboardPeriodKey key,
        DateTime fromDay,
        DateTime toDay,
        DateTime previousFromDay,
        DateTime previousToDay) =>
        new(
            key,
            ClinicClock.StartOfLocalDayUtc(fromDay),
            LastTickOfLocalDay(toDay),
            ClinicClock.StartOfLocalDayUtc(previousFromDay),
            LastTickOfLocalDay(previousToDay));

    /// <summary>
    /// The last representable instant inside a clinic-local day, as UTC. <c>EndOfLocalDayUtc</c> is the *next*
    /// midnight; the money reads are inclusive on both ends, so the tick is subtracted here once rather than at
    /// each of the twenty-odd call sites.
    /// </summary>
    private static DateTime LastTickOfLocalDay(DateTime clinicLocalDate) =>
        ClinicClock.EndOfLocalDayUtc(clinicLocalDate).AddTicks(-1);

    /// <summary>
    /// The window covering the trend sparkline: the first day of the month <see cref="TrendMonths"/>−1 months back,
    /// through the end of the current period. Its own method because the trend is the one read that deliberately
    /// ignores <see cref="From"/> — a six-month series cannot be derived from a one-day window.
    /// </summary>
    public (DateTime From, DateTime ToInclusive) TrendWindow(DateTime nowUtc)
    {
        var today = ClinicClock.ClinicToday(nowUtc);
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(TrendMonths - 1));

        return (ClinicClock.StartOfLocalDayUtc(firstMonth), LastTickOfLocalDay(today));
    }
}
