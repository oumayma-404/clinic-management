using System.Globalization;

namespace ClinicManagement.Application.Common;

/// <summary>
/// The clinic's wall clock. Tunisia is <b>UTC+1 all year</b> (no DST since 2008).
/// <para>
/// Created for P1 — working hours are expressed in clinic-local time while appointments are stored as UTC
/// instants, so enforcing one against the other needs a conversion that cannot be guessed. It also replaces
/// the <b>two byte-identical private copies</b> of <c>ResolveTunisiaTimeZone()</c> that had been copy-pasted
/// into separate query handlers (adjacent defect <b>A-21</b>), and is the single helper P6's local-day work
/// builds on.
/// </para>
/// <para>
/// ⚠️ <see cref="StartOfLocalDayUtc"/> and <see cref="EndOfLocalDayUtc"/> return an <b>explicit UTC instant</b>,
/// never a bare local <c>DateTime</c>. <c>ApplicationDbContext</c> treats <see cref="DateTimeKind.Unspecified"/>
/// as UTC on write, so handing a local value to a query would silently reinterpret it as UTC and shift every
/// boundary by an hour.
/// </para>
/// </summary>
public static class ClinicClock
{
    /// <summary>Fallback offset when the host has no tz database entry (bare containers, some Windows SKUs).</summary>
    private static readonly TimeSpan TunisiaOffset = TimeSpan.FromHours(1);

    private static readonly Lazy<TimeZoneInfo?> Tunisia = new(() =>
    {
        // IANA first (Linux/macOS containers), then the Windows id. Both are tried because the same binary
        // runs on a Windows clinic PC and in a Linux Cloud container.
        foreach (var id in new[] { "Africa/Tunis", "W. Central Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return null;
    });

    /// <summary>The clinic-local wall-clock time for a UTC instant.</summary>
    public static DateTime ToClinicLocal(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var zone = Tunisia.Value;
        return zone != null
            ? TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone)
            : asUtc + TunisiaOffset;
    }

    /// <summary>The UTC instant a clinic-local wall-clock time corresponds to.</summary>
    public static DateTime ToUtc(DateTime clinicLocal)
    {
        var unspecified = DateTime.SpecifyKind(clinicLocal, DateTimeKind.Unspecified);
        var zone = Tunisia.Value;
        return zone != null
            ? TimeZoneInfo.ConvertTimeToUtc(unspecified, zone)
            : DateTime.SpecifyKind(unspecified - TunisiaOffset, DateTimeKind.Utc);
    }

    /// <summary>Today's date in the clinic's zone.</summary>
    public static DateTime ClinicToday(DateTime? nowUtc = null) =>
        ToClinicLocal(nowUtc ?? DateTime.UtcNow).Date;

    /// <summary>The year the clinic is currently in — the authority for a document's number sequence.</summary>
    public static int ClinicYear(DateTime? nowUtc = null) => ClinicToday(nowUtc).Year;

    /// <summary>Midnight of a clinic-local day, as a UTC instant.</summary>
    public static DateTime StartOfLocalDayUtc(DateTime clinicLocalDate) => ToUtc(clinicLocalDate.Date);

    /// <summary>The exclusive end of a clinic-local day, as a UTC instant.</summary>
    public static DateTime EndOfLocalDayUtc(DateTime clinicLocalDate) => ToUtc(clinicLocalDate.Date.AddDays(1));

    /// <summary>
    /// The last representable instant <b>inside</b> a clinic-local day, as UTC.
    /// <para>
    /// ⚠️ The distinction from <see cref="EndOfLocalDayUtc"/> is load-bearing, not cosmetic: that one is the *next*
    /// midnight (exclusive) while every money read — <c>GetCollectedBetweenAsync</c> and friends — is inclusive on
    /// <b>both</b> ends. Handing them the exclusive instant counts a payment recorded at exactly midnight in both
    /// adjacent periods (finding #20). The subtraction lives here once rather than at each call site.
    /// </para>
    /// </summary>
    public static DateTime LastTickOfLocalDayUtc(DateTime clinicLocalDate) =>
        EndOfLocalDayUtc(clinicLocalDate).AddTicks(-1);

    /// <summary>
    /// A clinic-local calendar day as the inclusive-on-both-ends UTC range the money reads expect (AC-P6.2).
    /// </summary>
    public static (DateTime From, DateTime ToInclusive) LocalDayRangeUtc(DateTime clinicLocalDate) =>
        (StartOfLocalDayUtc(clinicLocalDate), LastTickOfLocalDayUtc(clinicLocalDate));

    /// <summary>
    /// « Aujourd'hui » as an inclusive UTC range — the <b>single authority</b> for what a query means by today
    /// (AC-P6.2). Every read that used to default to <c>DateTime.UtcNow.Date</c> ran the clinic's day from 01:00
    /// to 01:00 Tunis (§ 4.1); they all go through here instead.
    /// </summary>
    public static (DateTime From, DateTime ToInclusive) TodayRangeUtc(DateTime? nowUtc = null) =>
        LocalDayRangeUtc(ClinicToday(nowUtc));

    // ---- Tunisian calendar months (FR-8b) --------------------------------------------------------------
    //
    // This class had day and year helpers only and no month concept at all, while two private copies of one
    // already existed elsewhere — a month-to-date range inside a platform query and a French month label on the
    // console's label helper. Both moved here. Month arithmetic is where a second copy is *least* visible: two
    // implementations agree for eleven months out of twelve.

    /// <summary>The `fr-FR` culture, pinned. A container's ambient culture would render « August 2026 ».</summary>
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>The `AAAA-MM` key of a clinic-local date — the month identity this feature speaks end to end.</summary>
    public static string MonthKey(DateTime clinicLocalDate) =>
        clinicLocalDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The month the clinic is currently in.</summary>
    public static string CurrentMonthKey(DateTime? nowUtc = null) => MonthKey(ClinicToday(nowUtc));

    /// <summary>An `AAAA-MM` key back to its year and month, or false. The one validator of a caller-supplied key.</summary>
    public static bool TryParseMonthKey(string? monthKey, out int year, out int month)
    {
        year = 0;
        month = 0;

        if (!DateTime.TryParseExact(
                monthKey, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        year = parsed.Year;
        month = parsed.Month;
        return true;
    }

    /// <summary>
    /// A <b>whole</b> clinic-local month as the inclusive-on-both-ends UTC range the money and counting reads expect.
    ///
    /// <para>⚠️ Deliberately <b>not</b> the same function as <see cref="MonthToDateRangeUtc"/>, and the two names are
    /// the guard: that one stops at the last tick of <i>today</i>. Collapsing them would widen a month-to-date
    /// caller's window by the rest of the month, which no test in this solution could see.</para>
    /// </summary>
    public static (DateTime From, DateTime ToInclusive) MonthRangeUtc(string monthKey)
    {
        var (year, month) = ParseMonthKeyOrThrow(monthKey);
        var firstDay = new DateTime(year, month, 1);

        return (StartOfLocalDayUtc(firstDay), LastTickOfLocalDayUtc(firstDay.AddMonths(1).AddDays(-1)));
    }

    /// <summary>
    /// The current month <b>to date</b>: the 1st through the last tick of <paramref name="clinicToday"/>.
    ///
    /// <para>The shape a « ce mois-ci » figure needs — a payment dated later this month has not been collected yet.
    /// See the warning on <see cref="MonthRangeUtc"/> for why this is a second primitive rather than an argument.</para>
    /// </summary>
    public static (DateTime From, DateTime ToInclusive) MonthToDateRangeUtc(DateTime clinicToday) => (
        StartOfLocalDayUtc(new DateTime(clinicToday.Year, clinicToday.Month, 1)),
        LastTickOfLocalDayUtc(clinicToday));

    /// <summary>« août 2026 ». The overload shape <c>PlatformAccessLabels.Month</c> had, so its call sites are unchanged.</summary>
    public static string MonthLabelFr(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", French);

    /// <summary>« août 2026 » for an `AAAA-MM` key.</summary>
    public static string MonthLabelFr(string monthKey)
    {
        var (year, month) = ParseMonthKeyOrThrow(monthKey);
        return MonthLabelFr(year, month);
    }

    /// <summary>The month after <paramref name="monthKey"/>.</summary>
    public static string NextMonthKey(string monthKey)
    {
        var (year, month) = ParseMonthKeyOrThrow(monthKey);
        return MonthKey(new DateTime(year, month, 1).AddMonths(1));
    }

    /// <summary>
    /// The <paramref name="count"/> months immediately before <paramref name="monthKey"/>, newest first.
    /// </summary>
    public static IReadOnlyList<string> PrecedingMonthKeys(string monthKey, int count)
    {
        var (year, month) = ParseMonthKeyOrThrow(monthKey);
        var anchor = new DateTime(year, month, 1);

        return Enumerable.Range(1, Math.Max(0, count)).Select(back => MonthKey(anchor.AddMonths(-back))).ToList();
    }

    /// <summary>
    /// The clinic-local day the next month opens on — when an allowance renews, as a bare calendar day.
    /// </summary>
    public static DateTime FirstDayOfNextMonth(DateTime clinicLocalDate) =>
        new DateTime(clinicLocalDate.Year, clinicLocalDate.Month, 1).AddMonths(1);

    private static (int Year, int Month) ParseMonthKeyOrThrow(string monthKey)
    {
        if (!TryParseMonthKey(monthKey, out var year, out var month))
        {
            throw new ArgumentException($"'{monthKey}' n'est pas un mois au format AAAA-MM.", nameof(monthKey));
        }

        return (year, month);
    }
}
