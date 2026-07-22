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

    Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
