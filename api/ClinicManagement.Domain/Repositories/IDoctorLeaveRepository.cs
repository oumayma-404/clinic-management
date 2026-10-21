using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IDoctorLeaveRepository
{
    Task<DoctorLeave?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every congé of one practitioner, earliest first. Unpaged: a handful a year.</summary>
    Task<IReadOnlyList<DoctorLeave>> GetByDoctorAsync(
        Guid clinicId, Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>The clinic's congés touching the inclusive day window, for the agenda.</summary>
    Task<IReadOnlyList<DoctorLeave>> GetOverlappingAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default);

    /// <summary>
    /// The first congé of <paramref name="doctorId"/> touching the inclusive window, or null — the booking guard and
    /// the overlap refusal both ask this. <paramref name="excludeId"/> keeps a congé from colliding with itself.
    /// </summary>
    Task<DoctorLeave?> FindOverlappingAsync(
        Guid clinicId,
        Guid doctorId,
        DateOnly fromDay,
        DateOnly toDay,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(DoctorLeave leave, CancellationToken cancellationToken = default);
    void Remove(DoctorLeave leave);
}
