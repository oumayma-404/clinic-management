using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IAttendanceEntryRepository
{
    Task<AttendanceEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Her latest line without a departure, whatever its day, or null.</summary>
    Task<AttendanceEntry?> FindLatestOpenAsync(Guid clinicId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Every line of the cabinet whose day falls in the inclusive window, optionally for one account.</summary>
    Task<IReadOnlyList<AttendanceEntry>> ListBetweenAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, string? userId = null, CancellationToken cancellationToken = default);

    Task AddAsync(AttendanceEntry entry, CancellationToken cancellationToken = default);
}
