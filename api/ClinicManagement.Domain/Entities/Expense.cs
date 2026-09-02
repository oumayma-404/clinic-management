using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A cash-out / expense entry for the clinic caisse (loyer, salaires, fournitures, laboratoire, …).
/// Clinic-scoped; combined with the collected invoice payments to give the daily caisse + net figure.
/// Amount is a positive TND value stored to the millime (decimal(18,3)), like the invoice money columns.
/// </summary>
public class Expense : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public DateTime ExpenseDate { get; private set; }
    public string Category { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Description { get; private set; }

    /// <summary>
    /// The <see cref="RecurringExpense"/> that posted this row, or null for a hand-typed dépense. It is a label
    /// on the row's ORIGIN and nothing more: the row is an ordinary dépense to every money read, and editing or
    /// deleting it changes nothing about the series.
    /// </summary>
    public Guid? RecurringExpenseId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Expense() { } // For EF Core

    public Expense(
        Guid id,
        Guid clinicId,
        DateTime expenseDate,
        string category,
        decimal amount,
        PaymentMethod method,
        string? description = null,
        Guid? recurringExpenseId = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de la dépense doit être supérieur à 0.", nameof(amount));

        Id = id;
        ClinicId = clinicId;
        ExpenseDate = expenseDate;
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Amount = amount;
        Method = method;
        Description = description;
        RecurringExpenseId = recurringExpenseId;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(DateTime expenseDate, string category, decimal amount, PaymentMethod method, string? description)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de la dépense doit être supérieur à 0.", nameof(amount));

        ExpenseDate = expenseDate;
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Amount = amount;
        Method = method;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }
}
