using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class PatientMedicalHistory : Entity<Guid>
{
    public Guid PatientId { get; private set; }

    /// <summary>
    /// The owning clinic, denormalised from <see cref="Patient"/> so this row can carry a global query filter of
    /// its own. A clinical child used to have none — the per-handler check was its only layer — which made every
    /// new read of it a place tenant isolation could be forgotten silently. The column and the patient's must
    /// agree; nothing in the model can express that, so <c>verify-schema</c> asserts it
    /// (<c>clinical-child-clinic-matches-patient</c>).
    /// </summary>
    public Guid ClinicId { get; private set; }

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
        Guid clinicId,
        string description,
        DateTime? date = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be null or empty", nameof(description));

        Id = id;
        PatientId = patientId;
        ClinicId = clinicId;
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










