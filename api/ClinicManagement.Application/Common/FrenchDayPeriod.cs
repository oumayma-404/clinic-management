using System.Globalization;

namespace ClinicManagement.Application.Common;

/// <summary>An inclusive run of clinic-local days in French — the doctor congés and the secretary congés share it.</summary>
public static class FrenchDayPeriod
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« du 12/08 au 20/08 », « le 12/08 »; the year appears only when the two ends differ in it.</summary>
    public static string Format(DateOnly start, DateOnly end)
    {
        var format = start.Year == end.Year ? "dd/MM" : "dd/MM/yyyy";
        return start == end
            ? $"le {start.ToString(format, French)}"
            : $"du {start.ToString(format, French)} au {end.ToString(format, French)}";
    }

    /// <summary>« 3 jours », « 1 jour », « 0,5 jour » — a day count that may hold a half day.</summary>
    public static string Days(decimal days)
    {
        var text = days.ToString("0.#", French);
        return days > 1 ? $"{text} jours" : $"{text} jour";
    }
}
