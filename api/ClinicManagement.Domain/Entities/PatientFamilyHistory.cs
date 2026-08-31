using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class PatientFamilyHistory : Entity<Guid>, IAuditable
{
    public Guid PatientId { get; private set; }

    /// <summary>The owning clinic, denormalised from <see cref="Patient"/>. See <see cref="PatientMedicalHistory.ClinicId"/>.</summary>
    public Guid ClinicId { get; private set; }

    public string Relationship { get; private set; } // e.g., "Father", "Mother", "Grandfather"
    public string Condition { get; private set; } // e.g., "Heart Disease", "Diabetes"
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private PatientFamilyHistory() { } // For EF Core

    public PatientFamilyHistory(
        Guid id,
        Guid patientId,
        Guid clinicId,
        string relationship,
        string condition,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(relationship))
            throw new ArgumentException("Relationship cannot be null or empty", nameof(relationship));

        if (string.IsNullOrWhiteSpace(condition))
            throw new ArgumentException("Condition cannot be null or empty", nameof(condition));

        Id = id;
        PatientId = patientId;
        ClinicId = clinicId;
        Relationship = relationship.Trim();
        Condition = condition.Trim();
        Notes = notes?.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string relationship, string condition, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(relationship))
            throw new ArgumentException("Relationship cannot be null or empty", nameof(relationship));

        if (string.IsNullOrWhiteSpace(condition))
            throw new ArgumentException("Condition cannot be null or empty", nameof(condition));

        Relationship = relationship.Trim();
        Condition = condition.Trim();
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}










