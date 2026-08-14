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

    Task<Appointment> AddAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



