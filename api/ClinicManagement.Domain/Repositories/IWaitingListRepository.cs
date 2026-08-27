using ClinicManagement.Domain.Entities;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

public interface IWaitingListRepository
{
    Task<WaitingListEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's waiting-list entries (Patient nav included); when activeOnly, restricted to those
    /// still Waiting. Highest priority first, then oldest first.</summary>
    /// <summary>
    /// The salle d'attente, priority then oldest-first. <paramref name="searchTerm"/> is matched in SQL over the
    /// patient's name, the note and the desired timeframe.
    /// </summary>
    Task<PagedResult<WaitingListEntry>> GetByClinicIdAsync(
        Guid clinicId,
        bool activeOnly = true,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>How many patients are still <c>Waiting</c> in the clinic's salle d'attente.</summary>
    Task<int> CountWaitingAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<WaitingListEntry> AddAsync(WaitingListEntry entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(WaitingListEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
