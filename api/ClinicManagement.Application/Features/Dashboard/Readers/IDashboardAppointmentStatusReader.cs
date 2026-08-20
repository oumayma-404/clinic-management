using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Reads « Rendez-vous par statut » — the window's appointments, bucketed in time and folded into the five
/// <see cref="AppointmentStatusClass"/> classes.
/// </summary>
public interface IDashboardAppointmentStatusReader
{
    Task<AppointmentStatusMixDto> ReadAsync(
        Guid clinicId,
        AppointmentStatusWindow window,
        Guid? doctorId,
        CancellationToken cancellationToken);
}
