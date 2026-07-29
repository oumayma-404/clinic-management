using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

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
    string? VoidedByName);

public interface IInvoiceRepository
{
    /// <summary>Load an invoice with its lines and payments.</summary>
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>List a clinic's invoices, filtered by issue-date range / patient / status.</summary>
    Task<IEnumerable<Invoice>> GetFilteredAsync(
        Guid clinicId,
        DateTime? from = null,
        DateTime? to = null,
        Guid? patientId = null,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest sequence number already assigned for a clinic in a given year (0 when none). The next
    /// issued invoice uses this + 1, giving a gapless per-clinic-per-year sequence.
    /// </summary>
    Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default);

    /// <summary>Sum of payments received in [from, to] across the clinic's non-cancelled invoices.</summary>
    Task<decimal> GetCollectedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

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
    Task<decimal> GetInvoicedBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default);

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
    /// </summary>
    Task<IReadOnlyList<(Guid PatientId, decimal Outstanding)>> GetOutstandingByPatientAsync(
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

    /// <summary>Load the invoice that owns a given payment (with lines + payments), or null. Clinic-agnostic — the caller guards the clinic.</summary>
    Task<Invoice?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// El Fatoora outbox: invoices <c>Queued</c> for e-invoicing and due for a dispatch attempt
    /// (<c>EInvoiceNextAttemptAt &lt;= now</c>), across all clinics, oldest-due first, capped at
    /// <paramref name="maxCount"/>. Loaded with lines + payments for TEIF generation.
    /// </summary>
    Task<IEnumerable<Invoice>> GetDueForElFatooraDispatchAsync(int maxCount, DateTime now, CancellationToken cancellationToken = default);

    Task<Invoice> AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
    Task UpdateAsync(Invoice invoice, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
