using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Expenses;

/// <summary>
/// The month arithmetic behind a <c>RecurringExpense</c>: which months it still owes, and what day each one falls
/// on. Pure and static, so the one behaviour that is entirely a calendar boundary is checkable without a clock,
/// a database or a job.
///
/// <para>⚠️ <b>Every day here is a day in the CABINET's calendar</b>, resolved through <see cref="ClinicClock"/>.
/// A monthly dépense posted from <c>DateTime.UtcNow</c> would land on the 31st of the previous month for the first
/// hour of every Tunisian day — the one defect a recurring poster cannot be allowed to make, because nobody is
/// watching when it runs.</para>
/// </summary>
public static class MonthlyExpenseSchedule
{
    public const int MinDayOfMonth = 1;
    public const int MaxDayOfMonth = 31;

    public const string DayOutOfRange = "Le jour du mois doit être compris entre 1 et 31.";

    /// <summary>
    /// A structural bound on the catch-up walk, not a business rule: a corrupt <c>LastPostedMonth</c> must cost a
    /// truncated pass rather than an endless one. Ten years is far past any real gap — the marker is set to the
    /// month the series was created in, so the true ceiling is « how long was the PC switched off ».
    /// </summary>
    private const int MaxCatchUpMonths = 120;

    /// <summary>The French refusal for a day outside 1–31, or null.</summary>
    public static string? RefuseDayOfMonth(int dayOfMonth) =>
        dayOfMonth is < MinDayOfMonth or > MaxDayOfMonth ? DayOutOfRange : null;

    /// <summary>The <c>AAAA-MM</c> month a dépense's stored instant belongs to, in the cabinet's calendar.</summary>
    public static string MonthOf(DateTime expenseDateUtc) =>
        ClinicClock.MonthKey(ClinicClock.ToClinicLocal(expenseDateUtc).Date);

    /// <summary>The day of the month a dépense's stored instant falls on, in the cabinet's calendar.</summary>
    public static int DayOfMonthOf(DateTime expenseDateUtc) =>
        ClinicClock.ToClinicLocal(expenseDateUtc).Date.Day;

    /// <summary>
    /// The months a series still owes: everything strictly after <paramref name="lastPostedMonth"/> up to and
    /// including <paramref name="currentMonth"/>, oldest first. Empty when the series is up to date — and empty,
    /// not backwards, when the marker is somehow ahead of today.
    ///
    /// <para>It returns a LIST rather than one month because a clinic PC switched off for a quarter comes back
    /// owing three loyers, and « post the current month » would silently swallow the other two.</para>
    /// </summary>
    public static IReadOnlyList<string> DueMonths(string lastPostedMonth, string currentMonth)
    {
        if (!ClinicClock.TryParseMonthKey(lastPostedMonth, out _, out _)
            || !ClinicClock.TryParseMonthKey(currentMonth, out _, out _))
        {
            return Array.Empty<string>();
        }

        var due = new List<string>();
        var month = ClinicClock.NextMonthKey(lastPostedMonth);

        while (string.CompareOrdinal(month, currentMonth) <= 0 && due.Count < MaxCatchUpMonths)
        {
            due.Add(month);
            month = ClinicClock.NextMonthKey(month);
        }

        return due;
    }

    /// <summary>
    /// The instant to date a month's occurrence, with the day <b>clamped to the month's own length</b>: a series
    /// on the 31st posts on the 28th in February rather than throwing or skipping the month.
    /// </summary>
    public static DateTime PostingDateUtc(string monthKey, int dayOfMonth)
    {
        if (!ClinicClock.TryParseMonthKey(monthKey, out var year, out var month))
        {
            throw new ArgumentException($"'{monthKey}' n'est pas un mois au format AAAA-MM.", nameof(monthKey));
        }

        var day = Math.Clamp(dayOfMonth, MinDayOfMonth, DateTime.DaysInMonth(year, month));
        return ClinicClock.StartOfLocalDayUtc(new DateTime(year, month, day));
    }
}
