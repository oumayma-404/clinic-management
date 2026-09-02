using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class RecurringExpenseRepository : IRecurringExpenseRepository
{
    private readonly ApplicationDbContext _context;

    public RecurringExpenseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RecurringExpense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringExpenses
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringExpense>> GetActiveByClinicIdAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        // `CancelledAt == null` in SQL, not `IsActive` — the domain property is computed and has no column, so
        // filtering on it would silently pull the whole table into memory first.
        return await _context.RecurringExpenses
            .Where(r => r.ClinicId == clinicId && r.CancelledAt == null)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringExpense>> GetActiveForPostingAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.RecurringExpenses
            .Where(r => r.CancelledAt == null)
            .OrderBy(r => r.ClinicId)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringExpense> AddAsync(
        RecurringExpense recurringExpense,
        CancellationToken cancellationToken = default)
    {
        await _context.RecurringExpenses.AddAsync(recurringExpense, cancellationToken);
        return recurringExpense;
    }

    public Task UpdateAsync(RecurringExpense recurringExpense, CancellationToken cancellationToken = default)
    {
        // Attach only a DETACHED instance — see ExpenseRepository.UpdateAsync for why Update() on a tracked
        // aggregate produces "WHERE xmin = 0" and a 409 for a conflict that never happened.
        var entry = _context.Entry(recurringExpense);
        if (entry.State == EntityState.Detached)
        {
            _context.RecurringExpenses.Update(recurringExpense);
        }
        return Task.CompletedTask;
    }
}
