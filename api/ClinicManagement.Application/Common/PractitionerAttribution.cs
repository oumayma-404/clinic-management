using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common;

/// <summary>
/// « Qui a produit ceci ? » — the single answer, shared by every write path that attributes money or clinical work
/// to a practitioner (L9).
///
/// <para><b>Why one resolver.</b> Attribution has three possible sources and a strict precedence between them, and
/// the ordering is the whole content of the rule: an explicitly named practitioner beats the visit's, and the
/// visit's beats « whoever is logged in ». Six write paths each re-deriving that would be six chances to prefer the
/// caller over the appointment — which quietly credits the receptionist's linked <c>Doctor</c> record with a
/// dentist's work.</para>
///
/// <para>⚠️ <b>It validates the doctor against the caller's clinic</b>, and returns <c>null</c> rather than throwing
/// for one that does not belong. The alternative — accepting it — would let a crafted request attribute this
/// clinic's revenue to another practice's practitioner, which is exactly the class of defect the L9 FK exists to
/// make impossible at the database level; this is the same guard one layer up, where a French refusal is
/// available.</para>
///
/// <para>⚠️ <b>Null is a first-class answer, not a failure.</b> A visit booked with no practitioner (a « créneau
/// occupé », a walk-in recorded by reception) genuinely has none, and inventing one would be worse than admitting
/// it: every read tolerates null, and the migration's backfill deliberately leaves such rows unattributed.</para>
/// </summary>
public static class PractitionerAttribution
{
    /// <summary>
    /// The practitioner to attribute a new record to, in precedence order:
    /// <list type="number">
    ///   <item><paramref name="explicitDoctorId"/> — somebody said so on this request.</item>
    ///   <item>the practitioner on <paramref name="appointmentDoctorId"/> — the visit the work was done at.</item>
    ///   <item><paramref name="callerDoctorId"/> — the logged-in user's own <c>Doctor</c> record, when they have one.</item>
    /// </list>
    /// Each candidate is checked against <paramref name="clinicDoctorIds"/> before it is accepted, so a stale or
    /// cross-clinic id falls through to the next source rather than being stored.
    /// </summary>
    public static Guid? Resolve(
        Guid? explicitDoctorId,
        Guid? appointmentDoctorId,
        Guid? callerDoctorId,
        IReadOnlySet<Guid> clinicDoctorIds)
    {
        foreach (var candidate in new[] { explicitDoctorId, appointmentDoctorId, callerDoctorId })
        {
            if (candidate is { } id && id != Guid.Empty && clinicDoctorIds.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// The clinic's practitioner ids as a set, for <see cref="Resolve"/>. One read per request rather than a
    /// per-candidate <c>GetByIdAsync</c>: the roster of a dental practice is a handful of rows, and three
    /// round trips to validate three candidates would be three chances to forget one.
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> LoadClinicDoctorIdsAsync(
        IDoctorRepository doctorRepository, Guid clinicId, CancellationToken cancellationToken = default)
    {
        var doctors = await doctorRepository.GetByClinicIdAsync(clinicId, cancellationToken);
        return doctors.Select(d => d.Id).ToHashSet();
    }
}
