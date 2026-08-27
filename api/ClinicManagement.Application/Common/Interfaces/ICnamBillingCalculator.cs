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

    /// <summary>
    /// The same reimbursable estimate as <see cref="ComputeAsync"/>, split by whether each act <b>consumes the
    /// patient's annual ceiling</b> (L10). Feeds « Reste sur le plafond annuel ».
    ///
    /// <para>A member on this interface rather than a second calculator, deliberately: it resolves the same acts
    /// against the same catalogue and applies the same per-act <c>CnamReimbursementCalculator</c>, so a separate
    /// implementation would be a second authority over a reimbursement figure — precisely the defect
    /// <c>GetReimbursementEstimatesQuery</c> was added to remove on the client side.</para>
    ///
    /// <para>⚠️ Unlike <see cref="ComputeAsync"/> it takes <b>no document total and applies no cap</b>. The two
    /// caps there exist so a split always sums to the document; here the question is how much CNAM has been asked
    /// to pay, and clamping it to what the clinic happened to charge would under-report consumption on a
    /// discounted invoice — which inflates the remaining ceiling, the exact over-promise L10 removes.</para>
    /// </summary>
    Task<CnamCeilingConsumption> ComputeCeilingConsumptionAsync(
        IReadOnlyCollection<CnamBillingLine> lines,
        DateTime? patientDateOfBirth,
        DateTime careDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How much reimbursement a set of lines represents, split by whether it counts against the annual ceiling.
/// <para>
/// <see cref="HorsPlafond"/> is reported rather than dropped: « 320,000 DT dont 200,000 hors plafond » is a
/// different conversation from « 120,000 DT », and a patient told their ceiling is nearly untouched needs to see
/// why. <c>CnamPlafond.ConsumesCeiling</c> is the single rule that sorts a line between the two.
/// </para>
/// </summary>
public readonly record struct CnamCeilingConsumption(decimal Consuming, decimal HorsPlafond);
