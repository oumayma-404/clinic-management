using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>A patient's lines from the cabinet's previous software. Written once by the import, then only read.</summary>
public interface IPatientLegacyHistoryRepository
{
    /// <summary>One patient's lines, newest day first, undated lines last, the old record's order within a day.</summary>
    Task<IReadOnlyList<PatientLegacyHistoryEntry>> GetByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<PatientLegacyHistoryEntry> entries, CancellationToken cancellationToken = default);
}
