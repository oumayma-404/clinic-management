using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class PatientRepository : IPatientRepository
{
    private readonly ApplicationDbContext _context;

    public PatientRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .Include(p => p.Flags)
            .Include(p => p.MedicalHistoryEntries)
            .Include(p => p.FamilyHistoryEntries)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Patient?> GetByIdWithAppointmentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .Include(p => p.Appointments)
            .Include(p => p.Flags)
            .Include(p => p.Files)
            .Include(p => p.MedicalHistoryEntries)
            .Include(p => p.FamilyHistoryEntries)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Patient>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Patient>> GetByClinicIdAsync(
        Guid clinicId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Patients
            .Include(p => p.Flags.Where(f => f.IsActive))
            .Where(p => p.ClinicId == clinicId);

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<PatientLinkedDataCounts> GetLinkedDataCountsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        // Cancelled invoices, cancelled appointments and voided payments all still count: they are fiscal and
        // clinical records, and deleting the patient they belong to would orphan them just the same.
        return new PatientLinkedDataCounts(
            Appointments: await _context.Appointments.CountAsync(a => a.PatientId == patientId, cancellationToken),
            Invoices: await _context.Invoices.CountAsync(i => i.PatientId == patientId, cancellationToken),
            TreatmentPlans: await _context.TreatmentPlans.CountAsync(t => t.PatientId == patientId, cancellationToken),
            DentalRecords: await _context.DentalRecords.CountAsync(d => d.PatientId == patientId, cancellationToken),
            ToothStates: await _context.ToothStates.CountAsync(t => t.PatientId == patientId, cancellationToken),
            MedicalDocuments: await _context.MedicalDocuments.CountAsync(m => m.PatientId == patientId, cancellationToken),
            Files: await _context.PatientFiles.CountAsync(f => f.PatientId == patientId, cancellationToken),
            Folders: await _context.PatientFolders.CountAsync(f => f.PatientId == patientId, cancellationToken),
            Flags: await _context.PatientFlags.CountAsync(f => f.PatientId == patientId, cancellationToken),
            RecurringAppointments: await _context.RecurringAppointments.CountAsync(r => r.PatientId == patientId, cancellationToken),
            MedicalHistoryEntries: await _context.PatientMedicalHistories.CountAsync(h => h.PatientId == patientId, cancellationToken),
            FamilyHistoryEntries: await _context.PatientFamilyHistories.CountAsync(h => h.PatientId == patientId, cancellationToken),
            LabOrders: await _context.LabWorkOrders.CountAsync(l => l.PatientId == patientId, cancellationToken),
            WaitingListEntries: await _context.WaitingListEntries.CountAsync(w => w.PatientId == patientId, cancellationToken),
            Notifications: await _context.Notifications.CountAsync(n => n.PatientId == patientId, cancellationToken));
    }

    public async Task<PatientArchiveBlockers> GetArchiveBlockersAsync(
        Guid patientId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        // Outstanding is read the same way « Créances » reads it, so a patient who looks settled there is
        // archivable and one who does not, is not.
        var invoiceOutstanding = await _context.Invoices
            .Where(i => i.PatientId == patientId
                        && i.Status != InvoiceStatus.Draft
                        && i.Status != InvoiceStatus.Cancelled
                        && i.TotalTtc > i.AmountCollected)
            .SumAsync(i => (decimal?)(i.TotalTtc - i.AmountCollected), cancellationToken) ?? 0m;

        var debtBearing = PlanBillingRules.DebtBearingPlanStatuses.ToArray();
        var installmentOutstanding = await _context.TreatmentPlans
            .Where(p => p.PatientId == patientId && debtBearing.Contains(p.Status))
            .SelectMany(p => p.Installments)
            .Where(i => i.Amount > i.AmountPaid)
            .SumAsync(i => (decimal?)(i.Amount - i.AmountPaid), cancellationToken) ?? 0m;

        var futureAppointments = await _context.Appointments
            .CountAsync(a => a.PatientId == patientId
                             && a.AppointmentDateTime > asOfUtc
                             && a.Status != AppointmentStatus.Cancelled,
                cancellationToken);

        return new PatientArchiveBlockers(invoiceOutstanding, installmentOutstanding, futureAppointments);
    }

    public async Task<int> CountByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .CountAsync(p => p.ClinicId == clinicId, cancellationToken);
    }

    public async Task<int> CountFlaggedByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .Where(p => p.ClinicId == clinicId && p.Flags.Any(f => f.IsActive))
            .CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<Patient>> GetFlaggedPatientsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .Include(p => p.Flags.Where(f => f.IsActive))
            .Where(p => p.Flags.Any(f => f.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<Patient> AddAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        await _context.Patients.AddAsync(patient, cancellationToken);
        return patient;
    }

    public Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(patient);
        if (entry.State == EntityState.Detached)
        {
            // Entity is not tracked, attach and mark as modified
            _context.Patients.Update(patient);
        }
        else
        {
            // Entity is already tracked - mark only the UpdatedAt property as modified
            // This prevents EF Core from trying to update all columns
            entry.Property(p => p.UpdatedAt).IsModified = true;
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var patient = await GetByIdAsync(id, cancellationToken);
        if (patient != null)
        {
            _context.Patients.Remove(patient);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Patients.AnyAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddMedicalHistoryEntryAsync(PatientMedicalHistory entry, CancellationToken cancellationToken = default)
    {
        await _context.PatientMedicalHistories.AddAsync(entry, cancellationToken);
    }

    public async Task AddFamilyHistoryEntryAsync(PatientFamilyHistory entry, CancellationToken cancellationToken = default)
    {
        await _context.PatientFamilyHistories.AddAsync(entry, cancellationToken);
    }
}



