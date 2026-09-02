using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IRecurringExpenseRepository
{
    Task<RecurringExpense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// A clinic's active series, oldest first. Unpaged on purpose: a practice has a handful of standing
    /// commitments, and « Dépenses mensuelles » is the whole list or it is not an answer.
    /// </summary>
    Task<IReadOnlyList<RecurringExpense>> GetActiveByClinicIdAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every clinic's active series, for the posting pass. The caller must have declared
    /// <c>UseSystemWide</c> — without it the clinic filter answers zero rows and the pass logs a clean run.
    /// </summary>
    Task<IReadOnlyList<RecurringExpense>> GetActiveForPostingAsync(CancellationToken cancellationToken = default);

    Task<RecurringExpense> AddAsync(RecurringExpense recurringExpense, CancellationToken cancellationToken = default);

    Task UpdateAsync(RecurringExpense recurringExpense, CancellationToken cancellationToken = default);
}
