using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// When a fiche de soins makes its visit « Terminé » (<c>seance-au-fauteuil</c>). A saved fiche is evidence the
/// patient came, not evidence the séance is over: the visit is completed when the dentist presses « Terminer la
/// séance » or once the séance has ended by the clock — never by a save made while the patient is in the chair.
///
/// <para>Every instant is a fixed literal: the behaviour is a clock boundary, so a fixture built from
/// <c>DateTime.UtcNow</c> would pass or fail depending on when the suite runs.</para>
/// </summary>
public class FicheVisitCompletionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>The visit's slot: 10:00 → 11:00 UTC.</summary>
    private static readonly DateTime SlotStart = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime DuringTheSeance = new(2026, 11, 18, 10, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterTheSeance = new(2026, 11, 18, 11, 5, 0, DateTimeKind.Utc);

    private static Appointment RunningVisit(Guid? clinicId = null)
    {
        var visit = new Appointment(
            Guid.NewGuid(), clinicId ?? ClinicId, Guid.NewGuid(), null, SlotStart, TimeSpan.FromHours(1));
        visit.Start();
        return visit;
    }

    private sealed class Harness
    {
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<INotificationGenerator> Notifications { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();

        public Harness(Appointment visit)
        {
            Appointments.Setup(r => r.GetByIdAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(visit);
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Task<FicheVisitOutcome> Apply(Appointment visit, bool finishSeance, bool isCreate, DateTime now) =>
            FicheVisitCompletion.ApplyAsync(
                visit.Id, ClinicId, finishSeance, isCreate, now,
                Appointments.Object, Uow.Object, Notifications.Object, Realtime.Object,
                NullLogger.Instance, CancellationToken.None);

        public void ReviewCancelled(Appointment visit, Times times) =>
            Notifications.Verify(
                n => n.CancelPostVisitReviewAsync(ClinicId, visit.Id, It.IsAny<CancellationToken>()), times);
    }

    // ── The rule ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Save_During_The_Seance_Does_Not_Complete_It()
    {
        Assert.False(FicheVisitCompletion.ShouldComplete(RunningVisit(), finishSeance: false, DuringTheSeance));
    }

    [Fact]
    public void Terminer_La_Seance_Completes_It_Whatever_The_Clock_Says()
    {
        Assert.True(FicheVisitCompletion.ShouldComplete(RunningVisit(), finishSeance: true, DuringTheSeance));
    }

    [Fact]
    public void A_Save_After_The_Seance_Completes_It()
    {
        Assert.True(FicheVisitCompletion.ShouldComplete(RunningVisit(), finishSeance: false, AfterTheSeance));
    }

    // A recorded retard pushes the séance's end back (`Appointment.ExpectedEnd`): at 11:05, a visit that started
    // 15 minutes late is still running until 11:15.
    [Fact]
    public void A_Recorded_Retard_Pushes_The_End_Back()
    {
        var visit = RunningVisit();
        visit.RecordLateArrival(15);

        Assert.False(FicheVisitCompletion.ShouldComplete(visit, finishSeance: false, AfterTheSeance));
        Assert.True(FicheVisitCompletion.ShouldComplete(
            visit, finishSeance: false, new DateTime(2026, 11, 18, 11, 15, 0, DateTimeKind.Utc)));
    }

    // ── Applying it after the save ────────────────────────────────────────────────────────────────────────────

    // The demo's case: the dentist saves at 10:15 with the patient in the chair. The visit stays « En cours »,
    // nothing is saved for it — and the « remplir la fiche ? » prompt is withdrawn, because the fiche now exists.
    [Fact]
    public async Task A_First_Save_During_The_Seance_Keeps_The_Visit_Running_And_Withdraws_The_Prompt()
    {
        var visit = RunningVisit();
        var h = new Harness(visit);

        var outcome = await h.Apply(visit, finishSeance: false, isCreate: true, DuringTheSeance);

        Assert.Equal(FicheVisitOutcome.LeftRunning, outcome);
        Assert.Equal(AppointmentStatus.InProgress, visit.Status);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        h.ReviewCancelled(visit, Times.Once());
    }

    [Fact]
    public async Task Terminer_La_Seance_Completes_The_Visit_Now()
    {
        var visit = RunningVisit();
        var h = new Harness(visit);

        var outcome = await h.Apply(visit, finishSeance: true, isCreate: false, DuringTheSeance);

        Assert.Equal(FicheVisitOutcome.Completed, outcome);
        Assert.Equal(AppointmentStatus.Completed, visit.Status);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicId, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    // The fiche written after the patient left — today's habit — is unchanged: its first save completes the visit.
    [Fact]
    public async Task A_First_Save_After_The_Seance_Completes_The_Visit_As_Before()
    {
        var visit = RunningVisit();
        var h = new Harness(visit);

        var outcome = await h.Apply(visit, finishSeance: false, isCreate: true, AfterTheSeance);

        Assert.Equal(FicheVisitOutcome.Completed, outcome);
        Assert.Equal(AppointmentStatus.Completed, visit.Status);
        h.ReviewCancelled(visit, Times.Once());
    }

    // A re-save in the middle of the séance touches neither the visit nor the prompt (already withdrawn by the create).
    [Fact]
    public async Task A_Re_Save_During_The_Seance_Changes_Nothing()
    {
        var visit = RunningVisit();
        var h = new Harness(visit);

        var outcome = await h.Apply(visit, finishSeance: false, isCreate: false, DuringTheSeance);

        Assert.Equal(FicheVisitOutcome.LeftRunning, outcome);
        Assert.Equal(AppointmentStatus.InProgress, visit.Status);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        h.ReviewCancelled(visit, Times.Never());
    }

    // A cancelled visit is not reopened by a fiche, even on « Terminer la séance » (AC-P1.12).
    [Fact]
    public async Task A_Cancelled_Visit_Is_Left_Cancelled()
    {
        var visit = RunningVisit();
        visit.Cancel("Annulé par le patient");
        var h = new Harness(visit);

        var outcome = await h.Apply(visit, finishSeance: true, isCreate: false, DuringTheSeance);

        Assert.Equal(FicheVisitOutcome.Contradicted, outcome);
        Assert.Equal(AppointmentStatus.Cancelled, visit.Status);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Another clinic's appointment id is ignored outright: nothing read back is written, nothing is broadcast.
    [Fact]
    public async Task Another_Clinics_Visit_Is_Not_Touched()
    {
        var visit = RunningVisit(OtherClinicId);
        var h = new Harness(visit);

        var outcome = await h.Apply(visit, finishSeance: true, isCreate: true, AfterTheSeance);

        Assert.Equal(FicheVisitOutcome.NoVisit, outcome);
        Assert.Equal(AppointmentStatus.InProgress, visit.Status);
        h.Notifications.Verify(
            n => n.CancelPostVisitReviewAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        h.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Best-effort: a failure after the fiche has committed is logged, never thrown back at the save.
    [Fact]
    public async Task A_Failure_Is_Swallowed()
    {
        var visit = RunningVisit();
        var h = new Harness(visit);
        h.Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var outcome = await h.Apply(visit, finishSeance: true, isCreate: false, DuringTheSeance);

        Assert.Equal(FicheVisitOutcome.NoVisit, outcome);
    }
}
