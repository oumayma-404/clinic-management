using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class PatientFlag : Entity<Guid>, IAuditable
{
    public Guid PatientId { get; private set; }
    public PatientFlagType FlagType { get; private set; }
    public string Description { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private PatientFlag() { } // For EF Core

    public PatientFlag(
        Guid id,
        Guid patientId,
        PatientFlagType flagType,
        string description,
        string? notes = null)
    {
        Id = id;
        PatientId = patientId;
        FlagType = flagType;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Notes = notes;
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
    }

    public void Update(string description, string? notes)
    {
        Description = description;
        Notes = notes;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}



