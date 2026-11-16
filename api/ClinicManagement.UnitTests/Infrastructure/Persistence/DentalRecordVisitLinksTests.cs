using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Migrations;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// A fiche saved before its visit is linked by the save that books the visit — and by every other save that makes
/// a visit its day's only one.
///
/// <para><b>The defect.</b> Production, November 2026: a walk-in's fiche saved at 12:08, the visit booked at 12:31
/// for that morning. Nothing asked « quelle séance cette fiche documente-t-elle ? » again, so « À clôturer » asked for
/// a fiche the patient already had and <c>verify-schema</c> failed the deploy.</para>
///
/// <para>Touches no database. The tracker-level cases attach entities to stand for stored rows and judge on what the
/// save holds, which is exactly what <see cref="DentalRecordVisitLinks.LinkTracked"/> does after its two reads; the
/// reads themselves are compiled to SQL with <c>ToQueryString()</c>. Any attempt to connect throws.</para>
/// </summary>
public class DentalRecordVisitLinksTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherPatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>A date-only fiche on 10 November 2026: stored at midnight UTC, i.e. 01:00 in Tunis.</summary>
    private static readonly DateTime FicheDate = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>11:00 in Tunis on the fiche's day (Tunisia is UTC+1, no DST).</summary>
    private static readonly DateTime Morning = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>15:00 in Tunis on the fiche's day.</summary>
    private static readonly DateTime Afternoon = new(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>11:00 in Tunis the next day.</summary>
    private static readonly DateTime NextMorning = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    private sealed class DatabaseTouchedException : Exception;

    private sealed class RefuseToConnect : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new DatabaseTouchedException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new DatabaseTouchedException();
    }

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=never_connected;Username=none;Password=none")
            .AddInterceptors(new RefuseToConnect())
            .Options);

    /// <summary>A fiche as the database holds it: tracked, unchanged.</summary>
    private static DentalRecord StoredFiche(
        ApplicationDbContext db, DateTime? at = null, Guid? patientId = null, Guid? appointmentId = null)
    {
        var fiche = new DentalRecord(
            Guid.NewGuid(), patientId ?? PatientId, ClinicId, at ?? FicheDate, 0m, isAdultTeeth: true,
            appointmentId: appointmentId);
        db.DentalRecords.Attach(fiche);
        return fiche;
    }

    private static Appointment Visit(DateTime at, Guid? patientId = null, Guid? clinicId = null) =>
        new(Guid.NewGuid(), clinicId ?? ClinicId, patientId ?? PatientId, doctorId: null, at, TimeSpan.FromMinutes(30));

    /// <summary>A visit as the database holds it: tracked, unchanged.</summary>
    private static Appointment StoredVisit(ApplicationDbContext db, DateTime at, Guid? patientId = null, Guid? clinicId = null)
    {
        var visit = Visit(at, patientId, clinicId);
        db.Appointments.Attach(visit);
        return visit;
    }

    /// <summary>A visit this save books.</summary>
    private static Appointment BookedVisit(ApplicationDbContext db, DateTime at, Guid? patientId = null, Guid? clinicId = null)
    {
        var visit = Visit(at, patientId, clinicId);
        db.Appointments.Add(visit);
        return visit;
    }

    /// <summary>The production decision over what the save holds — <see cref="DentalRecordVisitLinks.LinkAsync"/>
    /// minus its two reads.</summary>
    private static int Link(ApplicationDbContext db) =>
        DentalRecordVisitLinks.LinkTracked(db.ChangeTracker, DentalRecordVisitLinks.DaysToAskAgain(db.ChangeTracker));

    // ── The case that shipped broken ──────────────────────────────────────────────────────────────────────────

    // The walk-in: the fiche exists, the desk books the visit afterwards. The booking's own save links it.
    [Fact]
    public void Booking_The_Only_Visit_Of_A_Fiches_Day_Links_The_Fiche()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var visit = BookedVisit(db, Morning);

        Assert.Equal(1, Link(db));
        Assert.Equal(visit.Id, fiche.AppointmentId);
    }

    [Fact]
    public void Two_Fiches_Of_That_Day_Are_Both_Linked()
    {
        using var db = Context();
        var first = StoredFiche(db);
        var second = StoredFiche(db, FicheDate.AddHours(5));
        var visit = BookedVisit(db, Morning);

        Assert.Equal(2, Link(db));
        Assert.Equal(visit.Id, first.AppointmentId);
        Assert.Equal(visit.Id, second.AppointmentId);
    }

    // ── Ambiguity is never resolved by guessing ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_Second_Visit_That_Day_Links_Nothing()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        StoredVisit(db, Morning);
        BookedVisit(db, Afternoon);

        Assert.Equal(0, Link(db));
        Assert.Null(fiche.AppointmentId);
    }

    // The booking is on another day: that day's question is asked, and this fiche's is not changed.
    [Fact]
    public void A_Visit_Booked_On_Another_Day_Leaves_The_Fiche_Alone()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        BookedVisit(db, NextMorning);

        Assert.Equal(0, Link(db));
        Assert.Null(fiche.AppointmentId);
    }

    [Fact]
    public void Another_Patients_Visit_Does_Not_Count()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        BookedVisit(db, Morning, patientId: OtherPatientId);

        Assert.Equal(0, Link(db));
        Assert.Null(fiche.AppointmentId);
    }

    // Defence in depth for a system-wide save: a patient id reached across clinics cannot pull in another
    // practice's visit.
    [Fact]
    public void Another_Clinics_Visit_Does_Not_Count()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        BookedVisit(db, Morning, clinicId: OtherClinicId);

        Assert.Equal(0, Link(db));
        Assert.Null(fiche.AppointmentId);
    }

    // A link already made — by a deep link, or because the day had one visit when the fiche was saved — is never
    // re-pointed by a later booking.
    [Fact]
    public void A_Fiche_That_Has_A_Visit_Is_Never_Repointed()
    {
        using var db = Context();
        var earlier = Guid.NewGuid();
        var fiche = StoredFiche(db, appointmentId: earlier);
        BookedVisit(db, Morning);

        Assert.Equal(0, Link(db));
        Assert.Equal(earlier, fiche.AppointmentId);
    }

    // ── The other writes that can make a visit its day's only one ─────────────────────────────────────────────

    // Two visits made the fiche ambiguous; cancelling one leaves exactly one.
    [Fact]
    public void Cancelling_One_Of_Two_Visits_Links_The_Other()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var kept = StoredVisit(db, Morning);
        var duplicate = StoredVisit(db, Afternoon);

        duplicate.Cancel("doublon");

        Assert.Equal(1, Link(db));
        Assert.Equal(kept.Id, fiche.AppointmentId);
    }

    [Fact]
    public void Marking_One_Of_Two_Visits_Absent_Links_The_Other()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var kept = StoredVisit(db, Morning);
        var missed = StoredVisit(db, Afternoon);

        missed.MarkAsNoShow();

        Assert.Equal(1, Link(db));
        Assert.Equal(kept.Id, fiche.AppointmentId);
    }

    // The visit was booked on the wrong day; correcting its date brings it onto the fiche's.
    [Fact]
    public void Moving_A_Visit_Onto_The_Fiches_Day_Links_It()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var visit = StoredVisit(db, NextMorning);

        visit.Reschedule(Morning, sameClinicDay: false);

        Assert.Equal(1, Link(db));
        Assert.Equal(visit.Id, fiche.AppointmentId);
    }

    // The day the visit LEFT is asked too: with it gone, the remaining one is the day's only visit.
    [Fact]
    public void Moving_One_Of_Two_Visits_Away_Links_The_One_That_Stays()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var stays = StoredVisit(db, Morning);
        var moves = StoredVisit(db, Afternoon);

        moves.Reschedule(NextMorning, sameClinicDay: false);

        Assert.Equal(1, Link(db));
        Assert.Equal(stays.Id, fiche.AppointmentId);
    }

    [Fact]
    public void Reactivating_A_Cancelled_Visit_Links_It()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var visit = Visit(Morning);
        visit.Cancel();
        db.Appointments.Attach(visit);

        visit.Reactivate(Morning);

        Assert.Equal(1, Link(db));
        Assert.Equal(visit.Id, fiche.AppointmentId);
    }

    // The calendar-import merge: the duplicate patient's visit moves onto the patient whose fiche it documents.
    [Fact]
    public void Reassigning_A_Visit_To_The_Fiches_Patient_Links_It()
    {
        using var db = Context();
        var fiche = StoredFiche(db);
        var visit = StoredVisit(db, Morning, patientId: OtherPatientId);

        visit.ReassignToPatient(PatientId);

        Assert.Equal(1, Link(db));
        Assert.Equal(visit.Id, fiche.AppointmentId);
    }

    // The fiche's own save asks again: its date was corrected onto the day of an existing visit.
    [Fact]
    public void Correcting_A_Fiches_Date_Onto_A_Visits_Day_Links_It_In_The_Same_Save()
    {
        using var db = Context();
        var visit = StoredVisit(db, Morning);
        var fiche = StoredFiche(db, at: NextMorning);

        fiche.Update(FicheDate, fiche.AmountPaid, null, null);

        Assert.Equal(1, Link(db));
        Assert.Equal(visit.Id, fiche.AppointmentId);
    }

    // ── Tunisia is UTC+1 ──────────────────────────────────────────────────────────────────────────────────────

    // 23:30 in Tunis is 22:30 UTC; 00:30 the next Tunisian day is 23:30 UTC on the SAME UTC date. Comparing raw
    // UTC dates would link them; the clinic's own day does not.
    [Fact]
    public void The_Day_Is_The_Clinics_Own_Not_The_UTC_Date()
    {
        using var db = Context();
        var lateEvening = new DateTime(2026, 11, 10, 22, 30, 0, DateTimeKind.Utc);
        var pastMidnight = new DateTime(2026, 11, 10, 23, 30, 0, DateTimeKind.Utc);
        var fiche = StoredFiche(db, at: lateEvening);
        BookedVisit(db, pastMidnight);

        Assert.Equal(0, Link(db));
        Assert.Null(fiche.AppointmentId);
    }

    // ── What asks the question, and what costs nothing ────────────────────────────────────────────────────────

    // AppointmentProgressJob writes every open visit every minute — « En cours », then « Séance passée ». Neither
    // changes which visit a fiche can document, so the hook must not even read.
    [Fact]
    public async Task The_Minutely_Status_Pass_Asks_Nothing_And_Reads_Nothing()
    {
        await using var db = Context();
        var visit = StoredVisit(db, Morning);

        visit.Start();
        visit.MarkAwaitingClosure();

        Assert.Empty(DentalRecordVisitLinks.DaysToAskAgain(db.ChangeTracker));
        Assert.Equal(0, await DentalRecordVisitLinks.LinkAsync(db));
    }

    [Fact]
    public async Task A_Save_That_Touches_No_Visit_And_No_Unlinked_Fiche_Reads_Nothing()
    {
        await using var db = Context();
        StoredVisit(db, Morning);
        var linked = StoredFiche(db, appointmentId: Guid.NewGuid());
        StoredFiche(db);

        linked.Update(FicheDate, 10m, null, null);

        Assert.Equal(0, await DentalRecordVisitLinks.LinkAsync(db));
    }

    // …and a booking does read: the stored fiches of that day are what it may link.
    [Fact]
    public async Task A_Booking_Reads_The_Days_Stored_Fiches()
    {
        await using var db = Context();
        BookedVisit(db, Morning);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => DentalRecordVisitLinks.LinkAsync(db));

        Assert.True(Reached<DatabaseTouchedException>(thrown), $"Expected a database read, got {thrown.GetType().Name}");
    }

    [Fact]
    public void A_Blocked_Slot_Asks_Nothing()
    {
        using var db = Context();
        db.Appointments.Add(new Appointment(
            Guid.NewGuid(), ClinicId, patientId: null, doctorId: null, Morning, TimeSpan.FromHours(2)));

        Assert.Empty(DentalRecordVisitLinks.DaysToAskAgain(db.ChangeTracker));
    }

    // ── The reads are SQL, not memory ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_Unlinked_Fiches_Read_Is_Pushed_Down()
    {
        using var db = Context();

        var sql = DentalRecordVisitLinks
            .UnlinkedFichesQuery(db, new[] { PatientId }, Morning, Afternoon)
            .ToQueryString();

        Assert.Contains("\"AppointmentId\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"PatientId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"InterventionDate\" >=", sql, StringComparison.Ordinal);
        Assert.Contains("\"InterventionDate\" <=", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Candidate_Visits_Read_Is_Pushed_Down()
    {
        using var db = Context();

        var sql = DentalRecordVisitLinks
            .CandidateVisitsQuery(db, new[] { PatientId }, Morning, Afternoon)
            .ToQueryString();

        Assert.Contains("\"Status\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"PatientId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"AppointmentDateTime\" >=", sql, StringComparison.Ordinal);
        Assert.Contains("\"AppointmentDateTime\" <=", sql, StringComparison.Ordinal);
    }

    // ── Where the hook sits ───────────────────────────────────────────────────────────────────────────────────

    // A link written after the change capture or the audit collector read the tracker would reach neither the PC de
    // secours nor the journal — and nothing else in the build can see that: the hook is correct wherever it is.
    [Fact]
    public void The_Save_Links_Before_Anything_Else_Reads_The_Tracker()
    {
        var source = Read("ClinicManagement.Infrastructure", "Persistence", "ApplicationDbContext.cs");
        var start = source.IndexOf("public override async Task<int> SaveChangesAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "SaveChangesAsync not found in ApplicationDbContext.cs");
        var body = source[start..];

        var link = body.IndexOf("await LinkWaitingFichesAsync(cancellationToken);", StringComparison.Ordinal);
        Assert.True(link > 0, "SaveChangesAsync no longer calls LinkWaitingFichesAsync");

        foreach (var reader in new[]
                 {
                     "NumberedDocuments.MakeProvisional(this)",
                     "NumberedDocuments.Collect(this)",
                     "PendingAuditRows()",
                     "HasCandidates(this)",
                     "base.SaveChangesAsync(cancellationToken)",
                 })
        {
            var at = body.IndexOf(reader, StringComparison.Ordinal);
            Assert.True(at > link, $"« {reader} » runs before the fiche links are made");
        }
    }

    // ── The one-time pass for the rows already stored ─────────────────────────────────────────────────────────

    [Fact]
    public void The_Relink_Migration_Runs_The_Original_Backfills_Statement()
    {
        Assert.Equal(SqlOf(new BackfillDentalRecordAppointmentLinks()), SqlOf(new RelinkOrphanDentalRecordVisits()));
    }

    // A migration EF cannot see is a folder entry that never runs: its id lives in the Designer's attribute.
    [Fact]
    public void The_Relink_Migration_Is_Discoverable_By_EF()
    {
        var type = typeof(RelinkOrphanDentalRecordVisits);

        Assert.Equal(
            "20261116150000_RelinkOrphanDentalRecordVisits",
            type.GetCustomAttribute<MigrationAttribute>()?.Id);
        Assert.Equal(typeof(ApplicationDbContext), type.GetCustomAttribute<DbContextAttribute>()?.ContextType);
    }

    private static string SqlOf(Migration migration) =>
        Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;

    private static bool Reached<T>(Exception thrown) where T : Exception
    {
        for (Exception? ex = thrown; ex is not null; ex = ex.InnerException)
        {
            if (ex is T)
            {
                return true;
            }
        }

        return false;
    }

    private static string Read(params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { SolutionRoot() }.Concat(relativeParts).ToArray()));

    /// <remarks><c>[CallerFilePath]</c>, not <c>AppContext.BaseDirectory</c>: the suite is routinely built to a scratch
    /// <c>BaseOutputPath</c> outside the repository. Throws rather than skipping — see
    /// <c>StartupBackfillCoverageTests</c>.</remarks>
    private static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicManagement.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException($"ClinicManagement.sln not found above '{thisFile}'.");
    }
}
