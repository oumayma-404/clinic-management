using ClinicManagement.Domain.Common;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One page of stock items, plus the three clinic-wide figures the stockroom screen shows around them.
///
/// <para><b>Why not a bare <see cref="PagedResult{T}"/>.</b> The screen renders « Stock faible (N) » and
/// « Péremption (N) » as filter chips, and a category dropdown built from the distinct categories. All three were
/// derived in the browser from the full item list. Over a page they would become "the low-stock items among these
/// 25" and "the categories that happen to appear on this page" — a chip offering to filter to 3 items when 40 are
/// low, and a dropdown missing most categories. They are facts about the clinic, so the server computes them.</para>
///
/// <para>The two counts deliberately ignore the current filters and search: they answer « how much is wrong in the
/// stockroom », which is what makes them worth clicking. <see cref="Categories"/> likewise lists every category so
/// the dropdown can always take you somewhere.</para>
/// </summary>
public class StockPageDto
{
    /// <summary>The items on the requested page (or all of them when no paging was asked for).</summary>
    public List<StockItemDto> Items { get; set; } = new();

    /// <summary>Items at or below their minimum, clinic-wide — the same predicate the « Stock faible » filter uses.</summary>
    public int LowStockCount { get; set; }

    /// <summary>
    /// Items with a dated lot still holding stock at or inside the clinic's expiry lead time, clinic-wide.
    /// Already-expired lots are included: that is the more urgent case of the same alert.
    /// </summary>
    public int ExpiringCount { get; set; }

    /// <summary>Every distinct category in the clinic, sorted — the options for the category filter.</summary>
    public List<string> Categories { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
}
