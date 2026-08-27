using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// The single answer to « quelle séance cette fiche documente-t-elle ? ».
///
/// <para><b>Why this exists.</b> <c>DentalRecord.AppointmentId</c> has been persisted and indexed since
/// <c>AddDentalRecordAppointmentId</c>, and its own docstring says storing it is what makes « quelles séances
/// n'ont pas encore de fiche ? » answerable — but only <b>one</b> door ever populated it: the post-visit prompt's
/// deep link. A fiche charted the ordinary way, from the patient's page, stored <c>null</c>. So the question the
/// column exists to answer would have reported « pas de fiche » for visits that have one, on the majority of
/// them. This is the <c>fixes-dont-propagate</c> shape: a correct mechanism wired to one call site fewer than it
/// has.</para>
///
/// <para><b>The rule.</b> A caller-supplied id always wins — the deep link knows more than we can infer. With
/// none, exactly <b>one</b> candidate visit on the fiche's own clinic-local day is linked; <b>zero or several
/// leave it null</b>. Ambiguity is left unresolved rather than guessed, and that asymmetry is the whole design:
/// a missing link costs one row on a worklist, while a wrong link attaches a séance to another visit and
/// auto-completes it — a claim about a patient's day that nobody made and nobody will see.</para>
/// </summary>
public static class DentalRecordVisitLink
{
    /// <summary>
    /// Resolve the visit a fiche documents.
    /// </summary>
    /// <param name="supplied">The id the client sent, if any. Returned as-is when present — the caller is
    /// responsible for tenant-checking it, exactly as it already does.</param>
    /// <param name="patientId">Whose fiche this is.</param>
    /// <param name="clinicId">The caller's clinic. Every candidate is re-checked against it, so a patient
    /// record reached across a tenant boundary cannot pull in another practice's appointment.</param>
    /// <param name="interventionDate">The day the séance happened. Its <b>clinic-local</b> day is the window —
    /// Tunisia is UTC+1, so taking the raw UTC day would file an evening séance against the following day and
    /// find nothing.</param>
    public static async Task<Guid?> ResolveAsync(
        Guid? supplied,
        Guid patientId,
        Guid clinicId,
        DateTime interventionDate,
        IAppointmentRepository appointments,
        CancellationToken cancellationToken = default)
    {
        if (supplied.HasValue && supplied.Value != Guid.Empty)
        {
            return supplied;
        }

        // ⚠️ LocalDayRangeUtc takes a CLINIC-LOCAL date and the stored instant is UTC, so it has to be converted
        // first. Tunisia is UTC+1: handing the raw instant over would file a 23:30 séance against the next day
        // and find no candidate at all — silently, and only ever for the last hour of the evening.
        var (dayStartUtc, dayLastTickUtc) =
            ClinicClock.LocalDayRangeUtc(ClinicClock.ToClinicLocal(interventionDate));

        var candidates = await appointments.GetForPatientOnDayAsync(
            patientId, dayStartUtc, dayLastTickUtc, cancellationToken);

        // Defence in depth: the repository is clinic-filtered, but this read is keyed on a patient rather than on
        // a clinic, and every other read in this layer re-checks the aggregate it loaded.
        var mine = candidates.Where(a => a.ClinicId == clinicId).ToList();

        // Exactly one, or nothing. Two visits in a day is an ordinary Tunisian morning (a control at 9h, an
        // extraction at 14h) and picking either would be a coin toss the user never sees.
        return mine.Count == 1 ? mine[0].Id : null;
    }
}
