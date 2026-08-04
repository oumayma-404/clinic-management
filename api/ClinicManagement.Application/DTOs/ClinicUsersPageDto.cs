namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Utilisateurs »: one page of the clinic's staff, plus the number of accounts waiting to be let in.
///
/// <para><b>Why this is not a bare <c>PagedResult&lt;ClinicUserDto&gt;</c>.</b> Since I5 a self-registered
/// account is created <b>pending</b>, so « 2 comptes en attente d'activation » is the reason an admin opens this
/// screen at all — and it is a fact about the whole clinic, not about the 25 rows that happen to be on this page.
/// Counting the loaded rows would report « 0 en attente » to an admin whose two pending colleagues sort onto
/// page 2, which is exactly the case where the number matters: nobody gets in until someone notices.</para>
///
/// <para>Same shape and same reasoning as <c>ReceivablesPageDto</c> and <c>CaisseLedgerDto</c>: the figure above
/// the table describes the whole set, the table pages.</para>
/// </summary>
public class ClinicUsersPageDto
{
    /// <summary>The staff on the requested page (or all of them when no paging was asked for).</summary>
    public List<ClinicUserDto> Items { get; set; } = new();

    /// <summary>
    /// Accounts across the <b>whole clinic</b> that have never been able to log in and are waiting for an admin
    /// to activate them — <c>User.IsPendingActivation</c>. Deliberately unaffected by the search term: an admin
    /// filtering for « Ben » must still see that someone, somewhere, is waiting.
    /// </summary>
    public int PendingActivationCount { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}
