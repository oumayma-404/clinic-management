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

    public async Task<IReadOnlyDictionary<Guid, Patient>> GetByIdsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Patient>();
        }

        // Deliberately no `Include`: the callers of this batch need identity (the full name), not the aggregate.
        // Pulling flags and both history collections for every patient with a balance is the cost the per-row
        // `GetByIdAsync` was already paying, and the read this replaces is the « Créances » list.
        var distinct = ids.Distinct().ToArray();

        return await _context.Patients
            .Where(p => p.ClinicId == clinicId && distinct.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
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
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Patients
            .Include(p => p.Flags.Where(f => f.IsActive))
            .Where(p => p.ClinicId == clinicId);

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        // Both bounds are inclusive, matching CountCreatedBetweenAsync — the dashboard's « Nouveaux patients »
        // links here with the same window, and a half-open list against a closed count would show a different
        // number of rows than the card that opened it.
        if (createdFrom.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= createdFrom.Value);
        }

        if (createdTo.HasValue)
        {
            query = query.Where(p => p.CreatedAt <= createdTo.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<int> CountCreatedBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Patients
            .Where(p => p.ClinicId == clinicId && p.CreatedAt >= from && p.CreatedAt <= toInclusive);

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        return await query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// AC-P4.41 — the bounded relance read. The handler previously loaded <b>every</b> patient and <b>every</b>
    /// appointment in the clinic and did all the work in memory, so a clinic with 4 000 patients and 30 000
    /// appointments paid for both full scans every time somebody opened the page.
    ///
    /// <para><b>What is in SQL:</b> clinic scope, the archived exclusion (AC-P4.43), the active snooze, the
    /// future-booking exclusion (as an <c>EXISTS</c>, so no appointment rows come back), the last completed
    /// visit (as a correlated <c>MAX</c>), and an upper bound on the recall anchor.</para>
    ///
    /// <para><b>Why the anchor bound is a superset, not the rule (AC-P4.42).</b> The rule is
    /// <c>anchor.AddMonths(interval) &lt;= now</c>. Rewriting that as <c>anchor &lt;= now.AddMonths(-interval)</c>
    /// so it can be a plain SQL comparison is <b>not</b> equivalent: <c>AddMonths</c> clamps to the end of the
    /// shorter month, and the clamp does not survive inversion. 31 January + 1 month is 28 February, so on
    /// 28 February that patient IS due — but 28 February − 1 month is 28 January, and 31 January is not
    /// ≤ 28 January, so the inverted form would drop them. The clamp can move a date by at most three days
    /// (31 → 28), so this bound subtracts the interval and then adds three days back, guaranteeing a superset;
    /// the handler applies the exact <c>AddMonths</c> test to what comes back. Identical results, bounded read.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<RecallCandidate>> GetRecallCandidatesAsync(
        Guid clinicId,
        DateTime anchorOnOrBeforeUtc,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? alwaysIncludePatientIds = null,
        CancellationToken cancellationToken = default)
    {
        return await RecallCandidateQuery(_context, clinicId, anchorOnOrBeforeUtc, nowUtc, alwaysIncludePatientIds)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The relance query as an un-executed <see cref="IQueryable{T}"/>. Split out of
    /// <see cref="GetRecallCandidatesAsync"/> so <c>RecallQueryTranslationTests</c> can compile <b>this</b>
    /// expression tree to SQL rather than a copy of it — a test holding its own copy would keep passing after
    /// this one changed, which is the failure mode it is meant to catch. Nothing else should call it.
    /// </summary>
    public static IQueryable<RecallCandidate> RecallCandidateQuery(
        ApplicationDbContext context,
        Guid clinicId,
        DateTime anchorOnOrBeforeUtc,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? alwaysIncludePatientIds = null)
    {
        var completed = AppointmentStatus.Completed;
        // Materialised once so EF parameterises a single array rather than re-evaluating the collection per row.
        var includeIds = alwaysIncludePatientIds is { Count: > 0 } ? alwaysIncludePatientIds.ToArray() : null;

        return context.Patients
            .Where(p => p.ClinicId == clinicId)
            // AC-P4.43 — relancing someone the clinic has archived is exactly what archiving is meant to stop.
            .Where(p => !p.IsArchived)
            // An active snooze temporarily removes the patient from the list.
            .Where(p => p.RecallSnoozedUntil == null || p.RecallSnoozedUntil <= nowUtc)
            // A patient with a future booked appointment does not need a recall. `Any` becomes EXISTS, so the
            // appointment rows are never materialised — that was half the original cost.
            .Where(p => !context.Appointments.Any(a =>
                a.PatientId == p.Id
                && a.AppointmentDateTime > nowUtc
                && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)))
            .Select(p => new
            {
                p.Id,
                p.FirstName,
                p.LastName,
                Phone = p.PhoneNumber != null ? p.PhoneNumber.Value : null,
                p.CreatedAt,
                p.RecallReason,
                p.LastRecallContactedAt,
                // Completed appointments only — the deliberate consequence of the Completed → Cancelled
                // transition (AC-P1.11): a cancelled visit did not happen, so it must not postpone a recall.
                LastCompletedVisit = context.Appointments
                    .Where(a => a.PatientId == p.Id && a.Status == completed)
                    .Max(a => (DateTime?)a.AppointmentDateTime),
            })
            // The date bound, applied to the same anchor the handler will measure from — OR the patient is one the
            // caller already knows qualifies for another reason (a stalled devis, an overdue échéance), which has
            // nothing to do with when they were last seen. The bound itself is never dropped: doing so would return
            // every eligible patient in the clinic on each page load, undoing AC-P4.41.
            .Where(x => (x.LastCompletedVisit ?? x.CreatedAt) <= anchorOnOrBeforeUtc
                        || (includeIds != null && includeIds.Contains(x.Id)))
            .Select(x => new RecallCandidate(
                x.Id,
                x.FirstName,
                x.LastName,
                x.Phone,
                x.LastCompletedVisit ?? x.CreatedAt,
                x.LastCompletedVisit,
                x.RecallReason,
                x.LastRecallContactedAt));
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



