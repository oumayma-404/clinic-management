namespace ClinicManagement.Domain.Common;

/// <summary>
/// One page of a list read, plus the total the page was cut from.
///
/// <para><b>Why this lives in Domain.</b> It is not a domain concept, and it is here for one structural reason:
/// the repository interfaces are in this project and this project has <b>zero references</b>, so a paging type
/// declared in Application could not appear in a repository signature. The alternative — every list method
/// returning <c>(IReadOnlyList&lt;T&gt; Items, int TotalCount)</c> and taking two loose nullable ints — spreads
/// the clamping rule across twenty call sites and makes "skip supplied but take forgotten" expressible. One
/// type in <c>Common/</c>, next to <c>Entity</c> and <c>ValueObject</c>, is the smaller compromise.</para>
///
/// <para><b>Why the total is not optional.</b> Every table renders « N résultats » and a page count from it, and
/// a page carrying only its own rows cannot tell the client whether a full page means there is more (it does
/// not, when the total is an exact multiple of the page size). The extra <c>COUNT(*)</c> is the price of an
/// honest pager.</para>
///
/// <para><b>Unbounded is a first-class case, not a large page.</b> Roughly a dozen callers legitimately want
/// every row — the header's patient lookup, the act/medication pickers inside a form, the AI dispatcher, the
/// PDF and reconciliation reads. They pass no paging and get <see cref="Unpaged"/>, whose <see cref="Page"/> is
/// 1 and whose <see cref="PageSize"/> equals the count, so <see cref="TotalPages"/> is 1 and the JSON shape is
/// identical for every consumer. Modelling that as "page 1 of size <c>int.MaxValue</c>" would have put a bogus
/// <c>LIMIT 2147483647</c> into the SQL and made every pager believe there were two billion pages.</para>
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }

    /// <summary>1-based. Always 1 for an unpaged read.</summary>
    public int Page { get; }

    /// <summary>Rows per page. For an unpaged read this is the row count itself (see the class remarks).</summary>
    public int PageSize { get; }

    /// <summary>Rows matching the filter across every page — <b>not</b> the length of <see cref="Items"/>.</summary>
    public int TotalCount { get; }

    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>
    /// Total pages, never below 1: an empty list is « page 1 sur 1 », not « page 1 sur 0 » — which is what a
    /// naive ceiling gives and what makes a pager render « 1 / 0 » on a clinic's first day.
    /// </summary>
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Wrap an already-materialised complete list as a single page — for the callers that legitimately read
    /// everything, and for the reads whose rows must be assembled in memory before they can be filtered.
    /// </summary>
    public static PagedResult<T> Unpaged(IReadOnlyList<T> items) =>
        new(items, page: 1, pageSize: items.Count, totalCount: items.Count);

    public static PagedResult<T> Empty(PageRequest? paging = null) =>
        new(Array.Empty<T>(), paging?.Page ?? 1, paging?.PageSize ?? 0, totalCount: 0);

    /// <summary>Project the items while preserving the page metadata — the total must survive a <c>Select</c>.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), Page, PageSize, TotalCount);

    /// <summary>
    /// Cut a page out of a list that is already in memory.
    ///
    /// <para><b>Only for reads with no single queryable source.</b> Two exist: « Créances » and the « extrait de
    /// caisse ». Both are unions of several ledgers (invoice payments, plan installment payments, avoirs,
    /// dépenses) that have to be merged and ordered together before any row's position in the list is known — you
    /// cannot <c>LIMIT</c> one input and get a page of the union. Everywhere else, paging in memory would mean
    /// the database still read every row, which is the whole problem paging exists to solve; use the repository's
    /// <c>PageRequest</c> instead.</para>
    ///
    /// <para>A page past the end yields an empty list with the true total, not an error — see
    /// <see cref="PageRequest"/> on why out-of-range is clamped rather than refused.</para>
    /// </summary>
    public static PagedResult<T> FromSource(IReadOnlyList<T> all, PageRequest? paging)
    {
        if (paging is not { } page)
        {
            return Unpaged(all);
        }

        var items = all.Skip(page.Skip).Take(page.Take).ToList();
        return new PagedResult<T>(items, page.Page, page.PageSize, all.Count);
    }
}

/// <summary>
/// A normalised page window — the single authority on paging arithmetic, so no handler computes its own
/// <see cref="Skip"/> and no endpoint invents its own default page size.
///
/// <para><b>Every value is clamped, never rejected.</b> A page of 0 or -3, a size of 10 000, a page past the
/// end: none of these is a user error worth a 400 in a clinic. They come from a stale bookmark, a hand-edited
/// URL, or the client racing a delete that shrank the list — and a French error toast for « page 4 » of a list
/// that now has 3 pages is worse than showing the rows that do exist. <see cref="MaxPageSize"/> is the one hard
/// stop, and it is there to bound the server, not to correct the caller.</para>
/// </summary>
public readonly record struct PageRequest
{
    /// <summary>What a table gets when it asks for a page without saying how big. Fills a screen without scrolling the shell.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// The ceiling a caller cannot cross, and the whole reason this is a type rather than two ints: without it
    /// <c>?pageSize=1000000</c> is a one-request way to make the server materialise a clinic's entire history —
    /// the exact failure paging was introduced to prevent.
    /// </summary>
    public const int MaxPageSize = 200;

    public int Page { get; }
    public int PageSize { get; }

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Skip => (Page - 1) * PageSize;
    public int Take => PageSize;

    /// <summary>
    /// Build a window from raw query-string input, clamping both values.
    ///
    /// <para>Returns <c>null</c> when the caller supplied <b>neither</b> value — the unpaged read. That has to
    /// stay distinguishable from « page 1 », or every existing picker, the header search and the AI dispatcher
    /// would silently start truncating at 25 rows the moment paging shipped.</para>
    /// </summary>
    public static PageRequest? From(int? page, int? pageSize)
    {
        if (page is null && pageSize is null)
        {
            return null;
        }

        return new PageRequest(
            page: Math.Max(1, page ?? 1),
            pageSize: Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
    }

    /// <summary>An explicit window, for callers not parsing a query string (jobs, internal reads, tests).</summary>
    public static PageRequest Of(int page, int pageSize) =>
        new(Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
