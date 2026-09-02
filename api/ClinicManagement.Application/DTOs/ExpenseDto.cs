using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

public class ExpenseDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Set when the row was posted by a monthly series — la caisse marks it « mensuelle ».</summary>
    public Guid? RecurringExpenseId { get; set; }

    /// <summary>Round-tripped by the edit form so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class ExpenseMappingExtensions
{
    public static ExpenseDto ToDto(this Expense expense) => new()
    {
        Id = expense.Id,
        ClinicId = expense.ClinicId,
        ExpenseDate = expense.ExpenseDate,
        Category = expense.Category,
        Amount = expense.Amount,
        Method = expense.Method.ToString(),
        Description = expense.Description,
        RecurringExpenseId = expense.RecurringExpenseId,
        Version = expense.Version,
        CreatedAt = expense.CreatedAt,
        UpdatedAt = expense.UpdatedAt
    };
}
