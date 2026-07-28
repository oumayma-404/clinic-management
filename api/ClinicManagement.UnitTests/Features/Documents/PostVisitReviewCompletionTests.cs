using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// The post-visit completion side-effect (spec AC-7): the guarded <see cref="Appointment.MarkVisitCompleted"/>
/// transition, and <see cref="CreateMedicalDocumentCommandHandler"/> completing the documented appointment
/// (best-effort, post-commit) when the created record carries an <c>AppointmentId</c>.
/// </summary>
public class PostVisitReviewCompletionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ---- Appointment.MarkVisitCompleted domain transition ----
    //
    // [AC-P1.13] **Rewritten.** The two theories that used to live here pinned the *silent-no-op* contract:
    // both asserted only "the status did not change" for Cancelled/Completed/NoShow, which is still true and
    // is exactly why they would have kept passing — a test that survives AC-P1.12 unchanged was pinning the
    // defect. What AC-P1.12 changed is that the three cases are no longer indistinguishable: the method now
    // returns a `VisitCompletionOutcome`, so the caller can tell "already closed" from "a fiche was filed
    // against a visit the schedule says never happened".
    //
    // The exhaustive per-state matrix now lives in Domain/AppointmentStatusTransitionTests.cs alongside the
    // rest of the machine; what remains here is the outcome contract this file's handler tests depend on.

    private static Appointment ApptInStatus(AppointmentStatus status)
    {
        var appt = new Appointment(
            Guid.NewGuid(), ClinicId, patientId: Guid.NewGuid(), doctorId: null,
            appointmentDateTime: DateTime.UtcNow, duration: TimeSpan.FromMinutes(30));
        switch (status)
        {
            case AppointmentStatus.Scheduled: break;
            case AppointmentStatus.Confirmed: appt.Confirm(); break;
            // Start() requires Scheduled or Confirmed; Complete() is now reachable from either (AC-P1.1).
            case AppointmentStatus.InProgress: appt.Start(); break;
            case AppointmentStatus.Completed: appt.Start(); appt.Complete(); break;
            case AppointmentStatus.Cancelled: appt.Cancel(); break;
            case AppointmentStatus.NoShow: appt.MarkAsNoShow(); break;
        }
        return appt;
    }

    // Record-fill closes an appointment that is still open, and says so.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    public void MarkVisitCompleted_From_Active_State_Completes(AppointmentStatus status) // [AC-P1.12]
    {
        var appt = ApptInStatus(status);

        var outcome = appt.MarkVisitCompleted();

        Assert.Equal(VisitCompletionOutcome.Completed, outcome);
        Assert.Equal(AppointmentStatus.Completed, appt.Status);
    }

    // Already closed: still a no-op on the status, but now reported as *idempotent* rather than as the same
    // silence a contradiction produced. The handler relies on this to still clear the post-visit review.
    [Fact]
    public void MarkVisitCompleted_From_Completed_Reports_Idempotent() // [AC-P1.12 / AC-P1.13]
    {
        var appt = ApptInStatus(AppointmentStatus.Completed);

        var outcome = appt.MarkVisitCompleted();

        Assert.Equal(VisitCompletionOutcome.AlreadyCompleted, outcome);
        Assert.Equal(AppointmentStatus.Completed, appt.Status);
    }

    // The case the old test collapsed into "no-op": a record filed against a cancelled or missed visit. The
    // status is still left alone — a cancelled visit is never silently reopened — but it is now *reported*.
    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void MarkVisitCompleted_From_Cancelled_Or_NoShow_Reports_A_Contradiction(AppointmentStatus status)
    {
        var appt = ApptInStatus(status);

        var outcome = appt.MarkVisitCompleted();

        Assert.Equal(VisitCompletionOutcome.Contradicted, outcome);
        Assert.Equal(status, appt.Status);
    }

    // ---- CreateMedicalDocumentCommandHandler completion side-effect (AC-7) ----

    private static Patient PatientIn(Guid clinicId) => new(
        Guid.NewGuid(), clinicId, "Jean", "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    private sealed class DocHarness
    {
        public Mock<IMedicalDocumentRepository> Docs { get; } = new();
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IPatientFolderRepository> Folders { get; } = new();
        public Mock<IPatientFileRepository> Files { get; } = new();
        public Mock<IFileStorage> Storage { get; } = new();
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<ICurrentClinicResolver> Resolver { get; } = new();
        public Mock<IClinicContext> ClinicContext { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<INotificationGenerator> Generator { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Patient Patient { get; }

        public DocHarness()
        {
            Patient = PatientIn(ClinicId);
            Patients.Setup(r => r.GetByIdAsync(Patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Patient);
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public CreateMedicalDocumentCommandHandler Handler() =>
            new(Docs.Object, Patients.Object, Folders.Object, Files.Object, Storage.Object,
                Appointments.Object, Resolver.Object, ClinicContext.Object, Doctors.Object, Clinics.Object,
                Generator.Object, Realtime.Object, Uow.Object,
                NullLogger<CreateMedicalDocumentCommandHandler>.Instance);

        public CreateMedicalDocumentCommand Command(Guid? appointmentId) => new()
        {
            PatientId = Patient.Id,
            DocumentType = "prescription",
            DocumentDate = DateTime.UtcNow,
            ContentJson = "[]",
            AppointmentId = appointmentId
        };
    }

    // [AC-7] A record carrying an AppointmentId completes the (active) appointment and clears its review.
    [Fact]
    public async Task Create_With_AppointmentId_Completes_Appointment_And_Cancels_Review()
    {
        var h = new DocHarness();
        var appt = new Appointment(Guid.NewGuid(), ClinicId, h.Patient.Id, null, DateTime.UtcNow, TimeSpan.FromMinutes(30));
        h.Appointments.Setup(r => r.GetByIdAsync(appt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appt);

        var result = await h.Handler().Handle(h.Command(appt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Completed, appt.Status); // persisted via EF change tracking (no explicit UpdateAsync)
        h.Generator.Verify(g => g.CancelPostVisitReviewAsync(ClinicId, appt.Id, It.IsAny<CancellationToken>()), Times.Once);
        // Completion broadcasts the "appointments" key so calendar views refetch the now-Completed status.
        h.Realtime.Verify(r => r.NotifyEntityChangedAsync(ClinicId, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-7] An appointment in another clinic is left untouched (silent no-op); the record still succeeds.
    [Fact]
    public async Task Create_With_CrossClinic_AppointmentId_Leaves_It_Unchanged()
    {
        var h = new DocHarness();
        var appt = new Appointment(Guid.NewGuid(), OtherClinicId, h.Patient.Id, null, DateTime.UtcNow, TimeSpan.FromMinutes(30));
        h.Appointments.Setup(r => r.GetByIdAsync(appt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appt);

        var result = await h.Handler().Handle(h.Command(appt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Scheduled, appt.Status);
        h.Generator.Verify(g => g.CancelPostVisitReviewAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        // No completion side-effect for a foreign appointment → no "appointments" broadcast either.
        h.Realtime.Verify(r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-7] Without an AppointmentId the completion side-effect never runs.
    [Fact]
    public async Task Create_Without_AppointmentId_Runs_No_Completion_SideEffect()
    {
        var h = new DocHarness();

        var result = await h.Handler().Handle(h.Command(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        h.Appointments.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Generator.Verify(g => g.CancelPostVisitReviewAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-7] A failure inside the completion side-effect must NOT fail the (already committed) record creation.
    [Fact]
    public async Task Create_Succeeds_Even_If_Completion_SideEffect_Throws()
    {
        var h = new DocHarness();
        var appointmentId = Guid.NewGuid();
        h.Appointments.Setup(r => r.GetByIdAsync(appointmentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await h.Handler().Handle(h.Command(appointmentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
