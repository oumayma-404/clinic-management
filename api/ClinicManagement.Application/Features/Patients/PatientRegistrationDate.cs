using System.Globalization;
using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// « Créé le » carried over from a previous software: the ONE rule for which day is acceptable and which instant it
/// becomes. Read by <see cref="PatientFromRequest.Build"/> (every creation, the import script's included) and by the
/// CSV import's row reader, so the dry run refuses exactly what the save would.
/// </summary>
public static class PatientRegistrationDate
{
    /// <summary>Before this, a file-opening date is a typo rather than a record.</summary>
    public static readonly DateTime Earliest = new(1900, 1, 1);

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>The French refusal for this day, or <c>null</c> when it is acceptable.</summary>
    public static string? Refusal(DateTime registeredOn, DateTime? dateOfBirth, DateTime clinicToday)
    {
        var day = registeredOn.Date;
        var shown = day.ToString("dd/MM/yyyy", Fr);

        if (day > clinicToday.Date)
        {
            return $"La date de création du dossier ({shown}) est dans le futur.";
        }

        if (day < Earliest)
        {
            return $"La date de création du dossier ({shown}) est invraisemblable.";
        }

        if (dateOfBirth is { } born && day < born.Date)
        {
            return $"La date de création du dossier ({shown}) précède la date de naissance "
                   + $"({born.Date.ToString("dd/MM/yyyy", Fr)}).";
        }

        return null;
    }

    /// <summary>
    /// The stored instant for a calendar day: <b>noon in Tunis</b>, never later than now.
    ///
    /// <para>⚠️ Noon and not midnight, because the creation date has two kinds of reader: the clinic-local ones
    /// (« Nouveaux patients », the CSV export's « Inscrit le ») and any reader of the bare UTC date. Tunis midnight
    /// is 23:00 UTC on the <i>previous</i> day, so the second kind would print the day before; noon is the same day
    /// under both. Capped at <paramref name="nowUtc"/> so « today » never stores an instant still to come.</para>
    /// </summary>
    public static DateTime InstantFor(DateTime registeredOn, DateTime nowUtc)
    {
        var noon = ClinicClock.ToUtc(registeredOn.Date.AddHours(12));
        return noon < nowUtc ? noon : nowUtc;
    }
}
