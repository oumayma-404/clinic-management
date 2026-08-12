using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF implementation of <see cref="IMessagingAllowanceRepository"/>.
///
/// <para>⚠️ <b>No <c>IgnoreQueryFilters()</c> anywhere, and none is needed.</b> Both tables carry a non-nullable
/// <c>ClinicId</c> and are filtered, so a caller that wants every cabinet — the three <c>messaging-*</c> verbs, the
/// daily pass, the dispatcher — declares <c>UseSystemWide</c> rather than having this class quietly read across
/// practices. <c>IClinicSubscriptionRepository</c>'s own stated rule.</para>
/// </summary>
public class MessagingAllowanceRepository : IMessagingAllowanceRepository
{
    private readonly ApplicationDbContext _context;

    public MessagingAllowanceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MessagingAllowanceEntry>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.MessagingAllowanceEntries
            .Where(e => e.ClinicId == clinicId)
            // Oldest first, with `Id` as the tie-break the fold's own ordering also applies: two entries recorded in
            // the same tick must fold in a stable order or the month's allowance would depend on row order.
            .OrderBy(e => e.RecordedAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<MessagingAllowanceEntry?> GetEntryAsync(
        Guid clinicId, Guid entryId, CancellationToken cancellationToken = default) =>
        await _context.MessagingAllowanceEntries
            // Scoped by clinic as well as by id, so another practice's entry is not merely refused later — it is
            // structurally unreachable, `CancelSubscriptionPeriodFromConsoleCommand`'s precedent.
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ClinicId == clinicId, cancellationToken);

    public async Task<ClinicMessagingMonth?> GetMonthAsync(
        Guid clinicId, string monthKey, CancellationToken cancellationToken = default) =>
        await _context.ClinicMessagingMonths
            .FirstOrDefaultAsync(m => m.ClinicId == clinicId && m.MonthKey == monthKey, cancellationToken);

    public async Task<IReadOnlyList<ClinicMessagingMonth>> GetMonthsAsync(
        Guid clinicId, string fromMonthKey, CancellationToken cancellationToken = default) =>
        await _context.ClinicMessagingMonths
            .Where(m => m.ClinicId == clinicId && m.MonthKey.CompareTo(fromMonthKey) >= 0)
            .OrderBy(m => m.MonthKey)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// ⚠️ A <b>LEFT</b> join from <c>Clinics</c>, so a cabinet the counting pass has never reached still appears with
    /// its month stated as unknown rather than as zeros — an inner join would hide exactly the cabinets whose absence
    /// is the thing worth seeing (FR-1a). <c>ClinicActivityRepository</c>'s own reasoning.
    /// </summary>
    public async Task<IReadOnlyList<ClinicMessagingReportRow>> GetForReportAsync(
        string monthKey, CancellationToken cancellationToken = default) =>
        await _context.Clinics
            .OrderBy(c => c.Name)
            .Select(c => new ClinicMessagingReportRow(
                c.Id,
                c.Name,
                _context.ClinicMessagingMonths
                    .FirstOrDefault(m => m.ClinicId == c.Id && m.MonthKey == monthKey)))
            .ToListAsync(cancellationToken);

    public async Task AddEntryAsync(MessagingAllowanceEntry entry, CancellationToken cancellationToken = default) =>
        await _context.MessagingAllowanceEntries.AddAsync(entry, cancellationToken);

    public async Task AddMonthAsync(ClinicMessagingMonth month, CancellationToken cancellationToken = default) =>
        await _context.ClinicMessagingMonths.AddAsync(month, cancellationToken);

    public Task UpdateEntryAsync(MessagingAllowanceEntry entry, CancellationToken cancellationToken = default)
    {
        Attach(entry);
        return Task.CompletedTask;
    }

    public Task UpdateMonthAsync(ClinicMessagingMonth month, CancellationToken cancellationToken = default)
    {
        Attach(month);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Only attaches a <b>detached</b> instance. On the normal path the caller loaded the row through this same
    /// context, so change tracking already holds its original values — including the <c>xmin</c> concurrency token.
    /// Calling <c>Update()</c> on a tracked entity re-marks every property modified; on a detached one that was never
    /// loaded the token reads as 0, producing <c>WHERE xmin = 0</c>, zero matched rows and a 409 for a conflict that
    /// never was. <c>NotificationRepository</c>/<c>ClinicSubscriptionRepository</c> document the same trap.
    /// </summary>
    private void Attach<T>(T entity) where T : class
    {
        if (_context.Entry(entity).State == EntityState.Detached)
        {
            _context.Set<T>().Update(entity);
        }
    }
}
