namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>One billable line for the CNAM reimbursable/out-of-pocket split: the (optional) catalog act it
/// bills and the amount charged for it (line total HT for an invoice, planned cost for a devis line).</summary>
public readonly record struct CnamBillingLine(Guid? DentalActCodeId, decimal Amount);

/// <summary>
/// Indicative CNAM split of a billed document. <see cref="Reimbursable"/> + <see cref="OutOfPocket"/> always
/// equals the document total, and neither part is ever negative.
/// </summary>
public readonly record struct CnamSplit(decimal Reimbursable, decimal OutOfPocket);

/// <summary>
/// Computes the indicative CNAM-reimbursable vs. patient-out-of-pocket split for a billed document
/// (invoice or devis) using the existing per-act reimbursement estimate over the global CNAM catalog
/// (coefficient × VLC × age-rate). The reimbursable part is capped per line at the charged amount and, in
/// total, at the document total, so the two parts always sum to the document total and stay non-negative.
/// A line with no catalog act — or whose act has no coefficient / no lettre-clé value — is counted fully
/// out-of-pocket. The figure is indicative only (mirrors the per-act calculator); it is never persisted.
/// </summary>
public interface ICnamBillingCalculator
{
    Task<CnamSplit> ComputeAsync(
        IReadOnlyCollection<CnamBillingLine> lines,
        decimal documentTotal,
        DateTime? patientDateOfBirth,
        DateTime careDate,
        CancellationToken cancellationToken = default);
}
