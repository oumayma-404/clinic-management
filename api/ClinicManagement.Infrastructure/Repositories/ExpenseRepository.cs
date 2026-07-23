using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class ExpenseRepository : IExpenseRepository
{
    private readonly ApplicationDbContext _context;

    public ExpenseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Expense>> GetByClinicIdAsync(Guid clinicId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Expenses.Where(e => e.ClinicId == clinicId);

        if (from.HasValue)
        {
            query = query.Where(e => e.ExpenseDate >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(e => e.ExpenseDate < to.Value);
        }

        return await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetTotalBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await _context.Expenses
            .Where(e => e.ClinicId == clinicId && e.ExpenseDate >= from && e.ExpenseDate < to)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
    }

    public async Task<Expense> AddAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        await _context.Expenses.AddAsync(expense, cancellationToken);
        return expense;
    }

    public Task UpdateAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        _context.Expenses.Update(expense);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await GetByIdAsync(id, cancellationToken);
        if (expense != null)
        {
            _context.Expenses.Remove(expense);
        }
    }
}
