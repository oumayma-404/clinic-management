using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>One line of the patient's history from the cabinet's previous software (read-only).</summary>
public class PatientLegacyHistoryEntryDto
{
    public Guid Id { get; set; }
    /// <summary>« yyyy-MM-dd », or null when the old software gave no date.</summary>
    public string? Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Details { get; set; }

    public static PatientLegacyHistoryEntryDto From(PatientLegacyHistoryEntry e) => new()
    {
        Id = e.Id,
        Date = e.Date?.ToString("yyyy-MM-dd"),
        Category = e.Category,
        Title = e.Title,
        Details = e.Details,
    };
}
