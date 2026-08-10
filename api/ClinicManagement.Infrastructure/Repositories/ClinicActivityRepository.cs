using System.Linq.Expressions;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// The vendor console's counter tables, and the one bounded JOIN the portfolio list is (see
/// <see cref="IClinicActivityRepository"/> for why the snapshot exists at all).
/// </summary>
public class ClinicActivityRepository : IClinicActivityRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicActivityRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ClinicActivitySnapshot>> GetAllSnapshotsAsync(
        CancellationToken cancellationToken = default) =>
        await _context.ClinicActivitySnapshots
            .OrderBy(s => s.ClinicId)
            .ToListAsync(cancellationToken);

    public async Task AddSnapshotAsync(ClinicActivitySnapshot snapshot, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivitySnapshots.AddAsync(snapshot, cancellationToken);

    public async Task<ClinicActivityDay?> GetDayAsync(
        Guid clinicId, DateOnly day, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivityDays
            .FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Day == day, cancellationToken);

    public async Task<IReadOnlyList<ClinicActivityDay>> GetDaysAsync(
        Guid clinicId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivityDays
            .AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.Day >= from && d.Day <= to)
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken);

    public async Task AddDayAsync(ClinicActivityDay day, CancellationToken cancellationToken = default) =>
        await _context.ClinicActivityDays.AddAsync(day, cancellationToken);

    public async Task<PagedResult<PlatformClinicRow>> GetPortfolioAsync(
        PlatformPortfolioFilter filter,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        // A LEFT JOIN, not an inner one: a cabinet the pass has never covered must still appear — with its
        // counters stated as unknown rather than as zero (EC-8/EC-15). An inner join would hide exactly the
        // cabinets whose absence from the counters is the thing worth seeing.
        var query =
            from clinic in _context.Clinics.AsNoTracking()
            join snapshotRow in _context.ClinicActivitySnapshots.AsNoTracking()
                on clinic.Id equals snapshotRow.ClinicId into snapshots
            from snapshot in snapshots.DefaultIfEmpty()
            select new PortfolioJoin { clinic = clinic, snapshot = snapshot };

        if (filter.SearchPattern is { } pattern)
        {
            // Name, city and the administrators' addresses (AC-2.5), all matched in SQL through `unaccent` so
            // « Béchir » finds « bechir ». The admin side is an EXISTS rather than a join: a cabinet with two
            // admins must appear once, and joining would duplicate its row and corrupt the page boundaries.
            query = query.Where(x =>
                EF.Functions.ILike(SqlSearch.Unaccent(x.clinic.Name)!, pattern, SqlSearch.EscapeString)
                || (x.clinic.City != null
                    && EF.Functions.ILike(SqlSearch.Unaccent(x.clinic.City)!, pattern, SqlSearch.EscapeString))
                || _context.Users.Any(u =>
                    u.ClinicId == x.clinic.Id
                    && u.Role == User.RoleAdmin
                    && u.Email != null
                    && EF.Functions.ILike(SqlSearch.Unaccent(u.Email)!, pattern, SqlSearch.EscapeString)));
        }

        if (filter.DormantOnly)
        {
            // Measured AND idle. A cabinet with no snapshot is deliberately excluded — see the filter's own
            // remarks: « jamais mesuré » is not a claim that nothing happened.
            query = query.Where(x => x.snapshot != null && x.snapshot.Writes30d == 0);
        }

        // ⚠️ `.ThenBy(Id)` on every branch, never decoratively: OFFSET over a non-unique sort can show one
        // cabinet on two pages and skip another, which reads as a practice having disappeared from the portfolio.
        var ordered = filter.Sort switch
        {
            PlatformPortfolioSort.Activity => query
                .OrderByDescending(x => x.snapshot != null ? x.snapshot.Writes30d : 0)
                .ThenBy(x => x.clinic.Id),
            PlatformPortfolioSort.CreatedAt => query
                .OrderByDescending(x => x.clinic.CreatedAt)
                .ThenBy(x => x.clinic.Id),
            _ => query
                .OrderBy(x => x.clinic.Name)
                .ThenBy(x => x.clinic.Id)
        };

        return await ordered.Select(Projection).ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<PlatformClinicRow?> GetClinicRowAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // The same LEFT JOIN and the same projection as the list — AC-3.1 is « the same figures », so the
        // expression is shared rather than retyped.
        var query =
            from clinic in _context.Clinics.AsNoTracking().Where(c => c.Id == clinicId)
            join snapshotRow in _context.ClinicActivitySnapshots.AsNoTracking()
                on clinic.Id equals snapshotRow.ClinicId into snapshots
            from snapshot in snapshots.DefaultIfEmpty()
            select new PortfolioJoin { clinic = clinic, snapshot = snapshot };

        return await query.Select(Projection).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// A cabinet beside its snapshot, or beside nothing. A named type rather than an anonymous one so the list
    /// and the detail can pass the <b>same</b> projection expression over it.
    /// </summary>
    private sealed class PortfolioJoin
    {
        public required Clinic clinic { get; init; }
        public ClinicActivitySnapshot? snapshot { get; init; }
    }

    /// <summary>
    /// The one place a cabinet becomes a portfolio row.
    ///
    /// <para>⚠️ Every activity figure is read off the snapshot or defaulted, and <c>CountersComputedAt</c> stays
    /// <b>null</b> when there is no snapshot — that null is the only thing that can tell the screen the zeros
    /// above it mean « pas encore mesuré » rather than « rien fait » (EC-15).</para>
    /// </summary>
    private static readonly Expression<Func<PortfolioJoin, PlatformClinicRow>> Projection = x =>
        new PlatformClinicRow(
            x.clinic.Id,
            x.clinic.Name,
            x.clinic.City,
            x.clinic.CreatedAt,
            x.snapshot != null ? x.snapshot.Users : 0,
            x.snapshot != null ? x.snapshot.Patients : 0,
            x.snapshot != null ? x.snapshot.Appointments30d : 0,
            x.snapshot != null ? x.snapshot.Writes7d : 0,
            x.snapshot != null ? x.snapshot.Writes30d : 0,
            x.snapshot != null ? x.snapshot.ActiveDays30d : 0,
            x.snapshot != null ? x.snapshot.LastWriteAt : null,
            x.snapshot != null ? x.snapshot.LastLoginAt : null,
            x.snapshot != null ? x.snapshot.CollectedThisMonth : 0m,
            x.snapshot != null ? x.snapshot.ComputedAt : (DateTime?)null);

    public async Task<PlatformPortfolioTotals> GetPortfolioTotalsAsync(CancellationToken cancellationToken = default)
    {
        var clinics = await _context.Clinics.AsNoTracking().CountAsync(cancellationToken);

        var dormant = await _context.ClinicActivitySnapshots
            .AsNoTracking()
            .CountAsync(s => s.Writes30d == 0, cancellationToken);

        var measured = await _context.ClinicActivitySnapshots.AsNoTracking().CountAsync(cancellationToken);

        // Derived from the two counts rather than read with a NOT EXISTS: the snapshot table is unique on
        // ClinicId and every row's clinic exists (FK), so « cabinets − mesurés » is the same number and one
        // query cheaper. It cannot go negative for the same two reasons.
        return new PlatformPortfolioTotals(clinics, dormant, clinics - measured);
    }
}
