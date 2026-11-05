using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

public interface IStaffLeaveRequestRepository
{
    Task<StaffLeaveRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cabinet's requests: <paramref name="pendingOnly"/> true lists those waiting (oldest first), false those
    /// decided or withdrawn (latest start first), null all of them (waiting first). <paramref name="status"/> narrows
    /// to one status.
    /// </summary>
    Task<PagedResult<StaffLeaveRequest>> ListAsync(
        Guid clinicId, bool? pendingOnly, StaffLeaveStatus? status, PageRequest? paging,
        CancellationToken cancellationToken = default);

    Task<int> CountPendingAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>Every request of one account, latest start first. Unpaged: a handful a year.</summary>
    Task<IReadOnlyList<StaffLeaveRequest>> GetByUserAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default);

    /// <summary>The first waiting or accepted request of the account touching the inclusive window, or null.</summary>
    Task<StaffLeaveRequest?> FindHoldingOverlapAsync(
        Guid clinicId, string userId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default);

    /// <summary>Accepted requests of anyone in the cabinet touching the inclusive window.</summary>
    Task<IReadOnlyList<StaffLeaveRequest>> GetApprovedOverlappingAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default);

    Task AddAsync(StaffLeaveRequest request, CancellationToken cancellationToken = default);
}
