using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class CalendarImportRunRepository : ICalendarImportRunRepository
{
    private readonly ApplicationDbContext _context;

    public CalendarImportRunRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CalendarImportRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.CalendarImportRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<PagedResult<CalendarImportRun>> GetHistoryAsync(
        Guid clinicId, PageRequest? paging, CancellationToken cancellationToken = default) =>
        await _context.CalendarImportRuns
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.StartedAtUtc)
            // The recurring pass runs every clinic in one loop, so two runs can share a tick. OFFSET over a
            // non-unique sort shows one row twice and skips another.
            .ThenBy(r => r.Id)
            .ToPagedResultAsync(paging, cancellationToken);

    public async Task<CalendarImportRun?> GetLatestUndoableAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // « Still has rows » is asked of the ROWS, not of the run's own counters: a practice may have deleted a
        // few imported visits by hand, and a run whose last row went that way has nothing left to undo even
        // though its counters still say it created a hundred. The counters are a record of what happened; the
        // stamps are what an undo can still act on, and only the second question is being asked here.
        var candidates = await _context.CalendarImportRuns
            .Where(r => r.ClinicId == clinicId && r.RevertedAtUtc == null)
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenBy(r => r.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var run in candidates)
        {
            var hasRows =
                await _context.Appointments.AnyAsync(a => a.CalendarImportRunId == run.Id, cancellationToken)
                || await _context.Patients.AnyAsync(p => p.CalendarImportRunId == run.Id, cancellationToken);

            if (hasRows)
            {
                return run;
            }
        }

        return null;
    }

    public async Task<CalendarImportRunContents> GetContentsAsync(
        Guid clinicId, Guid runId, CancellationToken cancellationToken = default)
    {
        // The clinic term is on every read below, not just the first. The run id arrives from a URL, and a run
        // belonging to another practice must read as EMPTY rather than as a set of rows somebody is one click
        // away from deleting.
        var appointments = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Procedures)
            .Where(a => a.ClinicId == clinicId && a.CalendarImportRunId == runId)
            .ToListAsync(cancellationToken);

        var patientRows = await _context.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId && p.CalendarImportRunId == runId)
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToListAsync(cancellationToken);

        if (appointments.Count == 0)
        {
            return new CalendarImportRunContents(
                Array.Empty<CalendarImportRunVisit>(),
                patientRows
                    .Select(p => new CalendarImportRunPatient(p.Id, $"{p.FirstName} {p.LastName}".Trim()))
                    .ToList());
        }

        var appointmentIds = appointments.Select(a => a.Id).ToList();

        // Five batched link reads, bounded by this run's ids — never clinic-wide. A practice reverting one import
        // must not pay for a scan of every fiche it has ever written.
        var ficheRows = await _context.DentalRecords
            .AsNoTracking()
            .Where(r => r.AppointmentId != null && appointmentIds.Contains(r.AppointmentId.Value))
            .Select(r => new { AppointmentId = r.AppointmentId!.Value, r.Id })
            .ToListAsync(cancellationToken);

        var ficheIds = ficheRows.Select(r => r.Id).ToList();

        // ⚠️ A CANCELLED note is not a blocker, matching `AppointmentInvoiceLinks`' own exclusion: it bills
        // nothing, so it is not work invested in the visit — and counting it would strand exactly the visits a
        // practice cancelled while trying to tidy up, which are the ones this undo exists for.
        var invoicedAppointmentIds = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.ClinicId == clinicId
                        && i.Status != InvoiceStatus.Cancelled
                        && i.AppointmentId != null
                        && appointmentIds.Contains(i.AppointmentId.Value))
            .Select(i => i.AppointmentId!.Value)
            .ToListAsync(cancellationToken);

        // A note may name the FICHE rather than the visit — the same gap `VisitClosureReader` documents, where
        // every séance billed before the appointment link existed carries a real, paid note the direct read
        // cannot see. Missing it here would delete a billed visit.
        var billedFicheIds = ficheIds.Count == 0
            ? new List<Guid>()
            : await _context.Invoices
                .AsNoTracking()
                .Where(i => i.ClinicId == clinicId
                            && i.Status != InvoiceStatus.Cancelled
                            && i.DentalRecordId != null
                            && ficheIds.Contains(i.DentalRecordId.Value))
                .Select(i => i.DentalRecordId!.Value)
                .ToListAsync(cancellationToken);

        var labOrderAppointmentIds = await _context.LabWorkOrders
            .AsNoTracking()
            .Where(o => o.ClinicId == clinicId
                        && o.AppointmentId != null
                        && appointmentIds.Contains(o.AppointmentId.Value))
            .Select(o => o.AppointmentId!.Value)
            .ToListAsync(cancellationToken);

        // A visit keeps its plan link after the devis is cancelled, so the link alone is not cover — the same
        // filter `VisitClosureReader` applies through `PlanBillingRules.DebtBearingPlanStatuses`.
        var planItemIds = appointments
            .SelectMany(a => a.LinkedTreatmentPlanItemIds)
            .Distinct()
            .ToList();

        var debtBearingItemIds = planItemIds.Count == 0
            ? new List<Guid>()
            // Shaped exactly like `TreatmentPlanRepository.GetDebtBearingItemIdsAsync` — plans down to items,
            // never an item DbSet (there is none) — and through `PlanBillingRules` rather than a retyped status
            // list, so this cannot drift from the four money reads that ask the same question.
            : await _context.TreatmentPlans
                .AsNoTracking()
                .Where(p => p.ClinicId == clinicId
                            && PlanBillingRules.DebtBearingPlanStatuses.Contains(p.Status))
                .SelectMany(p => p.Items)
                .Where(i => planItemIds.Contains(i.Id))
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

        // Materialised first: `appointments` is an in-memory list, so a `Contains` over a projection of it has no
        // SQL translation and would either throw or silently evaluate client-side over every patient of the clinic.
        var visitPatientIds = appointments
            .Where(a => a.PatientId.HasValue)
            .Select(a => a.PatientId!.Value)
            .Distinct()
            .ToList();

        var patientNames = visitPatientIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.Patients
                .AsNoTracking()
                .Where(p => p.ClinicId == clinicId && visitPatientIds.Contains(p.Id))
                .Select(p => new { p.Id, p.FirstName, p.LastName })
                .ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}".Trim(), cancellationToken);

        var fichesByAppointment = ficheRows
            .GroupBy(r => r.AppointmentId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());

        var invoiced = invoicedAppointmentIds.ToHashSet();
        var billedFiches = billedFicheIds.ToHashSet();
        var withLabOrder = labOrderAppointmentIds.ToHashSet();
        var debtBearing = debtBearingItemIds.ToHashSet();

        var visits = appointments
            .Select(a =>
            {
                fichesByAppointment.TryGetValue(a.Id, out var fiches);

                return new CalendarImportRunVisit(
                    AppointmentId: a.Id,
                    PatientId: a.PatientId,
                    PatientName: a.PatientId is { } pid && patientNames.TryGetValue(pid, out var name)
                        ? name
                        : "Patient introuvable",
                    AppointmentDateTime: a.AppointmentDateTime,
                    HasFiche: fiches is { Count: > 0 },
                    HasLiveInvoice: invoiced.Contains(a.Id)
                                    || (fiches?.Any(billedFiches.Contains) ?? false),
                    CoveredByPlan: a.LinkedTreatmentPlanItemIds.Any(debtBearing.Contains),
                    HasLabOrder: withLabOrder.Contains(a.Id),
                    HasProcedures: a.Procedures.Count > 0,
                    NothingToBill: a.IsNothingToBill,
                    Disregarded: a.IsDisregarded);
            })
            .ToList();

        return new CalendarImportRunContents(
            visits,
            patientRows
                .Select(p => new CalendarImportRunPatient(p.Id, $"{p.FirstName} {p.LastName}".Trim()))
                .ToList());
    }

    public async Task DeleteRunRowsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> appointmentIds,
        IReadOnlyCollection<Guid> patientIds,
        CancellationToken cancellationToken = default)
    {
        if (appointmentIds.Count > 0)
        {
            var ids = appointmentIds.ToList();

            // ⚠️ FIRST, and this ordering is the whole reason the method exists. `Notification.AppointmentId` and
            // `PushDelivery`'s are OnDelete(SetNull), so deleting the appointments below would NOT take their
            // queued reminders with them — it would orphan them with a null link, and the minutely dispatcher
            // would still send them. The patient gets « Rappel : votre rendez-vous demain » for a visit that no
            // longer exists.
            var reminders = await _context.Notifications
                .Where(n => n.AppointmentId != null && ids.Contains(n.AppointmentId.Value))
                .ToListAsync(cancellationToken);
            _context.Notifications.RemoveRange(reminders);

            var pushes = await _context.PushDeliveries
                .Where(d => d.AppointmentId != null && ids.Contains(d.AppointmentId.Value))
                .ToListAsync(cancellationToken);
            _context.PushDeliveries.RemoveRange(pushes);

            // The in-app feed rows too — « Rendez-vous créé », a post-visit prompt — otherwise the bell keeps
            // deep-linking to appointments that are gone, which renders as a dead row nobody can clear.
            var staff = await _context.StaffNotifications
                .Where(n => n.ClinicId == clinicId
                            && n.AppointmentId != null
                            && ids.Contains(n.AppointmentId.Value))
                .ToListAsync(cancellationToken);
            _context.StaffNotifications.RemoveRange(staff);

            // `AppointmentProcedure` children cascade (AppointmentConfiguration), so they are not listed here.
            var appointments = await _context.Appointments
                .Where(a => a.ClinicId == clinicId && ids.Contains(a.Id))
                .ToListAsync(cancellationToken);
            _context.Appointments.RemoveRange(appointments);
        }

        if (patientIds.Count > 0)
        {
            var ids = patientIds.ToList();

            // The « fiche importée, à compléter » bell rows. They carry a PatientId and no appointment, so the
            // pass above cannot have caught them.
            var patientNotifications = await _context.StaffNotifications
                .Where(n => n.ClinicId == clinicId && n.PatientId != null && ids.Contains(n.PatientId.Value))
                .ToListAsync(cancellationToken);
            _context.StaffNotifications.RemoveRange(patientNotifications);

            var patients = await _context.Patients
                .Where(p => p.ClinicId == clinicId && ids.Contains(p.Id))
                .ToListAsync(cancellationToken);
            _context.Patients.RemoveRange(patients);
        }
    }

    public async Task AddAsync(CalendarImportRun run, CancellationToken cancellationToken = default)
    {
        await _context.CalendarImportRuns.AddAsync(run, cancellationToken);
    }

    public Task UpdateAsync(CalendarImportRun run, CancellationToken cancellationToken = default)
    {
        _context.CalendarImportRuns.Update(run);
        return Task.CompletedTask;
    }
}
