using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

public interface ITreatmentPlanRepository
{
    /// <summary>Load a treatment plan with its items and installments.</summary>
    Task<TreatmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>List a clinic's treatment plans, filtered by patient / status / created-date range.</summary>
    Task<IEnumerable<TreatmentPlan>> GetFilteredAsync(
        Guid clinicId,
        Guid? patientId = null,
        TreatmentPlanStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest sequence number already assigned for a clinic in a given year (0 when none). The next
    /// accepted plan uses this + 1, giving a gapless per-clinic-per-year sequence (separate from invoices).
    /// </summary>
    Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Amount of installment payments collected in [from, to] across the clinic's <b>committed</b> plans
    /// (<c>PlanBillingRules.DebtBearingPlanStatuses</c> — a Draft devis's hand-built échéancier is not
    /// clinic money, and a cancelled plan is void). Approximated from the installment's cumulative
    /// <c>AmountPaid</c> and its last-payment date (<c>LastPaidOn</c>) — installments keep no per-payment
    /// history — so an installment topped up across two months is attributed to its last-payment month
    /// only. Feeds the dashboard "encaissé ce mois-ci" and the caisse.
    /// <para>
    /// Takes no billed-plan exclusion on purpose: this is <i>cash received</i>, not debt. The devis→facture
    /// bridge copies no payment onto the invoice, so suppressing a bridged plan here would delete real
    /// receipts from the caisse instead of de-duplicating them.
    /// </para>
    /// </summary>
    Task<decimal> GetInstallmentCollectedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Outstanding installment balance per patient (Σ amount − paid over not-fully-paid installments) plus
    /// the oldest overdue installment due date (an unpaid installment whose <c>DueDate</c> is before
    /// <paramref name="asOfUtc"/>), across the clinic's <b>committed</b> plans
    /// (<c>PlanBillingRules.DebtBearingPlanStatuses</c>) — only patients with a balance &gt; 0. Feeds the
    /// receivables list and aging, and the dashboard total-outstanding figure.
    /// </summary>
    /// <param name="excludedPlanIds">
    /// Plans already represented by an invoice, from <c>PlanBillingRules.BilledPlanIds</c> — their acts are
    /// counted through that invoice instead. Required (pass an empty set for none) so a new money read
    /// cannot silently omit the de-duplication and drift from « Solde patient ».
    /// </param>
    Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestOverdueDueDate)>> GetInstallmentOutstandingByPatientAsync(
        Guid clinicId, DateTime asOfUtc, IReadOnlyCollection<Guid> excludedPlanIds, CancellationToken cancellationToken = default);

    Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
