using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

public interface ILabWorkOrderRepository
{
    Task<LabWorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's lab work orders (with the Patient nav), newest first.</summary>
    /// <param name="status">When supplied, only orders currently in that stage.</param>
    Task<IEnumerable<LabWorkOrder>> GetByClinicIdAsync(
        Guid clinicId, LabOrderStatus? status = null, CancellationToken cancellationToken = default);

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
