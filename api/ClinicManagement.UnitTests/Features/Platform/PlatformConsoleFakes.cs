using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// A ledger that <b>keeps its rows</b>, so a write path and a read path can be compared against each other rather
/// than each against a hand-written expectation — the reason Part 3's journal test exists at all.
///
/// <para>Shared by Part 3's access-ledger tests and Part 4's idempotency tests: « one entry per submission » is a
/// property of this collection plus the unique index, and two fakes would let one of them drift into asserting
/// something the other does not.</para>
///
/// <para>⚠️ <see cref="Unique"/> reproduces the <b>partial unique index</b> on <c>IdempotencyKey</c>, because the
/// enforcement is the database's and a fake that ignored it would let AC-4.6's race test pass over an
/// implementation with no index behind it. It throws the same <c>DbUpdateException</c> Npgsql raises for 23505.
/// </para>
/// </summary>
internal sealed class FakeAccessLedger : IPlatformAccessEntryRepository
{
    public List<PlatformAccessEntry> Rows { get; } = new();

    /// <summary>Set false to model a database with no unique index — used to prove the guard is the index's.</summary>
    public bool Unique { get; init; } = true;

    /// <summary>
    /// How many key lookups answer « nothing recorded » regardless of the rows held — the only way to reach the
    /// instant EC-5 is about, where two submissions have both read before either has saved. Without it a test
    /// « racing » two identical calls exercises the read-first check twice and never the unique index at all.
    /// </summary>
    public int BlindReadsRemaining { get; set; }

    public Task AddAsync(PlatformAccessEntry entry, CancellationToken cancellationToken = default)
    {
        if (Unique
            && entry.IdempotencyKey is { } key
            && Rows.Any(r => r.IdempotencyKey == key))
        {
            throw new Microsoft.EntityFrameworkCore.DbUpdateException(
                $"duplicate key value violates unique constraint \"IX_PlatformAccessEntries_IdempotencyKey\" ({key})");
        }

        Rows.Add(entry);
        return Task.CompletedTask;
    }

    public Task<PagedResult<PlatformAccessEntry>> GetPageAsync(
        Guid? platformAccountId, Guid? clinicId, PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var matched = Rows
            .Where(r => platformAccountId is null || r.PlatformAccountId == platformAccountId)
            .Where(r => clinicId is null || r.ClinicId == clinicId)
            .OrderByDescending(r => r.OccurredAt)
            .ThenBy(r => r.Id)
            .ToList();

        var page = paging ?? PageRequest.Of(1, PageRequest.DefaultPageSize);
        return Task.FromResult(new PagedResult<PlatformAccessEntry>(
            matched.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToList(),
            page.Page, page.PageSize, matched.Count));
    }

    public Task<IReadOnlyList<PlatformAccessActor>> GetRecordedActorsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlatformAccessActor>>(Rows
            .Select(r => new PlatformAccessActor(r.PlatformAccountId, r.AccountEmail))
            .Distinct()
            .OrderBy(a => a.AccountEmail)
            .ToList());

    public Task<PlatformAccessEntry?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (BlindReadsRemaining > 0)
        {
            BlindReadsRemaining--;
            return Task.FromResult<PlatformAccessEntry?>(null);
        }

        return Task.FromResult(Rows.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey));
    }
}

/// <summary>The acting console account, or none — the second case is what an unattributable action looks like.</summary>
internal sealed class FakePlatformSession : IPlatformSessionContext
{
    public Guid? AccountId { get; init; }

    public string? Email { get; init; }

    public Guid? GetAccountId() => AccountId;

    public string? GetEmail() => Email;
}
