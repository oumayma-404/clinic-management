namespace ClinicManagement.Application.DTOs;

// Indicative CNAM reimbursement estimate for a single act (FR-5.5). Editor-only: never persisted, never
// printed. Estimate is null when the act's lettre clé has no VLC value (shown as "—", not zero).
public class ReimbursementEstimateDto
{
    public decimal? Estimate { get; set; }
    public decimal RateApplied { get; set; }
}
