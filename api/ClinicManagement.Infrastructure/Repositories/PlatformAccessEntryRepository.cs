using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// The console's access ledger (<c>platform-console</c> FR-5). Add and read; there is deliberately no update and
/// no delete — see <see cref="IPlatformAccessEntryRepository"/>.
/// </summary>
public class PlatformAccessEntryRepository : IPlatformAccessEntryRepository
{
    private readonly ApplicationDbContext _context;

    public PlatformAccessEntryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(PlatformAccessEntry entry, CancellationToken cancellationToken = default) =>
        await _context.PlatformAccessEntries.AddAsync(entry, cancellationToken);

    public async Task<PagedResult<PlatformAccessEntry>> GetPageAsync(
        Guid? platformAccountId,
        Guid? clinicId,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PlatformAccessEntries.AsNoTracking();

        if (platformAccountId is { } accountId)
        {
            query = query.Where(e => e.PlatformAccountId == accountId);
        }

        if (clinicId is { } clinic)
        {
            query = query.Where(e => e.ClinicId == clinic);
        }

        // ⚠️ `.ThenBy(Id)` is load-bearing: two tabs open the same cabinet in the same tick, and OFFSET over the
        // instant alone would show one row on two pages and skip another — on a ledger, a vanished access record.
        return await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<PlatformAccessEntry?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        await _context.PlatformAccessEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<PlatformAccessActor>> GetRecordedActorsAsync(
        CancellationToken cancellationToken = default)
    {
        // Derived from the rows, never from the account table: an account that has opened nothing offers a filter
        // that matches nothing, and a deactivated one that did must stay filterable. The address is the row's own
        // (denormalised at write time), so a deleted account still reads correctly.
        //
        // ⚠️ A plain SELECT DISTINCT over the pair, not a GroupBy picking one address per account. Nothing renames
        // a console account today (the verb creates, deactivates and re-secrets; there is no change-of-address), so
        // this yields one row each — and if that ever changes, showing both addresses an account has acted under is
        // the honest answer, where `Max` would have silently chosen one by alphabet.
        // ⚠️ The ORDER BY is done in MEMORY, and that is the fix rather than a shortcut. Projecting into a custom
        // type and then ordering — `.Select(new PlatformAccessActor(..)).Distinct().OrderBy(a => a.AccountEmail)` —
        // compiles, ships, and **throws at request time**: EF cannot translate an OrderBy over a type it has
        // materialised through Distinct, so the journal answered « il n'a pas pu être lu » on every single load.
        // The DISTINCT itself translates fine and is what has to stay in SQL; the sort is over one row per console
        // account, i.e. two or three. Nothing in the unit suite could see this — every test mocks this repository —
        // which is why PlatformAccessLogQueryTranslationTests now compiles the expression below.
        var actors = await RecordedActorsQuery(_context).ToListAsync(cancellationToken);

        return actors.OrderBy(a => a.AccountEmail, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The distinct actors, as the expression tree the provider must translate — shared with
    /// <c>PlatformAccessLogQueryTranslationTests</c> so the compiled SQL cannot drift from what ships.
    /// <c>PatientRepository.RecallCandidateQuery</c>'s arrangement, for its reason.
    /// </summary>
    public static IQueryable<PlatformAccessActor> RecordedActorsQuery(ApplicationDbContext context) =>
        context.PlatformAccessEntries
            .AsNoTracking()
            .Select(e => new PlatformAccessActor(e.PlatformAccountId, e.AccountEmail))
            .Distinct();
}
