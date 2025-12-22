using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _context;

    public AppointmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetUpcomingAppointmentsAsync(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Where(a => a.AppointmentDateTime >= fromDate &&
                       (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetAppointmentsForDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        return await _context.Appointments
            .Include(a => a.Patient)
            .Where(a => a.AppointmentDateTime >= startOfDay && a.AppointmentDateTime < endOfDay)
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<Appointment> AddAsync(Appointment appointment, CancellationToken cancellationToken = default)
    {
        await _context.Appointments.AddAsync(appointment, cancellationToken);
        return appointment;
    }

    public Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default)
    {
        _context.Appointments.Update(appointment);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var appointment = await GetByIdAsync(id);
        if (appointment != null)
        {
            _context.Appointments.Remove(appointment);
        }
    }
}



