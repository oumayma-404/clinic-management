using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using FicheDay = ClinicManagement.Application.Features.Patients.DentalRecordVisitLink.FicheDay;
using VisitFacts = ClinicManagement.Application.Features.Patients.DentalRecordVisitLink.VisitFacts;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Links a fiche saved with no visit to the visit that has since become its day's only one — inside the save that
/// made it so. Called by <see cref="ApplicationDbContext.SaveChangesAsync(CancellationToken)"/> before anything else
/// reads the change tracker.
///
/// <para><b>The defect.</b> <see cref="DentalRecordVisitLink.ResolveAsync"/> answers once, when the fiche is created.
/// A walk-in is charted first and booked after, so the fiche stayed unlinked for good: « À clôturer » asked for a
/// fiche the patient already had, and <c>verify-schema</c>'s <c>dental-record-visit-links-backfill</c> failed the
/// next deploy.</para>
///
/// <para><b>Why here and not at each writer.</b> The condition is created by at least seven kinds of write across
/// five handlers — a visit booked (one or a series), moved to another day, cancelled or marked absent leaving one
/// twin, reactivated, reassigned by a patient merge — plus a fiche whose date is corrected. Wiring each is this
/// repository's most expensive recurring defect (a rule at one call site out of several). A save sees every one of
/// them by construction, which is <c>AutomaticWriteInterceptor</c>'s argument; a new writer needs no knowledge of
/// this file.</para>
///
/// <para><b>Why the DbContext and not an interceptor.</b> <c>ClinicChangeCapture</c> and the audit chain read the
/// tracker in <c>SaveChangesAsync</c> before the base save runs the interceptors, so a link written by an interceptor
/// would reach neither the PC de secours nor the journal. Written here, first, it travels like any other change:
/// same statement batch, same transaction, same actor in the audit row.</para>
///
/// <para><b>What it never does.</b> It writes the column and nothing else — no status, no post-visit prompt, which
/// is the backfill migration's rule and for its reason. It never re-points a fiche that has a visit
/// (<see cref="DentalRecord.LinkToVisit"/>). And the relay's row store and handback write through raw SQL, never
/// through a save, so a PC de secours mirroring the cloud derives nothing of its own.</para>
///
/// <para><b>Cost.</b> Nothing unless a save books, moves, cancels or reassigns a visit, or saves a fiche still
/// without one — the minutely status pass changes neither, so it costs nothing here. Otherwise one read of the
/// unlinked fiches concerned, and a second of those days' visits only when there are fiches to link.</para>
/// </summary>
public static class DentalRecordVisitLinks
{
    /// <summary>Links what the pending changes made linkable. Returns how many fiches were linked.</summary>
    public static async Task<int> LinkAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var days = DaysToAskAgain(db.ChangeTracker);
        if (days.Count == 0)
        {
            return 0;
        }

        await LoadAsync(db, days, cancellationToken);

        var linked = LinkTracked(db.ChangeTracker, days);
        if (linked > 0)
        {
            // LinkToVisit writes a property, which snapshot tracking only sees on the next detection — and the change
            // capture and the audit collector must see it in this save.
            db.ChangeTracker.DetectChanges();
        }

        return linked;
    }

    /// <summary>
    /// The (clinic, patient, day)s whose answer the pending changes may have changed: every visit added, deleted or
    /// changed in a way <see cref="DentalRecordVisitLink.DaysToAskAgain"/> says matters, and every fiche saved while it
    /// still has no visit. Reads the tracker only — no query.
    /// </summary>
    public static IReadOnlyCollection<FicheDay> DaysToAskAgain(ChangeTracker tracker)
    {
        var days = new HashSet<FicheDay>();

        foreach (var entry in tracker.Entries<Appointment>())
        {
            var asked = entry.State switch
            {
                EntityState.Added => DentalRecordVisitLink.DaysToAskAgain(null, Current(entry.Entity)),
                EntityState.Modified => DentalRecordVisitLink.DaysToAskAgain(Original(entry), Current(entry.Entity)),
                EntityState.Deleted => DentalRecordVisitLink.DaysToAskAgain(Original(entry), null),
                _ => Array.Empty<FicheDay>(),
            };
            days.UnionWith(asked);
        }

        // A fiche saved while it still has no visit asks again, whatever changed on it: its date may have been
        // corrected onto a visit's day, and a visit booked through a path that bypassed this hook is caught up too.
        foreach (var entry in tracker.Entries<DentalRecord>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity.AppointmentId is null)
            {
                days.Add(new FicheDay(
                    entry.Entity.ClinicId,
                    entry.Entity.PatientId,
                    DentalRecordVisitLink.DayOf(entry.Entity.InterventionDate)));
            }
        }

        return days;
    }

    /// <summary>
    /// Links, for each day, its unlinked fiches to its only visit — judged on what the tracker holds, i.e. on the
    /// pending values, never on what the database still says. <see cref="LoadAsync"/> is what puts the stored rows
    /// there first. Returns how many fiches were linked.
    /// </summary>
    public static int LinkTracked(ChangeTracker tracker, IReadOnlyCollection<FicheDay> days)
    {
        // Taken once: each Entries() call runs change detection over the whole tracker.
        var fiches = Live<DentalRecord>(tracker).Where(r => r.AppointmentId is null).ToList();
        var visits = Live<Appointment>(tracker).ToList();

        var linked = 0;
        foreach (var day in days)
        {
            var onDay = fiches.Where(r => IsOn(r, day)).ToList();
            if (onDay.Count == 0)
            {
                continue;
            }

            var only = DentalRecordVisitLink.OnlyCandidate(visits
                .Where(a => a.PatientId == day.PatientId
                            && a.ClinicId == day.ClinicId
                            && DentalRecordVisitLink.CanBeDocumented(a.Status)
                            && DentalRecordVisitLink.DayOf(a.AppointmentDateTime) == day.Day)
                .Select(a => a.Id));

            if (only is not { } visitId)
            {
                continue;
            }

            foreach (var fiche in onDay)
            {
                if (fiche.LinkToVisit(visitId))
                {
                    linked++;
                }
            }
        }

        return linked;
    }

    /// <summary>
    /// Brings the stored rows the decision needs into the tracker: the days' unlinked fiches, then — only for the
    /// days that have one — their candidate visits. Rows already tracked come back as themselves, carrying their
    /// pending values, which is what <see cref="LinkTracked"/> judges on.
    /// </summary>
    private static async Task LoadAsync(
        ApplicationDbContext db, IReadOnlyCollection<FicheDay> days, CancellationToken cancellationToken)
    {
        var (from, to) = Span(days);
        await UnlinkedFichesQuery(db, days.Select(d => d.PatientId).Distinct().ToList(), from, to)
            .LoadAsync(cancellationToken);

        var fiches = Live<DentalRecord>(db.ChangeTracker).Where(r => r.AppointmentId is null).ToList();
        var withFiches = days.Where(d => fiches.Any(r => IsOn(r, d))).ToList();
        if (withFiches.Count == 0)
        {
            return;
        }

        var (visitsFrom, visitsTo) = Span(withFiches);
        await CandidateVisitsQuery(db, withFiches.Select(d => d.PatientId).Distinct().ToList(), visitsFrom, visitsTo)
            .LoadAsync(cancellationToken);
    }

    /// <summary>
    /// The stored fiches without a visit, for these patients, between two instants — tracked, because they are the
    /// rows this save may link. Public so a test can compile it to SQL (<c>ToQueryString()</c>) without a database.
    /// </summary>
    public static IQueryable<DentalRecord> UnlinkedFichesQuery(
        ApplicationDbContext db, IReadOnlyCollection<Guid> patientIds, DateTime fromUtc, DateTime toUtc) =>
        db.DentalRecords.AsTracking()
            .Where(r => r.AppointmentId == null
                        && patientIds.Contains(r.PatientId)
                        && r.InterventionDate >= fromUtc
                        && r.InterventionDate <= toUtc);

    /// <summary>
    /// The stored visits a fiche could document, for these patients, between two instants. Filtered on the
    /// <b>stored</b> status: a visit whose pending status makes it a candidate is already in the tracker.
    /// </summary>
    public static IQueryable<Appointment> CandidateVisitsQuery(
        ApplicationDbContext db, IReadOnlyCollection<Guid> patientIds, DateTime fromUtc, DateTime toUtc) =>
        db.Appointments.AsTracking()
            .Where(a => a.PatientId != null
                        && patientIds.Contains(a.PatientId.Value)
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.NoShow
                        && a.AppointmentDateTime >= fromUtc
                        && a.AppointmentDateTime <= toUtc);

    private static bool IsOn(DentalRecord fiche, FicheDay day) =>
        fiche.PatientId == day.PatientId
        && fiche.ClinicId == day.ClinicId
        && DentalRecordVisitLink.DayOf(fiche.InterventionDate) == day.Day;

    private static IEnumerable<T> Live<T>(ChangeTracker tracker) where T : class =>
        tracker.Entries<T>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .Select(e => e.Entity);

    /// <summary>The UTC instants from the first day's start to the last day's last tick, in the clinic's own days.</summary>
    private static (DateTime From, DateTime To) Span(IEnumerable<FicheDay> days)
    {
        var ordered = days.Select(d => d.Day).ToList();
        var first = ClinicClock.LocalDayRangeUtc(ordered.Min().ToDateTime(TimeOnly.MinValue));
        var last = ClinicClock.LocalDayRangeUtc(ordered.Max().ToDateTime(TimeOnly.MinValue));
        return (first.From, last.ToInclusive);
    }

    private static VisitFacts Current(Appointment appointment) =>
        new(appointment.ClinicId, appointment.PatientId, appointment.AppointmentDateTime, appointment.Status);

    private static VisitFacts Original(EntityEntry<Appointment> entry) =>
        new(
            entry.Property(a => a.ClinicId).OriginalValue,
            entry.Property(a => a.PatientId).OriginalValue,
            entry.Property(a => a.AppointmentDateTime).OriginalValue,
            entry.Property(a => a.Status).OriginalValue);
}
