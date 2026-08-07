using ClinicManagement.Domain.Common;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// Turns a plain collection into a <see cref="PagedResult{T}"/> for a Moq <c>ReturnsAsync</c>.
///
/// <para>Every repository list read returns a page now, so a test that stubs one has to hand back a page. Almost
/// none of them care about paging — they assert on a handful of rows — so the interesting thing is that the stub
/// stays readable: <c>ReturnsAsync(new[] { a, b }.AsPage())</c> rather than
/// <c>ReturnsAsync(new PagedResult&lt;Patient&gt;(new[] { a, b }, 1, 2, 2))</c>, which buries the two rows the
/// test is about in page arithmetic that is not.</para>
///
/// <para>It produces an <b>unpaged</b> page (page 1, <c>PageSize</c> = count, <c>TotalCount</c> = count), which is
/// what a repository returns when the caller supplied no <c>PageRequest</c> — the case these tests exercise. Tests
/// that are specifically about paging build the <see cref="PagedResult{T}"/> directly, so the window is visible in
/// the test rather than hidden behind this helper.</para>
/// </summary>
internal static class PagedTestExtensions
{
    public static PagedResult<T> AsPage<T>(this IEnumerable<T> items) =>
        PagedResult<T>.Unpaged(items as IReadOnlyList<T> ?? items.ToList());

    /// <summary>An explicit window, for the tests that assert on paging itself.</summary>
    public static PagedResult<T> AsPage<T>(this IEnumerable<T> items, int page, int pageSize, int totalCount) =>
        new(items as IReadOnlyList<T> ?? items.ToList(), page, pageSize, totalCount);
}
