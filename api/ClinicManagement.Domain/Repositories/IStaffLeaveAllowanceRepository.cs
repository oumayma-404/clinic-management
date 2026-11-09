using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The « Droit à congé » lines. Neither read is paged: every caller folds them (<c>StaffLeaveLedger</c>), and a fold over
/// a page is not a fold. Both are untracked and ordered by day, then by when they were written.
/// </summary>
public interface IStaffLeaveAllowanceRepository
{
    Task<IReadOnlyList<StaffLeaveAllowanceEntry>> GetForUserAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default);

    /// <summary>The whole cabinet's lines in one read — the team overview groups them by account.</summary>
    Task<IReadOnlyList<StaffLeaveAllowanceEntry>> GetForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, for a cancellation.</summary>
    Task<StaffLeaveAllowanceEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(StaffLeaveAllowanceEntry entry, CancellationToken cancellationToken = default);
}
