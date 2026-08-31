namespace ClinicManagement.Application.DTOs;

// Indicative CNAM reimbursement estimate for a single act (FR-5.5). Editor-only: never persisted, never
// printed. Estimate is null when the act carries no cotation or its lettre cle has no VLC value (shown as
// "—", not zero) — and UnavailableReason says which, so the screen can name the fix.
public class ReimbursementEstimateDto
{
    public decimal? Estimate { get; set; }
    public decimal RateApplied { get; set; }

    /// <summary>
    /// A <see cref="Domain.Enums.ReimbursementUnavailability"/> member name when <see cref="Estimate"/> is null,
    /// else null. The two causes need different sentences: a missing cotation is a gap an admin can close in the
    /// catalogue, a missing valeur de la lettre cle is not.
    /// </summary>
    public string? UnavailableReason { get; set; }
}
