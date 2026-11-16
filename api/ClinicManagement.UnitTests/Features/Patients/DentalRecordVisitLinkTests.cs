using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « Quelle séance cette fiche documente-t-elle ? » — the resolver that closes the defect « À clôturer » would
/// otherwise be built on top of.
///
/// <para><b>What was wrong.</b> <c>DentalRecord.AppointmentId</c> was populated by exactly one door — the
/// post-visit prompt's deep link — so a fiche charted the ordinary way from the patient's page stored
/// <c>null</c>. A worklist reading that absence as « pas de fiche » would report a missing fiche for the majority
/// of visits that have one, on its very first screen.</para>
///
/// <para><b>The asymmetry these cases pin.</b> Exactly one candidate links; zero or several leave it null. A
/// missing link costs one row on a worklist; a wrong link attaches a séance to another visit <i>and completes
/// it</i> — a claim about a patient's day that nobody made.</para>
/// </summary>
public class DentalRecordVisitLinkTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>Mid-morning Tunis on 14 August 2026, expressed in UTC (Tunisia is UTC+1, no DST).</summary>
    private static readonly DateTime InterventionDate = new(2026, 8, 14, 8, 30, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();

    private static Appointment AppointmentAt(DateTime whenUtc, Guid? clinicId = null) =>
        new(Guid.NewGuid(), clinicId ?? ClinicId, PatientId, doctorId: null, whenUtc, TimeSpan.FromMinutes(30));

    private void Candidates(params Appointment[] found) =>
        _appointments
            .Setup(r => r.GetForPatientOnDayAsync(
                PatientId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(found);

    private Task<Guid?> Resolve(Guid? supplied = null) =>
        DentalRecordVisitLink.ResolveAsync(
            supplied, PatientId, ClinicId, InterventionDate, _appointments.Object);

    // The deep link knows more than we can infer, so it is never second-guessed — and the repository is not even
    // consulted, which is what keeps the ordinary post-visit path at zero extra queries.
    [Fact]
    public async Task A_Supplied_Id_Wins_And_Costs_No_Read()
    {
        var supplied = Guid.NewGuid();

        Assert.Equal(supplied, await Resolve(supplied));

        _appointments.Verify(
            r => r.GetForPatientOnDayAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Guid.Empty is what a client sends when it means « none » — treating it as a supplied id would store a link
    // to an appointment that cannot exist.
    [Fact]
    public async Task An_Empty_Supplied_Id_Falls_Through_To_The_Inference()
    {
        var only = AppointmentAt(InterventionDate);
        Candidates(only);

        Assert.Equal(only.Id, await Resolve(Guid.Empty));
    }

    // The case the whole fix exists for: a fiche charted from the patient's page, on a day with one visit.
    [Fact]
    public async Task Exactly_One_Candidate_Is_Linked()
    {
        var only = AppointmentAt(InterventionDate);
        Candidates(only);

        Assert.Equal(only.Id, await Resolve());
    }

    [Fact]
    public async Task No_Candidate_Leaves_The_Link_Null()
    {
        Candidates();

        Assert.Null(await Resolve());
    }

    // Two visits in a day is an ordinary Tunisian morning — a control at 9h, an extraction at 14h. Picking either
    // is a coin toss the user never sees, and the loser is completed by mistake.
    [Fact]
    public async Task Two_Candidates_Leave_The_Link_Null_Rather_Than_Guessing()
    {
        Candidates(AppointmentAt(InterventionDate), AppointmentAt(InterventionDate.AddHours(5)));

        Assert.Null(await Resolve());
    }

    // Defence in depth: this read is keyed on a PATIENT rather than a clinic, so the caller re-checks the clinic
    // the way every other read in the layer re-checks the aggregate it loaded.
    [Fact]
    public async Task A_Candidate_From_Another_Clinic_Is_Not_Linked()
    {
        Candidates(AppointmentAt(InterventionDate, OtherClinicId));

        Assert.Null(await Resolve());
    }

    // One foreign row must not turn a resolvable day into an ambiguous one either: after filtering there is
    // exactly one candidate, and the fiche links to it.
    [Fact]
    public async Task A_Foreign_Candidate_Does_Not_Make_Its_Own_Clinics_Single_Visit_Ambiguous()
    {
        var mine = AppointmentAt(InterventionDate);
        Candidates(mine, AppointmentAt(InterventionDate.AddHours(2), OtherClinicId));

        Assert.Equal(mine.Id, await Resolve());
    }

    // ⚠️ Tunisia is UTC+1, so the window has to be the CLINIC's day. A fiche recorded at 23:30 Tunis is
    // 22:30 UTC on the same date here, but the bounds must still cover the Tunisian day it belongs to — handing
    // the raw instant to LocalDayRangeUtc would shift the window and find nothing, silently, and only ever for
    // the last hour of the evening.
    [Fact]
    public async Task The_Window_Is_The_Clinics_Own_Day()
    {
        DateTime? from = null;
        DateTime? to = null;
        _appointments
            .Setup(r => r.GetForPatientOnDayAsync(
                PatientId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, DateTime, CancellationToken>((_, f, t, _) => { from = f; to = t; })
            .ReturnsAsync(Array.Empty<Appointment>());

        // 23:30 Tunis on 14 August = 22:30 UTC.
        await DentalRecordVisitLink.ResolveAsync(
            null, PatientId, ClinicId,
            new DateTime(2026, 8, 14, 22, 30, 0, DateTimeKind.Utc),
            _appointments.Object);

        // The Tunisian day 14 Aug runs 13 Aug 23:00 UTC → 14 Aug 22:59:59.999… UTC.
        Assert.Equal(new DateTime(2026, 8, 13, 23, 0, 0, DateTimeKind.Utc), from);
        Assert.True(to > new DateTime(2026, 8, 14, 22, 30, 0, DateTimeKind.Utc));
        Assert.True(to < new DateTime(2026, 8, 14, 23, 0, 0, DateTimeKind.Utc));
    }

    // ── Asking again: which days a change to a visit re-opens (DentalRecordVisitLinks' half of the rule) ──────

    private static readonly DateOnly Day14 = new(2026, 8, 14);
    private static readonly DateOnly Day15 = new(2026, 8, 15);

    private static DentalRecordVisitLink.VisitFacts Facts(
        DateTime at, AppointmentStatus status = AppointmentStatus.Scheduled, Guid? patientId = null) =>
        new(ClinicId, patientId ?? PatientId, at, status);

    private static DentalRecordVisitLink.FicheDay DayOf(DateOnly day, Guid? patientId = null) =>
        new(ClinicId, patientId ?? PatientId, day);

    [Fact]
    public void A_New_Visit_Asks_Its_Own_Day()
    {
        Assert.Equal(new[] { DayOf(Day14) }, DentalRecordVisitLink.DaysToAskAgain(null, Facts(InterventionDate)));
    }

    // The minutely status pass: every status it writes is one a fiche can document, on the same day — nothing to ask.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.InProgress, AppointmentStatus.AwaitingClosure)]
    [InlineData(AppointmentStatus.AwaitingClosure, AppointmentStatus.Completed)]
    public void An_Advance_Between_Documentable_Statuses_Asks_Nothing(AppointmentStatus from, AppointmentStatus to)
    {
        Assert.Empty(DentalRecordVisitLink.DaysToAskAgain(Facts(InterventionDate, from), Facts(InterventionDate, to)));
    }

    // A cancelled or missed twin may leave its day with exactly one visit.
    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void A_Visit_That_Did_Not_Happen_Asks_The_Day_It_Leaves(AppointmentStatus outcome)
    {
        Assert.Equal(
            new[] { DayOf(Day14) },
            DentalRecordVisitLink.DaysToAskAgain(Facts(InterventionDate), Facts(InterventionDate, outcome)));
    }

    [Fact]
    public void A_Reactivated_Visit_Asks_Its_Day()
    {
        Assert.Equal(
            new[] { DayOf(Day14) },
            DentalRecordVisitLink.DaysToAskAgain(
                Facts(InterventionDate, AppointmentStatus.Cancelled), Facts(InterventionDate)));
    }

    [Fact]
    public void A_Visit_Moved_To_Another_Day_Asks_Both_Days()
    {
        Assert.Equal(
            new[] { DayOf(Day14), DayOf(Day15) },
            DentalRecordVisitLink.DaysToAskAgain(Facts(InterventionDate), Facts(InterventionDate.AddDays(1))));
    }

    // Same Tunisian day, another hour: the visit is still that day's candidate, so nothing changed for any fiche.
    [Fact]
    public void A_Visit_Moved_Within_Its_Day_Asks_Nothing()
    {
        Assert.Empty(DentalRecordVisitLink.DaysToAskAgain(Facts(InterventionDate), Facts(InterventionDate.AddHours(6))));
    }

    [Fact]
    public void A_Visit_Reassigned_To_Another_Patient_Asks_Both_Patients_Day()
    {
        var other = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        Assert.Equal(
            new[] { DayOf(Day14, other), DayOf(Day14) },
            DentalRecordVisitLink.DaysToAskAgain(
                Facts(InterventionDate, patientId: other), Facts(InterventionDate)));
    }

    // A blocked slot carries no patient, so no fiche can document it — whatever happens to it.
    [Fact]
    public void A_Blocked_Slot_Asks_Nothing()
    {
        var blocked = new DentalRecordVisitLink.VisitFacts(ClinicId, null, InterventionDate, AppointmentStatus.Scheduled);

        Assert.Empty(DentalRecordVisitLink.DaysToAskAgain(null, blocked));
        Assert.Empty(DentalRecordVisitLink.DaysToAskAgain(blocked, blocked with { Status = AppointmentStatus.Cancelled }));
    }

    // 23:30 in Tunis on the 14th is 22:30 UTC; 00:30 on the 15th is 23:30 UTC — the same UTC date.
    [Fact]
    public void The_Day_Asked_Is_The_Clinics_Own()
    {
        Assert.Equal(Day14, DentalRecordVisitLink.DayOf(new DateTime(2026, 8, 14, 22, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(Day15, DentalRecordVisitLink.DayOf(new DateTime(2026, 8, 14, 23, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Only_A_Single_Candidate_Is_An_Answer()
    {
        var one = Guid.NewGuid();

        Assert.Null(DentalRecordVisitLink.OnlyCandidate(Array.Empty<Guid>()));
        Assert.Equal(one, DentalRecordVisitLink.OnlyCandidate(new[] { one }));
        Assert.Equal(one, DentalRecordVisitLink.OnlyCandidate(new[] { one, one }));
        Assert.Null(DentalRecordVisitLink.OnlyCandidate(new[] { one, Guid.NewGuid() }));
    }

    // ── The write itself ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_Unlinked_Fiche_Takes_The_Visit()
    {
        var fiche = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, isAdultTeeth: true);
        var visit = Guid.NewGuid();

        Assert.True(fiche.LinkToVisit(visit));
        Assert.Equal(visit, fiche.AppointmentId);
    }

    // A deep link, or a day that had one visit when the fiche was saved, is never second-guessed by a later booking.
    [Fact]
    public void A_Linked_Fiche_Is_Never_Repointed()
    {
        var first = Guid.NewGuid();
        var fiche = new DentalRecord(
            Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, isAdultTeeth: true, appointmentId: first);

        Assert.False(fiche.LinkToVisit(Guid.NewGuid()));
        Assert.Equal(first, fiche.AppointmentId);
    }

    [Fact]
    public void The_Empty_Id_Links_Nothing()
    {
        var fiche = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, isAdultTeeth: true);

        Assert.False(fiche.LinkToVisit(Guid.Empty));
        Assert.Null(fiche.AppointmentId);
    }
}
