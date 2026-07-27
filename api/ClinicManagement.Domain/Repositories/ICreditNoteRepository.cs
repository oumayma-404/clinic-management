using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface ICreditNoteRepository
{
    /// <summary>
    /// Highest sequence number already assigned for a clinic in a given year (0 when none). The next avoir
    /// uses this + 1, giving a gapless per-clinic-per-year sequence (mirrors the invoice numbering).
    /// </summary>
    Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default);

    /// <summary>Sum of all avoirs already issued against a given invoice (guards against over-crediting).</summary>
    Task<decimal> GetTotalForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Credited total per invoice, for a batch of invoices — one query behind the whole « Factures » list so
    /// a row can show what has been credited back without an N+1.
    /// Invoices with no avoir are simply absent from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetTotalsForInvoicesAsync(
        IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default);

    /// <summary>Sum of avoir amounts refunded in [from, to] for a clinic — netted into the caisse/recettes.</summary>
    Task<decimal> GetRefundedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every avoir issued against an invoice, newest first.
    ///
    /// Until now this repository was write-only — no get, no list — so once an avoir was created the clinic
    /// could never see its number, motif or amount again, could not hand it to the patient, and could not
    /// tell that an invoice already had one.
    /// </summary>
    Task<IReadOnlyList<CreditNote>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>A single avoir by id, for its PDF. Caller re-checks the clinic.</summary>
    Task<CreditNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every avoir a clinic has issued in [from, to], newest first.</summary>
    Task<IReadOnlyList<CreditNote>> GetByClinicIdAsync(
        Guid clinicId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);

    Task<CreditNote> AddAsync(CreditNote creditNote, CancellationToken cancellationToken = default);
}
