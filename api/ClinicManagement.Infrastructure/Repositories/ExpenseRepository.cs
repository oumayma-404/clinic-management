using Microsoft.EntityFrameworkCore;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
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

    public async Task<PagedResult<Expense>> GetByClinicIdAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Expenses.Where(e => e.ClinicId == clinicId);

        if (from.HasValue)
        {
            query = query.Where(e => e.ExpenseDate >= from.Value);
        }
        if (to.HasValue)
        {
            // INCLUSIVE, like the three sibling ledgers (AC-7). It was `<` while `GetPaymentsBetweenAsync`,
            // `GetInstallmentPaymentsBetweenAsync` and the avoirs' read are all `<=`, so an expense dated on the
            // window's own last tick fell out of the extrait while the payments beside it stayed in — and
            // « Σ movements == cashIn − refunds − cashOut » stopped holding at a period boundary. Every caller now
            // passes `ClinicClock.LastTickOfLocalDayUtc` through `CaissePeriod`, so an inclusive bound is what the
            // value means.
            query = query.Where(e => e.ExpenseDate <= to.Value);
        }

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(e =>
                EF.Functions.ILike(SqlSearch.Unaccent(e.Category)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(e.Description)!, pattern, SqlSearch.EscapeString));
        }

        // CreatedAt alone is not a unique tiebreaker — two expenses entered in the same batch can share it to
        // the microsecond — so the ordering ends on the id.
        return await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<decimal> GetTotalBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        // Inclusive on both ends, predicate-for-predicate with the list above it and with the three other money
        // ledgers — the totals and the statement must sum the same rows or the caisse contradicts itself (AC-7).
        return await _context.Expenses
            .Where(e => e.ClinicId == clinicId && e.ExpenseDate >= from && e.ExpenseDate <= to)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
    }

    public async Task<Expense> AddAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        await _context.Expenses.AddAsync(expense, cancellationToken);
        return expense;
    }

    public Task UpdateAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(expense);
        if (entry.State == EntityState.Detached)
        {
            _context.Expenses.Update(expense);
        }
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
