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

    /// <summary>The day of the cabinet's very first line, or null before anyone ever pointed — no absence exists before it.</summary>
    Task<DateOnly?> GetFirstDayAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>Whether the account has any line on <paramref name="day"/> — open or closed. Untracked.</summary>
    Task<bool> HasLineOnAsync(Guid clinicId, string userId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>The cabinet's lines of <paramref name="day"/> still open, untracked — the departure reminder reads them.</summary>
    Task<IReadOnlyList<AttendanceEntry>> ListOpenOnAsync(Guid clinicId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Her lines of days before <paramref name="day"/> still open, oldest first, tracked — the arrival declares them.</summary>
    Task<IReadOnlyList<AttendanceEntry>> ListOpenBeforeAsync(
        Guid clinicId, string userId, DateOnly day, CancellationToken cancellationToken = default);

    Task AddAsync(AttendanceEntry entry, CancellationToken cancellationToken = default);
}
