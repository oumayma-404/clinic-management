using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface ILabWorkOrderRepository
{
    Task<LabWorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A clinic's lab work orders (with the Patient nav), newest first.</summary>
    Task<IEnumerable<LabWorkOrder>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>A patient's lab work orders, newest first.</summary>
    Task<IEnumerable<LabWorkOrder>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task<LabWorkOrder> AddAsync(LabWorkOrder order, CancellationToken cancellationToken = default);
    Task UpdateAsync(LabWorkOrder order, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
