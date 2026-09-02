using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

public class RecurringExpenseDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DayOfMonth { get; set; }

    /// <summary>The `AAAA-MM` of the last month posted, so the card can say « prochaine : octobre 2026 ».</summary>
    public string LastPostedMonth { get; set; } = string.Empty;

    /// <summary>The month the next occurrence is owed for — derived, so the client does no month arithmetic.</summary>
    public string NextMonth { get; set; } = string.Empty;

    /// <summary>Round-tripped by the edit form so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }
}

public static class RecurringExpenseMappingExtensions
{
    public static RecurringExpenseDto ToDto(this RecurringExpense series) => new()
    {
        Id = series.Id,
        Category = series.Category,
        Amount = series.Amount,
        Method = series.Method.ToString(),
        Description = series.Description,
        DayOfMonth = series.DayOfMonth,
        LastPostedMonth = series.LastPostedMonth,
        NextMonth = ClinicClock.NextMonthKey(series.LastPostedMonth),
        Version = series.Version
    };
}
