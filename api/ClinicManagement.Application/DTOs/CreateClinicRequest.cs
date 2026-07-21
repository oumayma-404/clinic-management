namespace ClinicManagement.Application.DTOs;

public class CreateClinicRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool GenerateCode { get; set; } = true;
    public string Role { get; set; } = "doctor"; // "doctor" or "secretary"
    public DoctorPersonalInfoDto? DoctorInfo { get; set; } // Required if Role is "doctor"
    public List<DoctorDto>? Doctors { get; set; } // Legacy: additional doctors (not the creator)
}

public class DoctorDto
{
    public Guid? Id { get; set; }
    public string? UserId { get; set; } // Auth0 sub / local user id this doctor is linked to (Doctor.LinkToUser); lets the client resolve "my doctor" authoritatively
    public string Name { get; set; } = string.Empty; // Kept for backward compatibility, maps to FullName
    public string? FirstName { get; set; } // New field
    public string? LastName { get; set; } // New field
    public string Specialty { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? CodeProfessionnelSante { get; set; } // CNAM provider code (prints on the bulletin)
    public string? OrdreNumberCnomdt { get; set; } // CNOMDT registration number (pre-filled on certificats/liaisons)
    public bool HasCachet { get; set; } // whether a cachet/signature image is on file (FR-3.1)
}

