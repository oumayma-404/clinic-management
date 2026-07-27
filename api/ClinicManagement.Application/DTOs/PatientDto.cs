namespace ClinicManagement.Application.DTOs;

public class PatientDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    /// <summary>Null when the patient gave none — not an empty string, and never a placeholder address.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Null when the patient gave none. A patient without one receives no reminder and no relance; the UI says
    /// so rather than rendering a neutral blank.
    /// </summary>
    public string? PhoneNumber { get; set; }
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public List<PatientFlagDto> Flags { get; set; } = new();

    /// <summary>
    /// Archived patients are hidden from lists, search, recall and every picker, but keep every record and stay
    /// reachable by direct URL — so a detail page that loads one must be able to say so.
    /// </summary>
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? ArchiveReason { get; set; }

    public DateTime CreatedAt { get; set; }
}
