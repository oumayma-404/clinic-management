using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IStaffQuickMessageListRepository
{
    /// <summary>The cabinet's saved list, tracked; null when it still uses the defaults.</summary>
    Task<StaffQuickMessageList?> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(StaffQuickMessageList list, CancellationToken cancellationToken = default);

    void Remove(StaffQuickMessageList list);
}
