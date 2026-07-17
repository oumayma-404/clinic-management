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

    // ---- Appointment.MarkVisitCompleted domain transition (AC-7) ----

    private static Appointment ApptInStatus(AppointmentStatus status)
    {
        var appt = new Appointment(
            Guid.NewGuid(), ClinicId, patientId: Guid.NewGuid(), doctorId: null,
            appointmentDateTime: DateTime.UtcNow, duration: TimeSpan.FromMinutes(30));
        switch (status)
        {
            case AppointmentStatus.Scheduled: break;
            case AppointmentStatus.Confirmed: appt.Confirm(); break;
            case AppointmentStatus.InProgress: appt.Start(); break;
            case AppointmentStatus.Completed: appt.Start(); appt.Complete(); break;
            case AppointmentStatus.Cancelled: appt.Cancel(); break;
            case AppointmentStatus.NoShow: appt.MarkAsNoShow(); break;
        }
        return appt;
    }

    // [AC-7] Record-fill completes an appointment that is still active.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    public void MarkVisitCompleted_From_Active_State_Completes(AppointmentStatus status)
    {
        var appt = ApptInStatus(status);

        appt.MarkVisitCompleted();

        Assert.Equal(AppointmentStatus.Completed, appt.Status);
    }

    // [AC-7] A terminal appointment (Cancelled/Completed/NoShow) is left unchanged — an idempotent no-op so a
    // second staff member filling a record is harmless.
    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void MarkVisitCompleted_From_Terminal_State_Is_No_Op(AppointmentStatus status)
    {
        var appt = ApptInStatus(status);

        appt.MarkVisitCompleted();

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
                Appointments.Object, Resolver.Object, Generator.Object, Realtime.Object, Uow.Object,
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
