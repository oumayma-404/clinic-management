using ClinicManagement.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// The one place a filtered query becomes a <see cref="PagedResult{T}"/>. Every paginated repository read ends
/// with a call to this, so the count-then-page pair is written once.
/// </summary>
public static class PagedQueryExtensions
{
    /// <summary>
    /// Count the filtered set, then fetch one page of it.
    ///
    /// <para>The query passed in must already carry its filters, its <c>Include</c>s <b>and its ordering</b>.
    /// The ordering is a precondition rather than a parameter because it has to end on a unique column and only
    /// the repository knows which one: <c>OFFSET</c>/<c>LIMIT</c> over a non-unique sort is free to return a row
    /// on two adjacent pages and omit another entirely, since nothing obliges PostgreSQL to break ties the same
    /// way twice. An unordered paged read is silently, intermittently wrong — the failure mode that looks like
    /// "a record vanished".</para>
    ///
    /// <para>Two round trips are deliberate. A windowed <c>COUNT(*) OVER ()</c> would fold them into one, but it
    /// returns nothing at all when the page is empty — so the last page of a list someone else just shortened
    /// would report a total of zero and the pager would collapse to « 0 résultats » over rows that exist.</para>
    ///
    /// <para><c>Include</c> and <c>OrderBy</c> are both discarded by EF when it compiles the <c>COUNT</c>, so
    /// the extra trip is a bare aggregate over the same predicate, not a second materialisation.</para>
    ///
    /// <para><paramref name="paging"/> of <c>null</c> means "every row" — see <see cref="PagedResult{T}.Unpaged"/>
    /// for why that is a case in its own right and not a very large page.</para>
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageRequest? paging,
        CancellationToken cancellationToken = default)
    {
        // Unpaged first, so the unbounded callers pay one round trip and not two: their total is just the
        // length of what came back.
        if (paging is not { } page)
        {
            var all = await query.ToListAsync(cancellationToken);
            return PagedResult<T>.Unpaged(all);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.Take)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page.Page, page.PageSize, totalCount);
    }
}
