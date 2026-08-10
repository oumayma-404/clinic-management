using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The console's own access ledger (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>⚠️ <b>Add and read. There is no update and no delete, and that is the contract</b> — the same shape
/// <c>IAuditEntryRepository</c> has. A ledger recording what a cross-cabinet surface did is only worth keeping if
/// nothing on that surface can rewrite it.</para>
///
/// <para>⚠️ <b>Readable, not write-only.</b> A ledger nobody can read is a promise nobody can check; the console
/// serves it at <c>GET /api/platform/access-log</c> and shows it at <c>/journal</c>. It stays a <b>console</b>
/// read — showing a cabinet who looked at it is named out of scope by the spec.</para>
/// </summary>
public interface IPlatformAccessEntryRepository
{
    Task AddAsync(PlatformAccessEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the ledger, newest first, optionally narrowed to one console account or one cabinet.
    ///
    /// <para>⚠️ Ordered on a unique column last: several rows can share an instant (two tabs, a script), and
    /// <c>OFFSET</c> over a non-unique sort would show one row twice and skip another — which on a ledger reads as
    /// the record of an access having disappeared.</para>
    /// </summary>
    Task<PagedResult<PlatformAccessEntry>> GetPageAsync(
        Guid? platformAccountId,
        Guid? clinicId,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The console accounts that appear in the ledger, for the journal's « Compte » filter.
    ///
    /// <para>Derived from the <b>rows</b> rather than from the account table, like
    /// <c>IAuditEntryRepository.GetRecordedEntityTypesAsync</c>: an account that has never opened a cabinet
    /// offers a filter that matches nothing, and a deactivated one that did must stay filterable.</para>
    /// </summary>
    Task<IReadOnlyList<PlatformAccessActor>> GetRecordedActorsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One console account as the journal's filter offers it: its id and the address its rows carry.</summary>
public sealed record PlatformAccessActor(Guid PlatformAccountId, string AccountEmail);
