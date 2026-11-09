using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IClinicRepository
{
    Task<Clinic?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Clinic?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Clinic?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<Clinic>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Clinic> AddAsync(Clinic clinic, CancellationToken cancellationToken = default);
    Task UpdateAsync(Clinic clinic, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Whether « Mode discret » is on. One column, no tracking — read on every guarded money request.</summary>
    Task<bool> IsMoneyHiddenAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the arrival lock is decided on, untracked and without the clinic's users — it is read on every save of a
    /// tracked secretary. Null for an unknown cabinet.
    /// </summary>
    Task<ClinicAttendanceSwitches?> GetAttendanceSwitchesAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>Every cabinet with the pointage on and its opening hours, untracked — the departure reminder's pass.</summary>
    Task<IReadOnlyList<ClinicAttendanceHours>> ListAttendanceHoursAsync(CancellationToken cancellationToken = default);
}

/// <summary>The pointage's two switches, the opening hours they are judged against, and when the lock was armed.</summary>
public sealed record ClinicAttendanceSwitches(
    bool AttendanceEnabled, bool LockEnabled, string? WorkingHoursJson, DateTime? LockArmedAtUtc = null);

/// <summary>A cabinet whose pointage is on, and the opening hours its closing time is read from.</summary>
public sealed record ClinicAttendanceHours(Guid ClinicId, string? WorkingHoursJson);




