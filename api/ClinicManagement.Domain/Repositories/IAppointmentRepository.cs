using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetUpcomingAppointmentsAsync(DateTime fromDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<Appointment>> GetAppointmentsForDateAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<Appointment> AddAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



