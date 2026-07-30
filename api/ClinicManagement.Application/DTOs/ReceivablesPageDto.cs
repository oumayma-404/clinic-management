using ClinicManagement.Domain.Common;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Créances »: one page of debtors, plus the figure the screen shows above them.
///
/// <para><b>Why this is not a bare <see cref="PagedResult{T}"/>.</b> The header renders « Total dû » over every
/// patient who owes anything, while the table renders 25 of them. If the response carried only a page, the client
/// would have to sum the rows it received — silently reporting the total of one page as the clinic's receivables.
/// That is a money figure, so it is computed server-side over the whole set and sent alongside the page.</para>
///
/// <para>Same shape and same reasoning as <c>CaisseLedgerDto</c>, where the four totals describe the window while
/// the movements page.</para>
/// </summary>
public class ReceivablesPageDto
{
    /// <summary>The debtors on the requested page (or all of them when no paging was asked for).</summary>
    public List<ReceivableDto> Items { get; set; } = new();

    /// <summary>
    /// Sum of <see cref="ReceivableDto.TotalOutstanding"/> across <b>every</b> debtor matching the filter, not
    /// just this page.
    /// </summary>
    public decimal TotalOutstanding { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
}
