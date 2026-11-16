using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Enums;
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
///
/// <para>⚠️ <b>It is asked twice, and the second time is not optional.</b> <see cref="ResolveAsync"/> answers when
/// the fiche is created. But a walk-in is charted first and booked after — production, November 2026: a fiche saved
/// at 12:08, its visit booked at 12:31 for that same morning — so the fiche stayed unlinked for good, « À clôturer »
/// asked for a fiche the patient already had, and <c>verify-schema</c>'s <c>dental-record-visit-links-backfill</c>
/// failed the next deploy.
/// <c>DentalRecordVisitLinks</c> (Infrastructure) now asks again inside every save that books, moves, cancels or
/// reassigns a visit, or saves a fiche still without one — with <see cref="DaysToAskAgain"/>,
/// <see cref="CanBeDocumented"/> and <see cref="OnlyCandidate"/> below, so both askings are this one rule.</para>
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
        return OnlyCandidate(candidates.Where(a => a.ClinicId == clinicId).Select(a => a.Id));
    }

    /// <summary>
    /// Whether a visit in this status can be the séance a fiche documents. A cancelled visit and a missed one are
    /// complete answers about a visit that did not happen. The SQL twins are <c>Status NOT IN (5, 6)</c> in the
    /// backfill migrations and in <c>verify-schema</c>, and <c>GetForPatientOnDayAsync</c>'s filter.
    /// </summary>
    public static bool CanBeDocumented(AppointmentStatus status) =>
        status is not (AppointmentStatus.Cancelled or AppointmentStatus.NoShow);

    /// <summary>The clinic-local day an instant falls on — the window both sides of the rule are compared in.</summary>
    public static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(ClinicClock.ToClinicLocal(utc));

    /// <summary>
    /// The visit a day's fiches document: its only candidate, or none. Two visits in a day is an ordinary Tunisian
    /// morning (a control at 9h, an extraction at 14h) and picking either would be a coin toss the user never sees.
    /// </summary>
    public static Guid? OnlyCandidate(IEnumerable<Guid> candidateIds)
    {
        var distinct = candidateIds.Distinct().Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>
    /// The days whose answer a change to one visit may have changed. <paramref name="before"/> is null for a new
    /// visit, <paramref name="after"/> null for a deleted one.
    ///
    /// <para>A visit that stays a candidate for the same patient on the same day changes nothing — which is every
    /// write <c>AppointmentProgressJob</c> makes, so the minutely status pass costs no query here. Otherwise the
    /// day it left is asked again (a cancelled twin may leave exactly one) and so is the day it reached (it may be
    /// that day's only visit).</para>
    /// </summary>
    public static IReadOnlyList<FicheDay> DaysToAskAgain(VisitFacts? before, VisitFacts? after)
    {
        var left = before?.AsFicheDay();
        var reached = after?.AsFicheDay();

        if (left == reached)
        {
            return Array.Empty<FicheDay>();
        }

        var days = new List<FicheDay>(2);
        if (left is { } l)
        {
            days.Add(l);
        }

        if (reached is { } r)
        {
            days.Add(r);
        }

        return days;
    }

    /// <summary>What the rule reads off a visit: whose it is, where, when, and in what status.</summary>
    public readonly record struct VisitFacts(Guid ClinicId, Guid? PatientId, DateTime AtUtc, AppointmentStatus Status)
    {
        /// <summary>The day this visit is a candidate on, or null when it cannot be one — no patient, or a status
        /// that says the visit did not happen.</summary>
        public FicheDay? AsFicheDay() =>
            PatientId is { } patientId && CanBeDocumented(Status)
                ? new FicheDay(ClinicId, patientId, DayOf(AtUtc))
                : null;
    }

    /// <summary>One patient's clinic-local day in one clinic — the unit the rule is decided on.</summary>
    public readonly record struct FicheDay(Guid ClinicId, Guid PatientId, DateOnly Day);
}
