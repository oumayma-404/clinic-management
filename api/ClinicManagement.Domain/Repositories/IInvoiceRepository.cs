using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

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
