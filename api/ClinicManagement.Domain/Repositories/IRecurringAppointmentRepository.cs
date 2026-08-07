using ClinicManagement.Domain.Entities;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

public interface IRecurringAppointmentRepository
{
    Task<RecurringAppointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's recurring series (newest first). <paramref name="activeOnly"/> restricts to active series.</summary>
    /// <summary>
    /// The clinic's recurring series, newest first. <paramref name="searchTerm"/> is matched in SQL over the
    /// patient's name, the practitioner name and the notes.
    /// </summary>
    Task<PagedResult<RecurringAppointment>> GetByClinicIdAsync(
        Guid clinicId,
        bool activeOnly = true,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    Task<RecurringAppointment> AddAsync(RecurringAppointment series, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringAppointment series, CancellationToken cancellationToken = default);
}
