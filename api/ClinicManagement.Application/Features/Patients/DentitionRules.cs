using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// The single authority on "which dentition would you assume for a patient this age".
///
/// <para>
/// Used in three places that must agree: the default offered by the add-patient form, the fallback when a
/// server-internal creator (the AI dispatcher, the Google→App sync's placeholder patient) supplies no dentition, and
/// the one-off backfill in <c>AddPatientDentition</c>. If those three disagreed, the same patient would be charted on
/// different teeth depending on which door they came in through.
/// </para>
///
/// <para>
/// It is a *default*, never a constraint — <c>Patient.SetDentition</c> takes whatever the dentist chooses. A
/// fourteen-year-old with retained deciduous teeth is a real case and the form must be able to say so.
/// </para>
/// </summary>
public static class DentitionRules
{
    /// <summary>
    /// Age at which the permanent set is assumed complete. Twelve-to-thirteen is when the second molars are through,
    /// so from this birthday on the adult chart is the right default.
    /// </summary>
    public const int AdultFromAgeYears = 13;

    /// <summary>
    /// The dentition to assume for someone born on <paramref name="dateOfBirth"/>, or <c>null</c> when there is no
    /// date of birth to reason from — « demandez, n'assumez pas ».
    ///
    /// <para>
    /// ⚠️ <b>Null is the answer, not a failure to produce one.</b> A walk-in registered with nothing but a name has no
    /// recorded birthday, and this used to receive a fabricated « thirty years ago » instead — so every such patient
    /// was silently charted on adult teeth, which is exactly wrong for the paediatric case the field exists for. The
    /// client mirror (<c>dentitionFromBirthdate</c>) has always returned null here for the same reason.
    /// </para>
    ///
    /// <para>
    /// Age is computed against the <b>clinic-local</b> calendar day, not UTC: a patient whose thirteenth birthday is
    /// today would otherwise still read as twelve for the first hour of every Tunisian day. Same reason every other
    /// calendar comparison in this layer goes through <see cref="ClinicClock"/>.
    /// </para>
    /// </summary>
    public static DentitionType? FromDateOfBirth(DateTime? dateOfBirth, DateTime? nowUtc = null)
    {
        if (dateOfBirth is not { } born)
        {
            return null;
        }

        var today = ClinicClock.ClinicToday(nowUtc);
        var dob = ClinicClock.ToClinicLocal(
            born.Kind == DateTimeKind.Utc ? born : DateTime.SpecifyKind(born, DateTimeKind.Utc)).Date;

        // Whole years elapsed: subtract one when this year's birthday has not arrived yet.
        var age = today.Year - dob.Year;
        if (dob.Date > today.AddYears(-age))
        {
            age--;
        }

        return age >= AdultFromAgeYears ? DentitionType.Adult : DentitionType.Child;
    }

    /// <summary>
    /// Parse the wire value. Returns null for an unrecognised or absent string so the caller can fall back to
    /// <see cref="FromDateOfBirth"/> rather than silently charting a child on adult teeth.
    /// </summary>
    public static DentitionType? Parse(string? value) =>
        Enum.TryParse<DentitionType>(value, ignoreCase: true, out var parsed) ? parsed : null;
}
