using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IExpenseRepository
{
    Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's expenses, optionally restricted to the [from, to) date range, newest first.</summary>
    /// <summary>
    /// Clinic expenses in a window. <paramref name="searchTerm"/> is matched in SQL over category and
    /// description; <paramref name="paging"/> of null returns every match — which is what the « extrait de
    /// caisse » needs, since it merges these rows with three other ledgers before it can order them.
    /// </summary>
    Task<PagedResult<Expense>> GetByClinicIdAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sum of a clinic's expense amounts recorded in [from, to).</summary>
    Task<decimal> GetTotalBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    Task<Expense> AddAsync(Expense expense, CancellationToken cancellationToken = default);
    Task UpdateAsync(Expense expense, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
