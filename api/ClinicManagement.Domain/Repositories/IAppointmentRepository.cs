using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByClinicIdAsync(Guid clinicId, DateTime? startDate = null, DateTime? endDate = null, Guid? doctorId = null, Guid? patientId = null, CancellationToken cancellationToken = default);
    Task<int> CountByClinicIdAsync(Guid clinicId, DateTime? startDate = null, DateTime? endDate = null, AppointmentStatus? status = null, IReadOnlyCollection<AppointmentStatus>? excludeStatuses = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many appointments the clinic has in <c>[from, toInclusive]</c>, broken down by status — one
    /// <c>GROUP BY</c> rather than one <c>COUNT</c> per status. The dashboard needs honoured, missed and
    /// cancelled counts *and* the total over the same window (the taux d'absence denominator), so four separate
    /// counts would be four round trips that could also disagree with each other if the window drifted between
    /// them. A status with no rows is simply absent from the dictionary — callers read it as zero.
    /// </summary>
    Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the clinic's work in <c>[from, toInclusive]</c> was actually made of, as one <c>GROUP BY</c> over the
    /// séances' <b>acts</b> — count and total minutes per act type.
    ///
    /// <para>Fans out over <c>AppointmentProcedure</c>, not over appointments: a visit routinely carries several
    /// acts, so this answers « de quoi ma journée est-elle faite ? » where <see cref="CountByStatusBetweenAsync"/>
    /// answers « combien de visites ». The two figures deliberately differ, and every caller labels this one
    /// « actes ».</para>
    ///
    /// <para><b>Cancelled and no-show visits are excluded</b>, matching every other « work done » figure on the
    /// dashboard: an act nobody performed is not part of the mix. <paramref name="doctorId"/> narrows to one
    /// practitioner's own séances (null = the whole cabinet).</para>
    ///
    /// <para>Grouped on <c>(ProcedureTypeId, ProcedureName)</c> and merged by id afterwards, rather than grouped
    /// on a conditional live-else-snapshot expression: the simple key is guaranteed to translate, and the caller
    /// has to overlay the live catalogue values anyway.</para>
    /// </summary>
    Task<IReadOnlyList<ProcedureMixRow>> GetProcedureMixBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetUpcomingAppointmentsAsync(DateTime fromDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetAppointmentsForDateAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByProcedureTypeIdAsync(Guid procedureTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's appointments linked to any of the given treatment-plan items, oldest first (empty set ⇒
    /// empty result). One batched read so a plan — or a whole page of plans — derives each act's scheduling
    /// state without an N+1. Deliberately loads no navigations: callers need only id/date/status.
    /// </summary>
    /// <summary>
    /// How many appointments belong to each of the given recurring series, as one <c>GROUP BY</c>.
    /// <para>Added when the series list was paginated: the handler used to read every appointment in the clinic
    /// and group them in memory, so a page of 25 series still cost a full-table read — paging the outer list
    /// while its companion read stayed unbounded is paging that does not bound anything.</para>
    /// Series with no appointments are simply absent from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByRecurringSeriesAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Appointment>> GetByTreatmentPlanItemIdsAsync(
        Guid clinicId, IReadOnlyCollection<Guid> treatmentPlanItemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every appointment whose slot is running <b>right now</b> and that nobody has started — across clinics, for
    /// the minutely progress pass. Empty is the ordinary answer.
    ///
    /// <para><b>The window is cut in SQL, the end is checked in memory, and that split is forced.</b>
    /// <c>Duration</c> is persisted as <b>ticks</b> (a <c>bigint</c> behind a value converter), so
    /// <c>AppointmentDateTime + Duration</c> has no translation — the database's own
    /// <c>AppointmentEndDateTime</c> column exists for the double-booking constraint but is deliberately unmapped.
    /// So the query narrows to visits that began within <paramref name="longestVisit"/> and the exact end is
    /// applied to that bounded set.</para>
    ///
    /// <para>The residual is stated rather than hidden: a booking <i>longer</i> than
    /// <paramref name="longestVisit"/> is not picked up, and simply keeps the status it has today. The error only
    /// ever runs that way — no appointment outside its own slot is returned.</para>
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetRunningNotStartedAsync(
        DateTime nowUtc, TimeSpan longestVisit, CancellationToken cancellationToken = default);

    /// <summary>
    /// The mirror image of <see cref="GetRunningNotStartedAsync"/>: every appointment whose slot has <b>ended</b>
    /// and which is still open (<c>Scheduled</c> / <c>Confirmed</c> / <c>InProgress</c>), across clinics, for the
    /// same minutely pass. The same SQL-window / in-memory-end split applies, and for the same forced reason.
    ///
    /// <para><b>It deliberately does not filter on <c>PatientId</c>.</b> The pass needs both kinds and routes them
    /// differently — a patient-bearing visit becomes « Séance passée », a « créneau occupé » is simply closed —
    /// so narrowing here would leave blocked slots reading « En cours » for ever, which is the defect this
    /// exists to fix.</para>
    ///
    /// <para>The residual is stated rather than hidden: a visit older than <paramref name="lookback"/> keeps
    /// whatever status it holds. It still appears on « À clôturer », which is where an unanswered visit belongs;
    /// the pass corrects the window a practice actually looks at.</para>
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetElapsedOpenAsync(
        DateTime nowUtc, TimeSpan lookback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Candidates for « à clôturer »: this clinic's patient-bearing visits that started in
    /// <c>[fromUtc, nowUtc]</c> and are neither <c>Cancelled</c> nor <c>NoShow</c> — both of which are complete
    /// answers rather than gaps.
    ///
    /// <para><b>The exact end-of-slot test is the CALLER's, in memory</b>, and that split is forced for
    /// <see cref="GetRunningNotStartedAsync"/>'s reason: <c>Duration</c> is persisted as <b>ticks</b> behind a
    /// value converter, so <c>AppointmentDateTime + Duration</c> has no translation, and the database's own
    /// <c>AppointmentEndDateTime</c> column (trigger-maintained for the double-booking constraint) is deliberately
    /// unmapped. What makes that safe here is the window: <c>fromUtc</c> is a bounded number of clinic-local days,
    /// so the set the caller filters is a clinic's recent agenda rather than its history.</para>
    ///
    /// <para>Ordered oldest-first with <c>Id</c> as a unique tie-break, because the caller pages this: <c>OFFSET</c>
    /// over a non-unique sort can show a row on two pages and skip another, which reads as « une séance a
    /// disparu ». Procedures are included — a row names the acts the séance was booked for.</para>
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetClosureCandidatesAsync(
        Guid clinicId,
        DateTime fromUtc,
        DateTime nowUtc,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// This patient's non-cancelled, non-missed appointments whose slot <b>starts</b> inside
    /// <c>[dayStartUtc, dayLastTickUtc]</c> — the candidate set behind « which visit does this fiche document? ».
    /// Both bounds are <b>inclusive</b>, matching what <c>ClinicClock.LocalDayRangeUtc</c> hands the caller.
    ///
    /// <para>Deliberately returns every candidate rather than picking one: the rule that exactly one candidate
    /// may be linked, and that zero or several leave the link null, belongs to
    /// <c>Application/Features/Patients/DentalRecordVisitLink</c> and must not be re-expressed as a
    /// <c>FirstOrDefault</c> here — guessing links a fiche to the wrong visit and auto-completes it.</para>
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetForPatientOnDayAsync(
        Guid patientId,
        DateTime dayStartUtc,
        DateTime dayLastTickUtc,
        CancellationToken cancellationToken = default);

    Task<Appointment> AddAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



