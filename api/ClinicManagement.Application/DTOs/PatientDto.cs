namespace ClinicManagement.Application.DTOs;

public class PatientDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public List<PatientFlagDto> Flags { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string? Country { get; set; }
}

public class InsuranceInfoDto
{
    public string Provider { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string? GroupNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public class PatientFlagDto
{
    public Guid Id { get; set; }
    public string FlagType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}



