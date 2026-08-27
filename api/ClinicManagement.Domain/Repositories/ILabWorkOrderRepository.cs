using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

public interface ILabWorkOrderRepository
{
    Task<LabWorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's lab work orders (with the Patient nav), newest first.</summary>
    /// <param name="status">When supplied, only orders currently in that stage.</param>
    /// <param name="patientId">
    /// When supplied, only that patient's orders. Folded in here rather than left to the separate
    /// <c>GetByPatientIdAsync</c> the handler used to branch to: that branch re-applied the status filter in
    /// memory, and only the unfiltered branch could have been paged.
    /// </param>
    /// <param name="searchTerm">
    /// Matched in SQL over prothésiste, description, notes, the patient's name and the <b>linked fiche
    /// fournisseur's</b> nom — the last was absent, so a bon filed under a laboratory was findable only through
    /// the free-text name retyped onto it.
    /// </param>
    /// <param name="supplierId">When supplied, only bons sent to that fiche fournisseur.</param>
    /// <param name="orderByExpectedDate">
    /// Order by « Prévu » ascending, dateless bons last, instead of newest-created first. What lets someone
    /// arriving from « Prothèses en retard : N » see the late ones together rather than reading dates by eye.
    /// </param>
    Task<PagedResult<LabWorkOrder>> GetByClinicIdAsync(
        Guid clinicId,
        LabOrderStatus? status = null,
        Guid? patientId = null,
        string? searchTerm = null,
        Guid? supplierId = null,
        bool orderByExpectedDate = false,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the clinic's orders are still at the laboratory (<c>Sent</c>) past their expected return date.
    /// An order with no <c>ExpectedDate</c> can never be late — there is nothing to be late against — so it is not
    /// counted, and neither is one already <c>Received</c>/<c>Fitted</c>.
    /// </summary>
    Task<int> CountOverdueAsync(Guid clinicId, DateTime asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>A patient's lab work orders, newest first.</summary>
    Task<IEnumerable<LabWorkOrder>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task<LabWorkOrder> AddAsync(LabWorkOrder order, CancellationToken cancellationToken = default);
    Task UpdateAsync(LabWorkOrder order, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
