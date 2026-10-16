using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>« Modifications à vérifier » (<c>clinic-pc-copy</c> AC-5.6) — written by the return, read by admins.</summary>
public interface IRelayReviewItemRepository
{
    /// <summary>What one cut's returns already listed: a second attempt lists a record once, keeping the first versions.</summary>
    Task<IReadOnlyList<RelayReviewItem>> GetForCutAsync(Guid relayId, DateTime cutSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// One of the two lists, newest first: « À reprendre » (<paramref name="reEnter"/>) or « À vérifier »; the lines
    /// already marked only when asked for.
    /// </summary>
    Task<PagedResult<RelayReviewItem>> GetPageAsync(
        Guid clinicId, bool reEnter, bool includeReviewed, PageRequest? paging, CancellationToken cancellationToken = default);

    /// <summary>The lines of one list nobody has marked yet — what the admins' bell row and the card count.</summary>
    Task<int> CountPendingAsync(Guid clinicId, bool reEnter, CancellationToken cancellationToken = default);

    Task<RelayReviewItem?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<RelayReviewItem> items, CancellationToken cancellationToken = default);
}
