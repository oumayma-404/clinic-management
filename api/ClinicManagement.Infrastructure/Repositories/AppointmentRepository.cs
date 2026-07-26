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
            .Include(a => a.ProcedureType)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetByClinicIdAsync(
        Guid clinicId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Where(a => a.ClinicId == clinicId);

        if (startDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDateTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDateTime <= endDate.Value);
        }

        if (doctorId.HasValue)
        {
            query = query.Where(a => a.DoctorId == doctorId.Value);
        }

        return await query
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountByClinicIdAsync(
        Guid clinicId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        AppointmentStatus? status = null,
        IReadOnlyCollection<AppointmentStatus>? excludeStatuses = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Appointments.Where(a => a.ClinicId == clinicId);

        if (startDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDateTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDateTime <= endDate.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (excludeStatuses is { Count: > 0 })
        {
            query = query.Where(a => !excludeStatuses.Contains(a.Status));
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetUpcomingAppointmentsAsync(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
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
            .Include(a => a.ProcedureType)
            .Where(a => a.AppointmentDateTime >= startOfDay && a.AppointmentDateTime < endOfDay)
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetByProcedureTypeIdAsync(Guid procedureTypeId, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Where(a => a.ProcedureTypeId == procedureTypeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Appointment>> GetByTreatmentPlanItemIdsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> treatmentPlanItemIds,
        CancellationToken cancellationToken = default)
    {
        if (treatmentPlanItemIds.Count == 0)
        {
            return Array.Empty<Appointment>();
        }

        // No Include here (unlike the other reads): the plan-workflow projection needs only the link,
        // the date and the status, and this runs for every plan on a list page.
        return await _context.Appointments
            .Where(a => a.ClinicId == clinicId
                        && a.TreatmentPlanItemId != null
                        && treatmentPlanItemIds.Contains(a.TreatmentPlanItemId.Value))
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



