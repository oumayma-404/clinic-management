namespace ClinicManagement.Application.DTOs;

public class ClinicDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
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

    // TTN « El Fatoora » e-invoicing settings (non-secret).
    public bool TtnEInvoicingEnabled { get; set; }
    public string TtnEnvironment { get; set; } = "Sandbox";

    public DateTime CreatedAt { get; set; }
}


