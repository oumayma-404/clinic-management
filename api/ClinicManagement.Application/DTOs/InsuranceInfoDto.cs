namespace ClinicManagement.Application.DTOs;

public class InsuranceInfoDto
{
    public string Provider { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string? GroupNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
}




