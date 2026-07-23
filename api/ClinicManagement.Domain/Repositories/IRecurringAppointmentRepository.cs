using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IRecurringAppointmentRepository
{
    Task<RecurringAppointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's recurring series (newest first). <paramref name="activeOnly"/> restricts to active series.</summary>
    Task<IEnumerable<RecurringAppointment>> GetByClinicIdAsync(Guid clinicId, bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<RecurringAppointment> AddAsync(RecurringAppointment series, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringAppointment series, CancellationToken cancellationToken = default);
}
