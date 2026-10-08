using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>« Modifications à vérifier » (<c>clinic-pc-copy</c> AC-5.6) — written by the return, read by admins.</summary>
public interface IRelayReviewItemRepository
{
    /// <summary>What one cut's returns already listed: a second attempt lists a record once, keeping the first versions.</summary>
    Task<IReadOnlyList<RelayReviewItem>> GetForCutAsync(Guid relayId, DateTime cutSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>The cabinet's lines, newest first; the ones already « Vu » only when asked for.</summary>
    Task<PagedResult<RelayReviewItem>> GetPageAsync(
        Guid clinicId, bool includeReviewed, PageRequest? paging, CancellationToken cancellationToken = default);

    /// <summary>The lines nobody has marked « Vu » yet — what the admins' bell row counts.</summary>
    Task<int> CountPendingAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<RelayReviewItem?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<RelayReviewItem> items, CancellationToken cancellationToken = default);
}
