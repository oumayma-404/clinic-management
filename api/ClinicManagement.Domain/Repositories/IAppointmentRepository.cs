using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByClinicIdAsync(Guid clinicId, DateTime? startDate = null, DateTime? endDate = null, Guid? doctorId = null, CancellationToken cancellationToken = default);
    Task<int> CountByClinicIdAsync(Guid clinicId, DateTime? startDate = null, DateTime? endDate = null, AppointmentStatus? status = null, IReadOnlyCollection<AppointmentStatus>? excludeStatuses = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetUpcomingAppointmentsAsync(DateTime fromDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetAppointmentsForDateAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByProcedureTypeIdAsync(Guid procedureTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's appointments linked to any of the given treatment-plan items, oldest first (empty set ⇒
    /// empty result). One batched read so a plan — or a whole page of plans — derives each act's scheduling
    /// state without an N+1. Deliberately loads no navigations: callers need only id/date/status.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetByTreatmentPlanItemIdsAsync(
        Guid clinicId, IReadOnlyCollection<Guid> treatmentPlanItemIds, CancellationToken cancellationToken = default);

    Task<Appointment> AddAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



