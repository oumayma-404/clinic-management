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
    /// Amount of installment payments collected in [from, to] across the clinic's non-cancelled plans.
    /// Approximated from the installment's cumulative <c>AmountPaid</c> and its last-payment date
    /// (<c>LastPaidOn</c>) — installments keep no per-payment history — so an installment topped up across
    /// two months is attributed to its last-payment month only. Feeds the dashboard "encaissé ce mois-ci".
    /// </summary>
    Task<decimal> GetInstallmentCollectedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Outstanding installment balance per patient (Σ amount − paid over not-fully-paid installments) plus
    /// the oldest overdue installment due date (an unpaid installment whose <c>DueDate</c> is before
    /// <paramref name="asOfUtc"/>), across the clinic's non-cancelled plans — only patients with a balance
    /// &gt; 0. Feeds the unified per-patient balance, the receivables aging, and the dashboard total.
    /// </summary>
    Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestOverdueDueDate)>> GetInstallmentOutstandingByPatientAsync(
        Guid clinicId, DateTime asOfUtc, CancellationToken cancellationToken = default);

    Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
