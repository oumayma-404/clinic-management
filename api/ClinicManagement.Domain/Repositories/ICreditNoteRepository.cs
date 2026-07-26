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

    /// <summary>Sum of avoir amounts refunded in [from, to] for a clinic — netted into the caisse/recettes.</summary>
    Task<decimal> GetRefundedBetweenAsync(Guid clinicId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    Task<CreditNote> AddAsync(CreditNote creditNote, CancellationToken cancellationToken = default);
}
