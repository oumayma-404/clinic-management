using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// Which dentist a booking with a patient is for — the one rule every command that books asks
/// (<c>cabinet-de-groupe</c> US-1).
///
/// <para><b>Why it exists.</b> A patient RDV with no dentist cannot reach anyone with « Patient arrivé », credits the
/// fiche a secretary writes to nobody, and — under L2b — blocks the slot for every chair in the cabinet. Measured on
/// prod 2026-11-16: 90 of the 121 RDV secretaries booked since September named no dentist, in one-dentist cabinets,
/// because the booking dialog pre-picks the signed-in dentist and a secretary is not one.</para>
///
/// <para>The rule: a booking that names a dentist, or has no patient (a « créneau bloqué », which is how a lunch break
/// blocks every chair), is left alone. A patient booking that names none gets the cabinet's only active practitioner
/// when there is exactly one, and is refused with <see cref="RequiredCode"/> when there are several — guessing which
/// of three dentists a patient is for is not something the server can do.</para>
///
/// <para>⚠️ On an update it applies only when the save <b>sends</b> the dentist field: a drag, « Reporter », a status
/// change or a lateness never do, and must keep working on an old RDV that has none.
/// <c>AppointmentPractitionerCoverageTests</c> fails when a file building an <c>Appointment</c> does not ask this.</para>
/// </summary>
public static class AppointmentPractitioner
{
    /// <summary>The refusal's code: a client branches on it, never on the sentence.</summary>
    public const string RequiredCode = "practitioner_required";

    public const string RequiredMessage =
        "Choisissez le praticien de ce rendez-vous : le cabinet en compte plusieurs.";

    /// <summary>
    /// The dentist the booking should carry: <paramref name="requestedDoctorId"/> when it names one or when there is
    /// no patient, else the only active practitioner, else null when the cabinet has none, else a refusal.
    /// </summary>
    public static async Task<Result<Guid?>> ResolveAsync(
        IDoctorRepository doctors,
        Guid clinicId,
        Guid? patientId,
        Guid? requestedDoctorId,
        CancellationToken cancellationToken)
    {
        if (requestedDoctorId.HasValue || patientId is null)
        {
            return Result<Guid?>.Success(requestedDoctorId);
        }

        var roster = await doctors.GetByClinicIdAsync(clinicId, cancellationToken) ?? [];
        var active = roster.Where(d => d.ClinicId == clinicId && d.IsActive).Select(d => d.Id).Take(2).ToList();

        return active.Count switch
        {
            0 => Result<Guid?>.Success(null),
            1 => Result<Guid?>.Success(active[0]),
            _ => Result<Guid?>.Failure(RequiredMessage, RequiredCode),
        };
    }
}
