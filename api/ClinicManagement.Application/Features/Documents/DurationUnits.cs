namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The unit a duration is counted in on a document a practitioner issues — « pendant 12 jours », « un repos de
/// 6 mois ». Shared by the ordonnance (persisted as the <c>durationUnit</c> key on each entry of a
/// prescription's <c>content.medications</c> array) and by the certificat médical (the same key on its own
/// content), because it is the same question with the same two answers.
///
/// <para>
/// ⚠️ <b>An absent unit is jours.</b> Every line written before this key existed holds a day count — the field
/// was labelled « Durée (jours) » and the printed sentence appended « jour(s) » unconditionally — so
/// <see cref="Normalize"/> is what keeps an old document printing the words it always did.
/// </para>
/// </summary>
public static class DurationUnits
{
    public const string Days = "jours";
    public const string Months = "mois";

    /// <summary>The unit a stored value actually has: <see cref="Months"/> only when it says so.</summary>
    public static string Normalize(string? unit) =>
        string.Equals(unit?.Trim(), Months, StringComparison.OrdinalIgnoreCase)
            ? Months
            : Days;

    /// <summary>The word printed after the count — « mois » is invariable, « jour » takes the plural.</summary>
    public static string Label(string? unit, string? count)
    {
        if (Normalize(unit) == Months)
        {
            return Months;
        }

        return int.TryParse(count?.Trim(), out var days) && days <= 1 ? "jour" : "jours";
    }
}
