using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class PatientMedicalHistory : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public string Description { get; private set; }
    public DateTime? Date { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private PatientMedicalHistory() { } // For EF Core

    public PatientMedicalHistory(
        Guid id,
        Guid patientId,
        string description,
        DateTime? date = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be null or empty", nameof(description));

        Id = id;
        PatientId = patientId;
        Description = description.Trim();
        Date = date;
        Notes = notes?.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string description, DateTime? date = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be null or empty", nameof(description));

        Description = description.Trim();
        Date = date;
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}










