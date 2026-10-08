using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>« Modifications à vérifier » (<c>clinic-pc-copy</c> AC-5.6) — written by the return, read by admins.</summary>
public interface IRelayReviewItemRepository
{
    /// <summary>What one cut's returns already listed: a second attempt lists a record once, keeping the first versions.</summary>
    Task<IReadOnlyList<RelayReviewItem>> GetForCutAsync(Guid relayId, DateTime cutSinceUtc, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<RelayReviewItem> items, CancellationToken cancellationToken = default);
}
