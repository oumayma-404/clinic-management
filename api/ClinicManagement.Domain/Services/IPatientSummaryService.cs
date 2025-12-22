using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Services;

public interface IPatientSummaryService
{
    Task<string> GenerateSummaryAsync(Patient patient, Appointment appointment, CancellationToken cancellationToken = default);
}



