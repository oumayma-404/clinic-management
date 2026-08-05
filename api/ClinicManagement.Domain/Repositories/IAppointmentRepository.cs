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

    Task<Appointment> AddAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



