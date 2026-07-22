using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

public class UpdateClinicRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public IFormFile? Logo { get; set; }

    // Billing / note-d'honoraires settings (optional; null leaves the current value unchanged).
    public string? MatriculeFiscal { get; set; }
    public bool? VatApplicable { get; set; }
    public decimal? VatRate { get; set; }
    public bool? StampDutyEnabled { get; set; }
    public decimal? StampDutyAmount { get; set; }

    // TTN « El Fatoora » e-invoicing settings (optional; null leaves the current value unchanged).
    public bool? TtnEInvoicingEnabled { get; set; }
    public string? TtnEnvironment { get; set; }

    // Working hours as a JSON array (reliability-and-polish AC-7). Sent as a form field by the settings UI;
    // null/blank leaves the current value unchanged.
    public string? WorkingHoursJson { get; set; }
}



