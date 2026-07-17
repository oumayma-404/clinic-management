namespace ClinicManagement.Application.DTOs;

public class ClinicDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Code { get; set; }
    public string? LogoUrl { get; set; }

    // Billing / note-d'honoraires settings.
    public string? MatriculeFiscal { get; set; }
    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }
    public bool StampDutyEnabled { get; set; }
    public decimal StampDutyAmount { get; set; }

    public DateTime CreatedAt { get; set; }
}


