using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One plan, reduced to what the « à rappeler » worklist needs to judge it: is it stalled, or unanswered?
///
/// <para>A projection, not a <see cref="TreatmentPlan"/>. The rules need five scalars and two item counts; loading
/// every plan in the clinic with its items, installments and payments to derive them is the over-fetch
/// <c>GetFilteredAsync</c> exists for and this read must avoid.</para>
/// </summary>
public sealed record RecallPlanFact(
    Guid PatientId,
    Guid PlanId,
    string? Number,
    TreatmentPlanStatus Status,
    DateTime CreatedAt,
    DateTime? AcceptedDate,
    int TotalItems,
    int DoneItems);

/// <summary>
/// One échéance-collection row behind the caisse statement — the plan side of <see cref="CaissePaymentRow"/>.
/// Keyed on the devis's own number, not an invoice's: an échéancier is collected against the devis.
/// </summary>
public sealed record CaisseInstallmentPaymentRow(
    Guid PaymentId,
    Guid TreatmentPlanId,
    string? PlanNumber,
    Guid PatientId,
    decimal Amount,
    PaymentMethod Method,
    DateTime PaidOn,
    bool IsVoided,
    string? VoidReason,
    string? VoidedByName);

public interface ITreatmentPlanRepository
{
    /// <summary>Load a treatment plan with its items and installments.</summary>
    Task<TreatmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every plan in the clinic holding an act linked to this fiche de soins, loaded with its items so the act can
    /// be un-marked. Backs the cleanup that runs when a fiche is deleted: <c>TreatmentPlanItem.LinkedDentalRecordId</c>
    /// is FK-less by design, so without this the act would stay « réalisé » pointing at a row that no longer exists —
    /// and, because marking an act done can auto-complete the plan, a deleted fiche could leave a devis closed
    /// against evidence that is gone. Normally returns zero or one plan.
    /// </summary>
    Task<IReadOnlyList<TreatmentPlan>> GetByLinkedDentalRecordAsync(
        Guid clinicId, Guid dentalRecordId, CancellationToken cancellationToken = default);

    /// <summary>List a clinic's treatment plans, filtered by patient / status / created-date range.</summary>
    /// <param name="acceptedFrom">
    /// Inclusive lower bound on <c>AcceptedDate</c> — a <b>different date</b> from <paramref name="from"/>, which
    /// bounds <c>CreatedAt</c>. Both exist because the dashboard's « Devis acceptés » KPI counts by the date the
    /// patient said yes, so drilling into it with the created-date range would show a different set of devis than
    /// the number counted. A plan with no <c>AcceptedDate</c> (still Draft) is excluded when this is supplied.
    /// </param>
    /// <param name="acceptedTo">Inclusive upper bound on <c>AcceptedDate</c>.</param>
    /// <param name="searchTerm">
    /// Matched in SQL over the devis number, title, notes and the patient's name (an EXISTS against
    /// <c>Patients</c> — names are resolved by a batched lookup after the page is cut, so the filter cannot live
    /// in the handler).
    /// </param>
    /// <param name="paging">The page to return, or null for every match (« Solde patient »).</param>
    Task<PagedResult<TreatmentPlan>> GetFilteredAsync(
        Guid clinicId,
        Guid? patientId = null,
        TreatmentPlanStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        DateTime? acceptedFrom = null,
        DateTime? acceptedTo = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the clinic's plans are in <paramref name="status"/>, optionally bounded by the date that status
    /// was reached. When <paramref name="byAcceptedDate"/> the bounds apply to <c>AcceptedDate</c> (« devis
    /// acceptés ce mois »); otherwise to <c>CreatedAt</c> (« devis en attente de réponse », a Draft has no
    /// accepted date). A count, not a list — the dashboard needs the number, never the aggregates.
    /// </summary>
    /// <summary>
    /// Plan facts for the whole clinic, for the « à rappeler » worklist. Excludes <c>Cancelled</c> plans (a void
    /// devis is nothing to chase) and <c>Completed</c> ones (nothing left to do). Item counts come from the
    /// database, never from a materialised collection.
    /// </summary>
    Task<IReadOnlyList<RecallPlanFact>> GetRecallPlanFactsAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    Task<int> CountByStatusAsync(
        Guid clinicId,
        TreatmentPlanStatus status,
        DateTime? from = null,
        DateTime? toInclusive = null,
        bool byAcceptedDate = false,
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
    /// <param name="excludedPlanIds">
    /// Plans already represented by a non-cancelled bridge invoice. <b>Required</b>, exactly like the
    /// outstanding query's — so a cash read cannot silently skip the de-duplication.
    ///
    /// Since the devis→facture bridge now carries collected installment money onto the invoice at issue, a
    /// bridged plan's receipts live on the invoice track; counting them here as well would double the caisse.
    /// </param>
    Task<decimal> GetInstallmentCollectedBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default);

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
    /// <summary>
    /// Every échéance collection recorded in <c>[from, toInclusive]</c>, for the « extrait de caisse ».
    /// <para>
    /// The row-level sibling of <see cref="GetInstallmentCollectedBetweenAsync"/> and it must stay
    /// predicate-for-predicate identical to it — including <paramref name="excludedPlanIds"/>. That parameter is
    /// not optional: a devis bridged into an issued invoice has its collections carried across onto the invoice
    /// track, so a statement that listed them here as well would show the same money twice *and* stop summing to
    /// the totals. Voided rows are returned (the caller strikes them through and excludes them from the balance).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CaisseInstallmentPaymentRow>> GetInstallmentPaymentsBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestOverdueDueDate)>> GetInstallmentOutstandingByPatientAsync(
        Guid clinicId, DateTime asOfUtc, IReadOnlyCollection<Guid> excludedPlanIds, CancellationToken cancellationToken = default);

    Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
