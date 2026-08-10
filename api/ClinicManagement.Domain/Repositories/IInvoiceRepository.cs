using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One payment row behind the caisse statement. A projection rather than a <c>Payment</c>: the statement needs
/// the owning invoice's number, which a bare <c>Payment</c> cannot reach.
/// <para>
/// It carries <c>PatientId</c> and <b>not</b> the patient's name: <c>Invoice</c> has no <c>Patient</c> navigation
/// property, so there is nothing to project from. The caller resolves every name in one pass through
/// <c>IPatientRepository.GetByIdsAsync</c> — which exists for exactly this shape of problem.
/// </para>
/// </summary>
public sealed record CaissePaymentRow(
    Guid PaymentId,
    Guid InvoiceId,
    string? InvoiceNumber,
    Guid PatientId,
    decimal Amount,
    PaymentMethod Method,
    DateTime PaidOn,
    bool IsVoided,
    string? VoidReason,
    string? VoidedByName,
    // Cheque identity (L8). On the statement it is the difference between « Chèque 45,000 » — which could be
    // anything — and a line naming the cheque somebody still has to take to the bank.
    string? ChequeNumber = null,
    string? ChequeBankName = null,
    DateTime? ChequeDueDate = null,
    // The banked mark (Group B). Only « chèques à encaisser » reads it — the statement lists a cheque by when it
    // was received, which banking does not change — but it rides the same projection because it is the same row.
    DateTime? ChequeBankedOn = null,
    string? ChequeBankedByName = null);

public interface IInvoiceRepository
{
    /// <summary>Load an invoice with its lines and payments.</summary>
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>List a clinic's invoices, filtered by issue-date range / patient / status.</summary>
    /// <param name="searchTerm">
    /// Matched in SQL over the invoice number and the patient's name (the latter as an EXISTS against
    /// <c>Patients</c>, since <c>Invoice</c> carries no <c>Patient</c> navigation). It must be here rather than
    /// in the handler: names are resolved after the page is cut, so a filter applied there would only ever see
    /// the rows already on it.
    /// </param>
    /// <param name="paging">The page to return, or null for every match (« Solde patient » and the revenue read).</param>
    Task<PagedResult<Invoice>> GetFilteredAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        Guid? patientId = null,
        InvoiceStatus? status = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest sequence number already assigned for a clinic in a given year (0 when none). The next
    /// issued invoice uses this + 1, giving a gapless per-clinic-per-year sequence.
    /// </summary>
    Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sum of payments received in [from, to] across the clinic's non-cancelled invoices.
    /// </summary>
    /// <param name="doctorId">
    /// L9 — when supplied, only invoices <b>attributed to that practitioner</b>. ⚠️ An unattributed invoice is
    /// therefore <i>excluded</i>, not silently included: a per-practitioner figure that quietly absorbed every
    /// historical row would make two dentists' filtered totals sum to more than the clinic's.
    /// </param>
    Task<decimal> GetCollectedBetweenAsync(
        Guid clinicId, DateTime from, DateTime to, Guid? doctorId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sum of <c>TotalTtc</c> over the clinic's invoices <b>issued</b> in <c>[from, toInclusive]</c> — the
    /// dashboard's « Facturé ». Drafts carry no number and cancelled invoices are void, so both are excluded,
    /// matching <c>GetInvoiceRevenueQuery</c>'s rule exactly.
    /// <para>
    /// A projected <c>SUM</c> rather than a reuse of <see cref="GetFilteredAsync"/>: that method materialises every
    /// invoice <b>with its lines and payments</b>, which is acceptable for a screen listing them and wasteful for a
    /// single figure on the app's home page.
    /// </para>
    /// </summary>
    /// <param name="doctorId">L9 — same rule as <see cref="GetCollectedBetweenAsync"/>'s: attributed rows only.</param>
    Task<decimal> GetInvoicedBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, Guid? doctorId = null, CancellationToken cancellationToken = default);

    // NOTE: there is deliberately no GetCollectedByMonthAsync. One was written for the « Tendance » sparkline and
    // removed: bucketing by the clinic-local month required date arithmetic on a `timestamptz` column
    // (`GroupBy(p => p.PaidOn.AddMinutes(offset).Month)`), which has no valid PostgreSQL translation — it failed at
    // runtime with `42883: function pg_catalog.timezone(unknown, interval) does not exist` while every unit test
    // passed, because they all mock this interface. DashboardTrendReader now derives each month's UTC bounds through
    // ClinicClock and calls GetCollectedBetweenAsync once per month instead: no timezone maths reaches the database,
    // and each point is produced by the same method the « Encaissé » figure uses.

    /// <summary>
    /// Outstanding invoice balance per patient (TTC − collected) across the clinic's issued, non-cancelled,
    /// non-draft invoices — only patients whose invoice balance is &gt; 0. Feeds the unified per-patient
    /// balance, the clinic receivables list, and the dashboard total-outstanding figure.
    /// <para>
    /// <b><c>OldestUnpaidIssueDate</c> is what ages the debt (J7).</b> This read used to return the total alone,
    /// so « Créances » could only date the plan track and the « Retard » column was blank for pure invoice
    /// debt — which is where most of a clinic's debt is. A note d'honoraires has no due date: it is payable on
    /// issue, so its issue date <i>is</i> the moment the debt started, and the oldest one over a patient's
    /// unpaid notes is the age of their invoice debt. Null only when a legacy row carries no issue date.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestUnpaidIssueDate)>> GetOutstandingByPatientAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One row per devis→facture bridge in the clinic: the plan it was generated from, the invoice's id,
    /// number and status. A light projection (no lines/payments loaded) so a plan can show « Facturé » and
    /// the money reads can count the invoice instead of the plan without over-fetching. Cancelled invoices
    /// are included — the caller decides whether a cancelled bridge still represents the plan.
    /// </summary>
    Task<IReadOnlyList<(Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)>>
        GetTreatmentPlanLinksAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One row per invoice line that bills a fiche de soins: the record it bills, and the invoice's id, number
    /// and status. The sibling of <see cref="GetTreatmentPlanLinksAsync"/> for the *act-level* question — "is the
    /// work this fiche recorded already on a live invoice?" — which the plan-level bridge link cannot answer.
    /// <para>
    /// A light projection on purpose. The only alternative was <c>GetFilteredAsync</c>, which loads every invoice
    /// of the patient <b>with its lines and payments</b> to test one id — the exact over-fetch the audit's § 9.7
    /// exists to remove. Cancelled invoices are included; the caller decides whether one still represents the work.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<(Guid DentalRecordId, Guid InvoiceId, string? Number, InvoiceStatus Status)>>
        GetDentalRecordLinksAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One row per invoice raised against one of <paramref name="appointmentIds"/>: the visit, and the invoice's
    /// id, number and status (AC-P6.13). The third sibling of <see cref="GetTreatmentPlanLinksAsync"/> and
    /// <see cref="GetDentalRecordLinksAsync"/>, answering "is this visit billed, and on which note?".
    /// <para>
    /// Bounded by the id set rather than clinic-wide like its two siblings, deliberately: those answer a
    /// per-patient question over a naturally small set, while this one is read by the agenda, whose caller has a
    /// date window. Returning every appointment-linked invoice the clinic has ever raised in order to annotate one
    /// week of the calendar grows without limit.
    /// </para>
    /// <para>Cancelled invoices are included — the caller decides whether a cancelled note still bills the visit.</para>
    /// </summary>
    Task<IReadOnlyList<(Guid AppointmentId, Guid InvoiceId, string? Number, InvoiceStatus Status)>>
        GetAppointmentLinksAsync(
            Guid clinicId,
            IReadOnlyCollection<Guid> appointmentIds,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Every payment row recorded in <c>[from, toInclusive]</c>, for the « extrait de caisse ».
    /// <para>
    /// The row-level sibling of <see cref="GetCollectedBetweenAsync"/>, and it must stay predicate-for-predicate
    /// identical to it: same clinic filter, same <c>Status != Cancelled</c> exclusion, same inclusive bounds, and
    /// <b>voided rows are returned</b> rather than filtered. The sum excludes them; the statement shows them
    /// struck through (§ 1 keeps a void visible, motif and actor included), so the filter lives in the caller
    /// where both behaviours can be derived from one read.
    /// </para>
    /// <para>
    /// A light projection, not <c>GetFilteredAsync</c> — the statement needs eleven scalars per row, not every
    /// invoice of the clinic with its lines (§ 9.7).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CaissePaymentRow>> GetPaymentsBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same money <see cref="GetCollectedBetweenAsync"/> sums, split by <c>PaymentMethod</c> — la caisse's
    /// « dont espèces », so the owner can separate what is physically in the drawer from a cheque nobody has
    /// banked yet.
    /// <para>
    /// ⚠️ A <c>GROUP BY</c> sibling of the SUM, deliberately, and <b>not</b> a grouping of
    /// <see cref="GetPaymentsBetweenAsync"/>'s rows in the caller: those include voided payments (the statement
    /// strikes them through) while the SUM drops them, so summing them would produce a breakdown that
    /// silently disagrees with <c>CashIn</c> unless the caller re-applied <c>!IsVoided</c> — one predicate
    /// remembered in two places, which is exactly how the two figures drift. Predicate-for-predicate identical
    /// to the SUM instead, so <c>Σ breakdown == CashIn</c> holds by construction.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<PaymentMethodTotal>> GetCollectedByMethodBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every **cheque** payment recorded against the clinic's non-cancelled invoices, non-voided, optionally
    /// bounded by the cheque's own <c>ChequeDueDate</c> — the invoice half of « chèques à encaisser ». First
    /// reader of <c>IX_Payments_ChequeDueDate</c>.
    /// <para>
    /// ⚠️ The date bounds are on the <b>due date</b>, not on <c>PaidOn</c>: the question is « what can I take to
    /// the bank this month? », and a post-dated cheque is received long before it can be presented. Rows with no
    /// due date are **always returned** regardless of the bounds — a cheque nobody wrote a date on is precisely
    /// the money-lost case the view exists for, so excluding it from a bounded window would hide it for ever.
    /// </para>
    /// <para>
    /// Unpaged: the caller merges this with the treatment-plan half, orders the union and pages in memory — the
    /// same shape as the « extrait de caisse » and « Créances », where no single query knows a row's position.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CaissePaymentRow>> GetChequePaymentsAsync(
        Guid clinicId,
        DateTime? dueFrom = null,
        DateTime? dueTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>Load the invoice that owns a given payment (with lines + payments), or null. Clinic-agnostic — the caller guards the clinic.</summary>
    Task<Invoice?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<Invoice> AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
    Task UpdateAsync(Invoice invoice, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
