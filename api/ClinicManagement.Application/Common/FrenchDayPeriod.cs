using System.Globalization;

namespace ClinicManagement.Application.Common;

/// <summary>An inclusive run of clinic-local days in French — the doctor congés and the secretary congés share it.</summary>
public static class FrenchDayPeriod
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Spelled out rather than read from the culture, so a host without ICU data words them the same.
    private static readonly string[] Months =
    [
        "janvier", "février", "mars", "avril", "mai", "juin",
        "juillet", "août", "septembre", "octobre", "novembre", "décembre",
    ];

    /// <summary>« du 12/08 au 20/08 », « le 12/08 »; the year appears only when the two ends differ in it.</summary>
    public static string Format(DateOnly start, DateOnly end)
    {
        var format = start.Year == end.Year ? "dd/MM" : "dd/MM/yyyy";
        return start == end
            ? $"le {start.ToString(format, French)}"
            : $"du {start.ToString(format, French)} au {end.ToString(format, French)}";
    }

    /// <summary>
    /// « le 16 novembre 2026 », « du 16 au 18 novembre 2026 », « du 30 novembre au 2 décembre 2026 », « du 30 décembre
    /// 2026 au 2 janvier 2027 » — always the year, since a bell row is read long after it was written.
    /// </summary>
    public static string Words(DateOnly start, DateOnly end)
    {
        if (start == end)
            return $"le {DayAndMonth(start)} {start.Year}";
        if (start.Year != end.Year)
            return $"du {DayAndMonth(start)} {start.Year} au {DayAndMonth(end)} {end.Year}";
        return start.Month == end.Month
            ? $"du {DayNumber(start)} au {DayAndMonth(end)} {end.Year}"
            : $"du {DayAndMonth(start)} au {DayAndMonth(end)} {end.Year}";
    }

    /// <summary>« 3 jours », « 1 jour », « 0,5 jour », « −3 jours » — plural from the size, never the sign.</summary>
    public static string Days(decimal days) => $"{Sign(days)}{Number(days)} {(Math.Abs(days) > 1 ? "jours" : "jour")}";

    /// <summary>« 2,5 j », « −1 j » — where the sentence already says it is days.</summary>
    public static string Short(decimal days) => $"{Sign(days)}{Number(days)} j";

    /// <summary>A balance: « 9 jours », or « 3 jours de dépassement » below zero.</summary>
    public static string Balance(decimal days) => days < 0 ? $"{Days(-days)} de dépassement" : Days(days);

    private static string Sign(decimal days) => days < 0 ? "−" : string.Empty;

    private static string Number(decimal days) => Math.Abs(days).ToString("0.#", French);

    private static string DayNumber(DateOnly day) => day.Day == 1 ? "1er" : day.Day.ToString(CultureInfo.InvariantCulture);

    private static string DayAndMonth(DateOnly day) => $"{DayNumber(day)} {Months[day.Month - 1]}";
}
