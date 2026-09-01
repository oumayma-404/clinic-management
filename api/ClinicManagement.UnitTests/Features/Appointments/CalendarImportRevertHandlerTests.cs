using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « Annuler cet import » — and above all, <b>what it must never delete</b>.
///
/// <para>The undo removes patient records, so the question that decides whether it can ship is not « does it
/// work » but « can it reach the practice's own work ». Every test below is a variation on that: a visit the
/// dentist charted, a fiche, a note d'honoraires, a booking made after the import, another cabinet's run. Each
/// asserts against the ids actually handed to <c>DeleteRunRowsAsync</c> — the one call that destroys anything —
/// rather than against the summary the handler returns, because a correct-looking report over a wrong delete is
/// exactly the failure that would not be noticed.</para>
/// </summary>
public class CalendarImportRevertHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string UserId = "local|11111111-1111-1111-1111-111111111111";

    private readonly Mock<ICalendarImportRunRepository> _runs = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IRealtimeNotifier> _realtime = new();
    private readonly Mock<IClinicRecoveryPointService> _recoveryPoints = new();

    /// <summary>What the destructive call was actually given. Null until it is made.</summary>
    private IReadOnlyCollection<Guid>? _deletedAppointments;
    private IReadOnlyCollection<Guid>? _deletedPatients;

    private static CalendarImportRun RunIn(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, UserId, new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 29, 0, 0, 0, DateTimeKind.Utc));

    private static CalendarImportRunVisit Visit(
        Guid? patientId = null,
        bool hasFiche = false,
        bool hasLiveInvoice = false,
        bool coveredByPlan = false,
        bool hasLabOrder = false,
        bool hasProcedures = false,
        bool nothingToBill = false) =>
        new(
            AppointmentId: Guid.NewGuid(),
            PatientId: patientId ?? Guid.NewGuid(),
            PatientName: "Ahmed Ben Ali",
            AppointmentDateTime: new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
            HasFiche: hasFiche,
            HasLiveInvoice: hasLiveInvoice,
            CoveredByPlan: coveredByPlan,
            HasLabOrder: hasLabOrder,
            HasProcedures: hasProcedures,
            NothingToBill: nothingToBill,
            Disregarded: false);

    /// <summary>A placeholder holding nothing but the appointments this run created.</summary>
    private static PatientLinkedDataCounts UntouchedPatient(int appointments) =>
        new(appointments, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Notifications: 1);

    private RevertCalendarImportRunCommandHandler Handler(CalendarImportRun run, CalendarImportRunContents contents)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinicContext.Setup(c => c.GetUserId()).Returns(UserId);

        _runs.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _runs.Setup(r => r.GetContentsAsync(ClinicId, run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contents);

        _runs.Setup(r => r.DeleteRunRowsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, IReadOnlyCollection<Guid>, CancellationToken>(
                (_, appointments, patients, _) =>
                {
                    _deletedAppointments = appointments;
                    _deletedPatients = patients;
                })
            .Returns(Task.CompletedTask);

        // The net succeeds unless a test says otherwise — the ordinary case, and what every other assertion
        // in this file is about.
        _recoveryPoints.Setup(r => r.TryTakeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new RevertCalendarImportRunCommandHandler(
            _runs.Object, _patients.Object, _recoveryPoints.Object, _unitOfWork.Object, _clinicResolver.Object,
            _clinicContext.Object, _realtime.Object,
            NullLogger<RevertCalendarImportRunCommandHandler>.Instance);
    }

    private void PatientHolds(Guid patientId, PatientLinkedDataCounts counts) =>
        _patients.Setup(r => r.GetLinkedDataCountsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(counts);

    // ── what it may delete ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_Untouched_Import_Is_Deleted_Whole()
    {
        var patientId = Guid.NewGuid();
        var visit = Visit(patientId);
        var run = RunIn(ClinicId);

        PatientHolds(patientId, UntouchedPatient(appointments: 1));

        var result = await Handler(run, new CalendarImportRunContents(
            new[] { visit }, new[] { new CalendarImportRunPatient(patientId, "Ahmed Ben Ali") }))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { visit.AppointmentId }, _deletedAppointments);
        Assert.Equal(new[] { patientId }, _deletedPatients);
        Assert.Empty(result.Value!.Kept);
    }

    // ── what it must NOT delete ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The headline guarantee: the dentist charted a fiche against an imported slot, so the visit is now a
    /// clinical record and the undo must leave it — and say why.
    /// </summary>
    [Fact]
    public async Task A_Visit_The_Dentist_Charted_A_Fiche_On_Is_Never_Deleted()
    {
        var patientId = Guid.NewGuid();
        var charted = Visit(patientId, hasFiche: true);
        var run = RunIn(ClinicId);

        // The patient now holds that fiche, so they are not deletable either.
        PatientHolds(patientId, UntouchedPatient(appointments: 1) with { DentalRecords = 1 });

        var result = await Handler(run, new CalendarImportRunContents(
            new[] { charted }, new[] { new CalendarImportRunPatient(patientId, "Ahmed Ben Ali") }))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_deletedAppointments!);
        Assert.Empty(_deletedPatients!);
        Assert.Equal(2, result.Value!.Kept.Count); // the visit, and the patient
        Assert.Contains(result.Value.Kept, k => k.Reason.Contains("fiche de soins"));
    }

    [Theory]
    [InlineData(true, false, false, false, false)]  // a note d'honoraires
    [InlineData(false, true, false, false, false)]  // a devis step
    [InlineData(false, false, true, false, false)]  // a bon de prothèse
    [InlineData(false, false, false, true, false)]  // acts typed on the séance
    [InlineData(false, false, false, false, true)]  // a recorded « rien à facturer »
    public async Task Any_Work_Recorded_Against_A_Visit_Keeps_It(
        bool invoice, bool plan, bool lab, bool acts, bool nothingToBill)
    {
        var patientId = Guid.NewGuid();
        var visit = Visit(patientId, hasLiveInvoice: invoice, coveredByPlan: plan, hasLabOrder: lab,
            hasProcedures: acts, nothingToBill: nothingToBill);
        var run = RunIn(ClinicId);

        PatientHolds(patientId, UntouchedPatient(appointments: 1));

        var result = await Handler(run, new CalendarImportRunContents(
            new[] { visit }, Array.Empty<CalendarImportRunPatient>()))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_deletedAppointments!);
        Assert.Single(result.Value!.Kept);
    }

    /// <summary>
    /// The case a column comparison alone would miss: the placeholder is untouched, but the practice has since
    /// booked this person a <b>real</b> visit. Deleting the patient would take that booking with it.
    /// </summary>
    [Fact]
    public async Task A_Placeholder_The_Practice_Has_Since_Booked_Again_Is_Kept()
    {
        var patientId = Guid.NewGuid();
        var imported = Visit(patientId);
        var run = RunIn(ClinicId);

        // Two appointments on file; only one of them is this run's.
        PatientHolds(patientId, UntouchedPatient(appointments: 2));

        var result = await Handler(run, new CalendarImportRunContents(
            new[] { imported }, new[] { new CalendarImportRunPatient(patientId, "Ahmed Ben Ali") }))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The imported slot still goes — nothing is recorded against it — but the person stays.
        Assert.Equal(new[] { imported.AppointmentId }, _deletedAppointments);
        Assert.Empty(_deletedPatients!);
        Assert.Contains(result.Value!.Kept, k => k.Id == patientId);
    }

    /// <summary>Anything else on the fiche — a document, a file, an antécédent — keeps the patient too.</summary>
    [Fact]
    public async Task A_Placeholder_Carrying_Any_Other_Record_Is_Kept()
    {
        var patientId = Guid.NewGuid();
        var run = RunIn(ClinicId);

        PatientHolds(patientId, UntouchedPatient(appointments: 1) with { MedicalHistoryEntries = 1 });

        await Handler(run, new CalendarImportRunContents(
            Array.Empty<CalendarImportRunVisit>(),
            new[] { new CalendarImportRunPatient(patientId, "Ahmed Ben Ali") }))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.Empty(_deletedPatients!);
    }

    // ── tenancy and idempotence ─────────────────────────────────────────────────────────────────────────

    /// <summary>Another practice's run is unreachable, and refused the same way an unknown id is.</summary>
    [Fact]
    public async Task Another_Clinics_Run_Is_Refused_And_Deletes_Nothing()
    {
        var run = RunIn(OtherClinicId);

        var result = await Handler(run, new CalendarImportRunContents(
            Array.Empty<CalendarImportRunVisit>(), Array.Empty<CalendarImportRunPatient>()))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(_deletedAppointments);
        _runs.Verify(r => r.DeleteRunRowsAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Second_Revert_Is_Refused_With_A_Code_And_Deletes_Nothing()
    {
        var run = RunIn(ClinicId);
        run.MarkReverted(DateTime.UtcNow, UserId, 3, 2, 0);

        var result = await Handler(run, new CalendarImportRunContents(
            Array.Empty<CalendarImportRunVisit>(), Array.Empty<CalendarImportRunPatient>()))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(RevertCalendarImportRunCommandHandler.AlreadyRevertedCode, result.Code);
        Assert.Null(_deletedAppointments);
    }

    /// <summary>
    /// ⚠️ <b>No net, no delete.</b> A self-serve bulk delete of patient records with nobody holding a backup, so
    /// a recovery point that could not be taken is a <b>refusal</b> rather than a warning — and it happens before
    /// anything is staged.
    /// </summary>
    [Fact]
    public async Task Without_A_Recovery_Point_Nothing_Is_Deleted()
    {
        var patientId = Guid.NewGuid();
        var visit = Visit(patientId);
        var run = RunIn(ClinicId);

        PatientHolds(patientId, UntouchedPatient(appointments: 1));

        var handler = Handler(run, new CalendarImportRunContents(
            new[] { visit }, new[] { new CalendarImportRunPatient(patientId, "Ahmed Ben Ali") }));

        _recoveryPoints.Setup(r => r.TryTakeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await handler.Handle(
            new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(RevertCalendarImportRunCommandHandler.NoRecoveryPointCode, result.Code);
        Assert.Null(_deletedAppointments);
        _runs.Verify(r => r.DeleteRunRowsAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A revert with nothing to delete spends no recovery point: a run whose rows have all been kept costs the
    /// practice no archive, and seven days of retention are not spent on a no-op.
    /// </summary>
    [Fact]
    public async Task A_Revert_That_Deletes_Nothing_Takes_No_Recovery_Point()
    {
        var patientId = Guid.NewGuid();
        var charted = Visit(patientId, hasFiche: true);
        var run = RunIn(ClinicId);

        PatientHolds(patientId, UntouchedPatient(appointments: 1) with { DentalRecords = 1 });

        var result = await Handler(run, new CalendarImportRunContents(
            new[] { charted }, new[] { new CalendarImportRunPatient(patientId, "Ahmed Ben Ali") }))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _recoveryPoints.Verify(
            r => r.TryTakeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Mixed, which is the shape of a real cabinet: three imported slots, one of them charted. Exactly the two
    /// untouched ones go.
    /// </summary>
    [Fact]
    public async Task A_Mixed_Run_Deletes_Only_The_Untouched_Rows()
    {
        var run = RunIn(ClinicId);
        var keptPatient = Guid.NewGuid();
        var cleanPatientA = Guid.NewGuid();
        var cleanPatientB = Guid.NewGuid();

        var charted = Visit(keptPatient, hasFiche: true);
        var cleanA = Visit(cleanPatientA);
        var cleanB = Visit(cleanPatientB);

        PatientHolds(keptPatient, UntouchedPatient(appointments: 1) with { DentalRecords = 1 });
        PatientHolds(cleanPatientA, UntouchedPatient(appointments: 1));
        PatientHolds(cleanPatientB, UntouchedPatient(appointments: 1));

        var result = await Handler(run, new CalendarImportRunContents(
            new[] { charted, cleanA, cleanB },
            new[]
            {
                new CalendarImportRunPatient(keptPatient, "Chartée"),
                new CalendarImportRunPatient(cleanPatientA, "Propre A"),
                new CalendarImportRunPatient(cleanPatientB, "Propre B"),
            }))
            .Handle(new RevertCalendarImportRunCommand { RunId = run.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _deletedAppointments!.Count);
        Assert.DoesNotContain(charted.AppointmentId, _deletedAppointments);
        Assert.Equal(2, _deletedPatients!.Count);
        Assert.DoesNotContain(keptPatient, _deletedPatients);

        Assert.Equal(2, result.Value!.AppointmentsDeleted);
        Assert.Equal(2, result.Value.PatientsDeleted);
    }
}
