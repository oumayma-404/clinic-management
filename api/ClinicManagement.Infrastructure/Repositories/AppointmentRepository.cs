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
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetByClinicIdAsync(
        Guid clinicId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? doctorId = null,
        Guid? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
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

        if (patientId.HasValue)
        {
            query = query.Where(a => a.PatientId == patientId.Value);
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

    public async Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusBetweenAsync(
        Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken cancellationToken = default)
    {
        // One GROUP BY. The dashboard needs Completed, NoShow, Cancelled and the total over the SAME window (the
        // taux d'absence denominator); four CountByClinicIdAsync calls would be four round trips whose bounds
        // could drift apart, which is exactly the failure the single-authority period exists to prevent.
        // Bounds are inclusive on both ends, matching CountByClinicIdAsync.
        //
        // ⚠️ Séances the practice has RETIRED are excluded here, and this is the call site that makes the mark
        // mean anything. « Retirer de la liste » is honoured by `VisitClosureRules.IsOnWorklist`, which governs
        // the worklist and the dashboard chip — but the taux d'absence and « Rendez-vous par statut » are this
        // GROUP BY, and nothing about the worklist reaches them. Excluded from one but not the other, a cabinet
        // retires a hundred phantom visits, watches the list go quiet, and reads exactly the same wrong absence
        // rate it complained about.
        var rows = await _context.Appointments
            .Where(a => a.ClinicId == clinicId
                        && a.DisregardedAtUtc == null
                        && a.AppointmentDateTime >= from
                        && a.AppointmentDateTime <= toInclusive)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    public async Task<IReadOnlyList<AppointmentStatusSlot>> GetStatusTimelineAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ NO date arithmetic here, and that is the point of the whole method. The caller buckets by
        // clinic-LOCAL day/week/month, and shifting a `timestamptz` inside the query is what produced
        // `42883: function pg_catalog.timezone(unknown, interval) does not exist` for the money trend. So this
        // stays a plain range filter and a projection, and `ClinicClock` does the shifting in memory.
        //
        // Projected to two value-type columns — no navigations, no tracking — so the row count the window allows
        // (up to 366 days) stays a cheap transfer rather than a few thousand hydrated aggregates.
        // Retired séances are excluded for `CountByStatusBetweenAsync`'s reason: this feeds the same card, and a
        // chart still painting a column the figure above it no longer counts is worse than either alone.
        var query = _context.Appointments
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.DisregardedAtUtc == null
                        && a.AppointmentDateTime >= from
                        && a.AppointmentDateTime <= toInclusive);

        if (doctorId.HasValue)
        {
            query = query.Where(a => a.DoctorId == doctorId.Value);
        }

        return await query
            .OrderBy(a => a.AppointmentDateTime)
            .ThenBy(a => a.Id)
            .Select(a => new AppointmentStatusSlot(a.AppointmentDateTime, a.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProcedureMixRow>> GetProcedureMixBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        // One GROUP BY over the child act rows. Cancelled and no-show visits are excluded for the same reason
        // « RDV honorés » excludes them: an act nobody performed is not part of what the clinic did.
        var rows = await _context.Appointments
            .Where(a => a.ClinicId == clinicId
                        && a.AppointmentDateTime >= from
                        && a.AppointmentDateTime <= toInclusive
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.NoShow
                        && (doctorId == null || a.DoctorId == doctorId))
            .SelectMany(a => a.Procedures)
            // Keyed on the snapshot pair rather than on a live-else-snapshot CASE: this shape is guaranteed to
            // translate, and rows sharing an id are merged by the reader, which overlays the live name anyway.
            .GroupBy(p => new { p.ProcedureTypeId, p.ProcedureName })
            .Select(g => new ProcedureMixRow(
                g.Key.ProcedureTypeId,
                g.Key.ProcedureName,
                // Any snapshot of the group will do — they only differ when the act was recoloured, and the live
                // colour wins over all of them one layer up.
                g.Max(p => p.ColorHex),
                g.Count(),
                g.Sum(p => p.DurationMinutes ?? 0)))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IEnumerable<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetUpcomingAppointmentsAsync(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
            .Where(a => a.AppointmentDateTime >= fromDate &&
                       (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The bounded candidate set behind <see cref="GetRunningNotStartedAsync"/>, exposed so
    /// <c>AppointmentProgressQueryTranslationTests</c> compiles the <b>production</b> expression tree rather than
    /// a copy of it. No <c>Include</c>s: the pass reads a status and a clinic id and writes a status.
    /// </summary>
    public static IQueryable<Appointment> RunningCandidateQuery(
        ApplicationDbContext db, DateTime nowUtc, TimeSpan longestVisit)
    {
        var earliestStart = nowUtc - longestVisit;

        return db.Appointments
            .Where(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
                        && a.AppointmentDateTime <= nowUtc
                        && a.AppointmentDateTime > earliestStart)
            // Unique column last: this read is not paged today, but an unstable order over a set the pass mutates
            // makes a partial failure report a different subset every tick.
            .OrderBy(a => a.AppointmentDateTime)
            .ThenBy(a => a.Id);
    }

    public async Task<IReadOnlyList<Appointment>> GetRunningNotStartedAsync(
        DateTime nowUtc, TimeSpan longestVisit, CancellationToken cancellationToken = default)
    {
        var candidates = await RunningCandidateQuery(_context, nowUtc, longestVisit)
            .ToListAsync(cancellationToken);

        // The half SQL cannot do — see the interface for why `AppointmentDateTime + Duration` has no translation.
        return candidates.Where(a => a.AppointmentDateTime + a.Duration > nowUtc).ToList();
    }

    /// <summary>
    /// The bounded candidate set behind <see cref="GetElapsedOpenAsync"/>, exposed for
    /// <see cref="RunningCandidateQuery"/>'s reason — the translation test compiles the <b>production</b>
    /// expression tree rather than a copy of it.
    /// </summary>
    public static IQueryable<Appointment> ElapsedCandidateQuery(
        ApplicationDbContext db, DateTime nowUtc, TimeSpan lookback)
    {
        var earliestStart = nowUtc - lookback;

        return db.Appointments
            .Where(a => (a.Status == AppointmentStatus.Scheduled
                         || a.Status == AppointmentStatus.Confirmed
                         || a.Status == AppointmentStatus.InProgress)
                        && a.AppointmentDateTime <= nowUtc
                        && a.AppointmentDateTime > earliestStart)
            .OrderBy(a => a.AppointmentDateTime)
            .ThenBy(a => a.Id);
    }

    public async Task<IReadOnlyList<Appointment>> GetElapsedOpenAsync(
        DateTime nowUtc, TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        var candidates = await ElapsedCandidateQuery(_context, nowUtc, lookback)
            .ToListAsync(cancellationToken);

        // Mirror of the start pass's residual test: `<=` where that one reads `>`.
        return candidates.Where(a => a.AppointmentDateTime + a.Duration <= nowUtc).ToList();
    }

    public async Task<IReadOnlyList<Appointment>> GetClosureCandidatesAsync(
        Guid clinicId,
        DateTime fromUtc,
        DateTime nowUtc,
        Guid? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            // The acts the séance was booked for — the row names them, and without this the projection would
            // issue one query per appointment to say « Détartrage ».
            .Include(a => a.Procedures)
            .Where(a => a.ClinicId == clinicId
                        // A « créneau occupé » has nothing to close.
                        && a.PatientId != null
                        && a.AppointmentDateTime >= fromUtc
                        // ⚠️ A séance RETIRÉE is a candidate whatever its hour and whatever its statut, and that
                        // is what keeps « Supprimer (créé par erreur) » from being a black hole. The elapsed bound
                        // is right for the worklist — a visit that has not happened owes nothing yet — but
                        // « séances retirées » is the ONLY screen that lists the mark, so under the old predicate a
                        // mis-typed séance NEXT Tuesday left the agenda, left the figures, and appeared nowhere:
                        // unrecoverable, while the dialog that removed it promised otherwise. The two exclusions
                        // below move inside the same branch for the same reason — a row can be annulée and then
                        // retirée, and the second mark must stay visible.
                        //
                        // The OPEN half is untouched by construction: a disregarded row never satisfies
                        // `VisitClosureRules.IsOnWorklist`, so nothing new reaches the worklist or the dashboard
                        // chip — only the « retirées » complement grows, which is exactly the recovery list.
                        && (a.DisregardedAtUtc != null
                            || (a.AppointmentDateTime <= nowUtc
                                // Both are complete answers rather than gaps: a visit that did not happen needs no
                                // fiche and owes no money. Excluded so the in-memory rule never has to un-say them.
                                && a.Status != AppointmentStatus.Cancelled
                                && a.Status != AppointmentStatus.NoShow))
                        && (doctorId == null || a.DoctorId == doctorId))
            // Unique column last — the caller pages this, and OFFSET over a non-unique sort can show a row on two
            // pages and skip another, which on this screen reads as « une séance a disparu ».
            .OrderBy(a => a.AppointmentDateTime)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Appointment>> GetForPatientOnDayAsync(
        Guid patientId,
        DateTime dayStartUtc,
        DateTime dayLastTickUtc,
        CancellationToken cancellationToken = default)
    {
        // Every candidate, never a FirstOrDefault: which one may be linked — and that several means none — is
        // DentalRecordVisitLink's rule, and a second copy of it here would guess where that one refuses.
        return await _context.Appointments
            .Where(a => a.PatientId == patientId
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.NoShow
                        && a.AppointmentDateTime >= dayStartUtc
                        && a.AppointmentDateTime <= dayLastTickUtc)
            .OrderBy(a => a.AppointmentDateTime)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetAppointmentsForDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
            .Where(a => a.AppointmentDateTime >= startOfDay && a.AppointmentDateTime < endOfDay)
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Appointment>> GetByProcedureTypeIdAsync(Guid procedureTypeId, CancellationToken cancellationToken = default)
    {
        return await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.ProcedureType)
            .Include(a => a.Procedures).ThenInclude(p => p.ProcedureType)
            // Matches the whole séance, not just its lead act: a procedure renamed or recoloured must be
            // re-snapshotted on every row that carries it, and a procedure booked as a future visit's *second*
            // act must still block its hard deletion (ProcedureType.IsUsedByFutureAppointments).
            .Where(a => a.ProcedureTypeId == procedureTypeId
                        || a.Procedures.Any(p => p.ProcedureTypeId == procedureTypeId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountByRecurringSeriesAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken cancellationToken = default)
    {
        var ids = seriesIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await _context.Appointments
            .Where(a => a.ClinicId == clinicId
                && a.RecurringAppointmentId.HasValue
                && ids.Contains(a.RecurringAppointmentId.Value))
            .GroupBy(a => a.RecurringAppointmentId!.Value)
            .Select(g => new { SeriesId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SeriesId, x => x.Count, cancellationToken);
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

        // The child rows ARE included here, unlike the other lean reads: a séance that groups several devis acts
        // is one appointment whose *second* and *third* acts live only in `Procedures`, so a projection built from
        // the parent scalar alone would report those acts as « À planifier » forever — offering to book a visit
        // that already exists. Nothing else about the acts is loaded (no ProcedureType), because the projection
        // needs only the link, the date and the status, and this runs for every plan on a list page.
        return await _context.Appointments
            .Include(a => a.Procedures)
            .Where(a => a.ClinicId == clinicId
                        && ((a.TreatmentPlanItemId != null
                             && treatmentPlanItemIds.Contains(a.TreatmentPlanItemId.Value))
                            || a.Procedures.Any(p => p.TreatmentPlanItemId != null
                                                     && treatmentPlanItemIds.Contains(p.TreatmentPlanItemId.Value))))
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
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(appointment);
        if (entry.State == EntityState.Detached)
        {
            _context.Appointments.Update(appointment);
        }
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



