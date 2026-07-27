using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Why a patient may not be archived, in French. Archiving hides someone from every list — it must not become a
/// way to make an unpaid balance vanish from « Créances » or a booked visit vanish from the calendar.
///
/// Lives in the handler layer, not on <see cref="Domain.Entities.Patient"/>, because the patient aggregate holds
/// no invoices or treatment plans — the same reason the billed-plan block sits in the amend handler.
/// </summary>
public static class PatientArchiveRules
{
    /// <summary>Null when archiving is allowed; otherwise the reason, ready to display.</summary>
    public static string? DescribeBlockers(PatientArchiveBlockers blockers)
    {
        if (!blockers.Any)
        {
            return null;
        }

        var reasons = new List<string>();

        if (blockers.TotalOutstanding > 0m)
        {
            reasons.Add($"un solde de {blockers.TotalOutstanding:0.000} DT reste dû");
        }

        if (blockers.FutureAppointments > 0)
        {
            reasons.Add(blockers.FutureAppointments == 1
                ? "un rendez-vous à venir est programmé"
                : $"{blockers.FutureAppointments} rendez-vous à venir sont programmés");
        }

        return "Archivage impossible : " + string.Join(" et ", reasons) + ".";
    }
}
