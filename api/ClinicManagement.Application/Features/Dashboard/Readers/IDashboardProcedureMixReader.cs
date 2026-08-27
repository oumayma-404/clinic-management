using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>What the period's work was made of, by act type.</summary>
public interface IDashboardProcedureMixReader
{
    Task<List<ProcedureMixPointDto>> ReadAsync(
        Guid clinicId, DashboardPeriod period, Guid? doctorId, CancellationToken cancellationToken);
}
