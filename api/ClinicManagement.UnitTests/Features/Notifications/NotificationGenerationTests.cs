using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Notifications;

/// <summary>
/// Generation rules for the in-app staff feed: what the <see cref="NotificationGenerator"/> writes, and
/// the trigger decisions the command handlers make (actor exclusion, &lt;24h reminder skip, low-stock
/// crossing, cancel/reschedule/reactivation). Covers spec US-2…US-5.
/// </summary>
public class NotificationGenerationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ---- NotificationGenerator (the writer) ---------------------------------

    private sealed class GeneratorHarness
    {
        public Mock<IStaffNotificationRepository> Repo { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();
        public List<StaffNotification> Added { get; } = new();

        public GeneratorHarness()
        {
            Repo.Setup(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
                .Callback<StaffNotification, CancellationToken>((n, _) => Added.Add(n))
                .Returns(Task.CompletedTask);
        }

        public NotificationGenerator Generator() =>
            new(Repo.Object, Doctors.Object, Uow.Object, Realtime.Object, NullLogger<NotificationGenerator>.Instance);
    }

    // [US-2] A created notification records the actor (to exclude) and deep-link target, and broadcasts.
    [Fact]
    public async Task AppointmentCreated_Records_Actor_And_Broadcasts()
    {
        var h = new GeneratorHarness();
        await h.Generator().AppointmentCreatedAsync(
            ClinicId, appointmentId: Guid.NewGuid(), actorUserId: "local|actor",
            patientName: "Jean Dupont", appointmentDateTimeUtc: DateTime.UtcNow.AddDays(2));

        var n = Assert.Single(h.Added);
        Assert.Equal(NotificationCategory.AppointmentCreated, n.Category);
        Assert.Equal("local|actor", n.ActorUserId);
        Assert.Equal(NotificationTargetKind.Appointment, n.TargetKind);
        Assert.Null(n.TargetUserId); // [AC-8] existing categories stay clinic-wide (no per-user targeting)
        h.Realtime.Verify(r => r.NotifyEntityChangedAsync(ClinicId, "notifications", It.IsAny<CancellationToken>()), Times.Once);
    }

    // [US-4] A reminder scheduled for an appointment >24h out is stored due at appt-24h, visible to all.
    [Fact]
    public async Task ScheduleReminder_More_Than_24h_Out_Creates_Reminder_At_Due_Time()
    {
        var h = new GeneratorHarness();
        var apptTime = DateTime.UtcNow.AddHours(48);

        await h.Generator().ScheduleAppointmentReminderAsync(ClinicId, Guid.NewGuid(), "Jean Dupont", apptTime);

        var n = Assert.Single(h.Added);
        Assert.Equal(NotificationCategory.Reminder, n.Category);
        Assert.Null(n.ActorUserId); // visible to all staff
        Assert.Equal(apptTime.AddHours(-24), n.EffectiveFeedTime, TimeSpan.FromSeconds(1));
        // A future-dated reminder isn't visible in any feed yet, so no client refetch is triggered.
        h.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // [US-4] Same-day booking (<24h out) schedules no reminder — the "created" notification suffices.
    [Fact]
    public async Task ScheduleReminder_Less_Than_24h_Out_Creates_Nothing()
    {
        var h = new GeneratorHarness();

        await h.Generator().ScheduleAppointmentReminderAsync(ClinicId, Guid.NewGuid(), "Jean Dupont", DateTime.UtcNow.AddHours(3));

        Assert.Empty(h.Added);
    }

    // [US-4] Cancelling suppresses a pending reminder.
    [Fact]
    public async Task Cancelled_Suppresses_Pending_Reminder()
    {
        var h = new GeneratorHarness();
        var appointmentId = Guid.NewGuid();
        var reminder = new StaffNotification(
            Guid.NewGuid(), ClinicId, NotificationCategory.Reminder, "Rappel", "…",
            DateTime.UtcNow.AddHours(10), NotificationTargetKind.Appointment, appointmentId: appointmentId);
        h.Repo.Setup(r => r.GetReminderByAppointmentAsync(appointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reminder);

        await h.Generator().AppointmentCancelledAsync(
            ClinicId, appointmentId, "local|actor", "Jean Dupont", DateTime.UtcNow.AddDays(2));

        Assert.Contains(h.Added, n => n.Category == NotificationCategory.AppointmentCancelled);
        h.Repo.Verify(r => r.RemoveAsync(reminder, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [US-4] Rescheduling moves an existing reminder to reflect the new time.
    [Fact]
    public async Task Rescheduled_Moves_Existing_Reminder()
    {
        var h = new GeneratorHarness();
        var appointmentId = Guid.NewGuid();
        var oldTime = DateTime.UtcNow.AddDays(2);
        var newTime = DateTime.UtcNow.AddDays(5);
        var reminder = new StaffNotification(
            Guid.NewGuid(), ClinicId, NotificationCategory.Reminder, "Rappel", "…",
            oldTime.AddHours(-24), NotificationTargetKind.Appointment, appointmentId: appointmentId);
        h.Repo.Setup(r => r.GetReminderByAppointmentAsync(appointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reminder);

        await h.Generator().AppointmentRescheduledAsync(
            ClinicId, appointmentId, "local|actor", "Jean Dupont", oldTime, newTime);

        Assert.Contains(h.Added, n => n.Category == NotificationCategory.AppointmentRescheduled);
        Assert.Equal(newTime.AddHours(-24), reminder.EffectiveFeedTime, TimeSpan.FromSeconds(1));
        h.Repo.Verify(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [US-5] Low stock is visible to all staff (no actor exclusion) and targets the stock screen.
    [Fact]
    public async Task LowStock_Has_No_Actor_And_Targets_Stock()
    {
        var h = new GeneratorHarness();
        await h.Generator().LowStockAsync(ClinicId, Guid.NewGuid(), "Gants", currentStock: 2, minimumStockLevel: 5);

        var n = Assert.Single(h.Added);
        Assert.Equal(NotificationCategory.LowStock, n.Category);
        Assert.Null(n.ActorUserId);
        Assert.Equal(NotificationTargetKind.StockItem, n.TargetKind);
    }

    // Best-effort: a persistence failure is swallowed (never breaks the core operation).
    [Fact]
    public async Task Generator_Swallows_Persistence_Failure()
    {
        var h = new GeneratorHarness();
        h.Repo.Setup(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        // Must not throw.
        await h.Generator().AppointmentCreatedAsync(
            ClinicId, Guid.NewGuid(), "local|actor", "Jean Dupont", DateTime.UtcNow.AddDays(2));
    }

    // ---- UpdateStockItemCommandHandler: the not-low → low crossing decision -----

    private sealed class StockHarness
    {
        public Mock<IStockItemRepository> Stock { get; } = new();
        public Mock<ICurrentClinicResolver> ClinicResolver { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<INotificationGenerator> Generator { get; } = new();

        public StockHarness()
        {
            ClinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public UpdateStockItemCommandHandler Handler() =>
            new(Stock.Object, ClinicResolver.Object, Uow.Object, Generator.Object);

        public StockItem ExistingItem(int currentStock, int min)
        {
            var item = new StockItem(Guid.NewGuid(), ClinicId, "Gants", "Medical Supplies", "Box", min, 100);
            item.SetCurrentStock(currentStock);
            Stock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
            return item;
        }
    }

    // [US-5] Not-low → low (quantity drop) fires exactly one low-stock notification.
    [Fact]
    public async Task Update_Fires_LowStock_On_Quantity_Drop_Crossing()
    {
        var h = new StockHarness();
        var item = h.ExistingItem(currentStock: 10, min: 5); // not low

        var result = await h.Handler().Handle(
            new UpdateStockItemCommand { Id = item.Id, Name = "Gants", Category = "Medical Supplies", Unit = "Box", CurrentStock = 3, MinimumStockLevel = 5 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        h.Generator.Verify(g => g.LowStockAsync(ClinicId, item.Id, "Gants", 3, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [US-5] Not-low → low via raising the minimum (quantity unchanged) fires once.
    [Fact]
    public async Task Update_Fires_LowStock_On_Minimum_Raise_Crossing()
    {
        var h = new StockHarness();
        var item = h.ExistingItem(currentStock: 10, min: 5); // not low

        var result = await h.Handler().Handle(
            new UpdateStockItemCommand { Id = item.Id, Name = "Gants", Category = "Medical Supplies", Unit = "Box", CurrentStock = 10, MinimumStockLevel = 15 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        h.Generator.Verify(g => g.LowStockAsync(ClinicId, item.Id, "Gants", 10, 15, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [US-5] Edge-triggered: staying low (already low before) fires nothing.
    [Fact]
    public async Task Update_Does_Not_Fire_LowStock_When_Already_Low()
    {
        var h = new StockHarness();
        var item = h.ExistingItem(currentStock: 3, min: 5); // already low

        var result = await h.Handler().Handle(
            new UpdateStockItemCommand { Id = item.Id, Name = "Gants", Category = "Medical Supplies", Unit = "Box", CurrentStock = 2, MinimumStockLevel = 5 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        h.Generator.Verify(g => g.LowStockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- UpdateAppointmentCommandHandler: cancel / reschedule / reactivation ----

    // An appointment WITH a patient (PatientId set) so notification triggers fire. The Patient navigation
    // is left unloaded; the handler falls back to "Patient" for the name, which these tests don't assert on.
    private static Appointment AppointmentWithPatient(Guid clinicId, DateTime when) =>
        new(Guid.NewGuid(), clinicId, patientId: Guid.NewGuid(), doctorId: null,
            appointmentDateTime: when, duration: TimeSpan.FromMinutes(30));

    private static (UpdateAppointmentCommandHandler handler, Mock<INotificationGenerator> gen) UpdateHandler(Appointment appointment)
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        var clinicResolver = new Mock<ICurrentClinicResolver>();
        clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(appointment.ClinicId));
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("local|actor");
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var gen = new Mock<INotificationGenerator>();

        var handler = new UpdateAppointmentCommandHandler(
            repo.Object, new Mock<IProcedureTypeRepository>().Object, clinicResolver.Object,
            context.Object, uow.Object, ScopeFactory(), gen.Object,
            new Mock<IReminderScheduler>().Object,
            NullLogger<UpdateAppointmentCommandHandler>.Instance);
        return (handler, gen);
    }

    // [US-3] Cancelling an appointment with a patient fires a cancelled notification.
    [Fact]
    public async Task Update_To_Cancelled_Fires_Cancelled_Notification()
    {
        var appointment = AppointmentWithPatient(ClinicId, DateTime.UtcNow.AddDays(2));
        var (handler, gen) = UpdateHandler(appointment);

        var result = await handler.Handle(new UpdateAppointmentCommand { Id = appointment.Id, Status = "Cancelled" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        gen.Verify(g => g.AppointmentCancelledAsync(appointment.ClinicId, appointment.Id, "local|actor", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        gen.Verify(g => g.AppointmentRescheduledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        // [AC-4] Cancelling removes the pending post-visit review (and never keeps it in sync).
        gen.Verify(g => g.CancelPostVisitReviewAsync(appointment.ClinicId, appointment.Id, It.IsAny<CancellationToken>()), Times.Once);
        gen.Verify(g => g.EnsurePostVisitReviewAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [US-3] Changing the date fires a rescheduled notification.
    [Fact]
    public async Task Update_With_New_Date_Fires_Rescheduled_Notification()
    {
        var appointment = AppointmentWithPatient(ClinicId, DateTime.UtcNow.AddDays(2));
        var (handler, gen) = UpdateHandler(appointment);
        var newDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(5), DateTimeKind.Utc);

        var result = await handler.Handle(new UpdateAppointmentCommand { Id = appointment.Id, AppointmentDateTime = newDate }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        gen.Verify(g => g.AppointmentRescheduledAsync(appointment.ClinicId, appointment.Id, "local|actor", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        // [AC-4] Rescheduling keeps the post-visit review in sync — moved to the new end (start + 30-min duration).
        gen.Verify(g => g.EnsurePostVisitReviewAsync(
            appointment.ClinicId, appointment.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.Is<DateTime>(d => d == newDate + TimeSpan.FromMinutes(30)), It.IsAny<CancellationToken>()), Times.Once);
    }

    // [US-3 / R-3] Reactivating a cancelled appointment (status → Scheduled, same date) must never emit a
    // bogus "rescheduled" (nor a "cancelled"). Note: the domain itself forbids rescheduling a cancelled
    // appointment (Appointment.Reschedule throws), so the update returns a failure here — but the key
    // guarantee for THIS feature is that no notification is generated on that path.
    [Fact]
    public async Task Reactivating_Cancelled_Same_Date_Fires_No_Notification()
    {
        var appointment = AppointmentWithPatient(ClinicId, DateTime.UtcNow.AddDays(2));
        appointment.Cancel();
        var (handler, gen) = UpdateHandler(appointment);

        await handler.Handle(new UpdateAppointmentCommand { Id = appointment.Id, Status = "Scheduled" }, CancellationToken.None);

        gen.Verify(g => g.AppointmentRescheduledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        gen.Verify(g => g.AppointmentCancelledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- NotificationGenerator: post-visit review (EnsurePostVisitReviewAsync / CancelPostVisitReviewAsync) ----

    // [AC-1 / AC-3] A created post-visit review is future-dated at the appointment END time and carries no
    // actor (it is a prompt TO the doctor). Future-dated → nothing visible yet, so no realtime broadcast.
    [Fact]
    public async Task EnsurePostVisitReview_Creates_Review_Due_At_Appointment_End()
    {
        var h = new GeneratorHarness();
        var end = DateTime.UtcNow.AddHours(2);

        await h.Generator().EnsurePostVisitReviewAsync(
            ClinicId, appointmentId: Guid.NewGuid(), doctorId: null, patientName: "Jean Dupont", appointmentEndUtc: end);

        var n = Assert.Single(h.Added);
        Assert.Equal(NotificationCategory.PostVisitReview, n.Category);
        Assert.Equal(end, n.EffectiveFeedTime, TimeSpan.FromSeconds(1));
        Assert.Null(n.ActorUserId);
        Assert.Equal(NotificationTargetKind.Appointment, n.TargetKind);
        // Future-dated → not visible in any feed yet → no client refetch triggered.
        h.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // [AC-2] A DoctorId that resolves to a Doctor with a linked user targets ONLY that user.
    [Fact]
    public async Task EnsurePostVisitReview_Targets_The_Linked_Doctor_User()
    {
        var h = new GeneratorHarness();
        var doctorId = Guid.NewGuid();
        var doctor = new Doctor(doctorId, ClinicId, "Alice", "Martin", "Dentiste");
        doctor.LinkToUser("local|doc-user");
        h.Doctors.Setup(d => d.GetByIdAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);

        await h.Generator().EnsurePostVisitReviewAsync(
            ClinicId, Guid.NewGuid(), doctorId.ToString(), "Jean Dupont", DateTime.UtcNow.AddHours(1));

        Assert.Equal("local|doc-user", Assert.Single(h.Added).TargetUserId);
    }

    // [AC-2] Doctor exists but has no linked user → visible to all staff (null target).
    [Fact]
    public async Task EnsurePostVisitReview_Targets_All_Staff_When_Doctor_Has_No_User()
    {
        var h = new GeneratorHarness();
        var doctorId = Guid.NewGuid();
        var doctor = new Doctor(doctorId, ClinicId, "Alice", "Martin", "Dentiste"); // not linked to a user
        h.Doctors.Setup(d => d.GetByIdAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);

        await h.Generator().EnsurePostVisitReviewAsync(
            ClinicId, Guid.NewGuid(), doctorId.ToString(), "Jean Dupont", DateTime.UtcNow.AddHours(1));

        Assert.Null(Assert.Single(h.Added).TargetUserId);
    }

    // [AC-2] Null or unparsable DoctorId → visible to all staff (null target); no doctor lookup is attempted.
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task EnsurePostVisitReview_Targets_All_Staff_When_DoctorId_Missing_Or_Invalid(string? doctorId)
    {
        var h = new GeneratorHarness();

        await h.Generator().EnsurePostVisitReviewAsync(
            ClinicId, Guid.NewGuid(), doctorId, "Jean Dupont", DateTime.UtcNow.AddHours(1));

        Assert.Null(Assert.Single(h.Added).TargetUserId);
        h.Doctors.Verify(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-4] Rescheduling MOVES the existing review to the new end time and recomputes its target (here the
    // doctor is cleared → back to all staff) instead of adding a second row.
    [Fact]
    public async Task EnsurePostVisitReview_Moves_Existing_Review_Instead_Of_Adding()
    {
        var h = new GeneratorHarness();
        var appointmentId = Guid.NewGuid();
        var existing = new StaffNotification(
            Guid.NewGuid(), ClinicId, NotificationCategory.PostVisitReview, "Compte rendu", "…",
            DateTime.UtcNow.AddHours(1), NotificationTargetKind.Appointment,
            appointmentId: appointmentId, targetUserId: "local|old-doc");
        h.Repo.Setup(r => r.GetPostVisitReviewByAppointmentAsync(appointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var newEnd = DateTime.UtcNow.AddHours(5);

        await h.Generator().EnsurePostVisitReviewAsync(
            ClinicId, appointmentId, doctorId: null, "Jean Dupont", newEnd);

        Assert.Empty(h.Added); // moved in place, not added
        Assert.Equal(newEnd, existing.EffectiveFeedTime, TimeSpan.FromSeconds(1));
        Assert.Null(existing.TargetUserId); // recomputed from the (now-null) doctor
    }

    // [AC-4] Cancelling removes the pending review.
    [Fact]
    public async Task CancelPostVisitReview_Removes_Existing()
    {
        var h = new GeneratorHarness();
        var appointmentId = Guid.NewGuid();
        var existing = new StaffNotification(
            Guid.NewGuid(), ClinicId, NotificationCategory.PostVisitReview, "Compte rendu", "…",
            DateTime.UtcNow.AddHours(1), NotificationTargetKind.Appointment, appointmentId: appointmentId);
        h.Repo.Setup(r => r.GetPostVisitReviewByAppointmentAsync(appointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await h.Generator().CancelPostVisitReviewAsync(ClinicId, appointmentId);

        h.Repo.Verify(r => r.RemoveAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-4] Cancelling when there is no review is a harmless no-op.
    [Fact]
    public async Task CancelPostVisitReview_No_Op_When_None_Exists()
    {
        var h = new GeneratorHarness();

        await h.Generator().CancelPostVisitReviewAsync(ClinicId, Guid.NewGuid());

        h.Repo.Verify(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- CreateAppointmentCommandHandler: post-visit scheduling trigger decision (AC-1) ----

    private static Patient PatientIn(Guid clinicId) => new(
        Guid.NewGuid(), clinicId, "Jean", "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    private static (CreateAppointmentCommandHandler handler, Mock<INotificationGenerator> gen) CreateHandler(Patient? patient)
    {
        var appointments = new Mock<IAppointmentRepository>();
        var patients = new Mock<IPatientRepository>();
        if (patient != null)
        {
            patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        }
        var procedures = new Mock<IProcedureTypeRepository>();
        var users = new Mock<IUserRepository>();
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var gen = new Mock<INotificationGenerator>();

        var handler = new CreateAppointmentCommandHandler(
            appointments.Object, patients.Object, procedures.Object, users.Object,
            context.Object, uow.Object, gen.Object,
            new Mock<IReminderScheduler>().Object);
        return (handler, gen);
    }

    // [AC-1] Creating an appointment WITH a patient schedules a post-visit review due at start + duration.
    [Fact]
    public async Task Create_With_Patient_Schedules_PostVisitReview_At_End()
    {
        var patient = PatientIn(ClinicId);
        var (handler, gen) = CreateHandler(patient);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc);

        var result = await handler.Handle(
            new CreateAppointmentCommand { PatientId = patient.Id, AppointmentDateTime = start, DurationMinutes = 30 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        gen.Verify(g => g.EnsurePostVisitReviewAsync(
            ClinicId, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            start + TimeSpan.FromMinutes(30), It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-1] A patient-less "busy slot" appointment schedules NOTHING (no post-visit review).
    [Fact]
    public async Task Create_Without_Patient_Schedules_No_PostVisitReview()
    {
        var (handler, gen) = CreateHandler(null);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc);

        var result = await handler.Handle(
            new CreateAppointmentCommand { PatientId = null, AppointmentDateTime = start, DurationMinutes = 30 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        gen.Verify(g => g.EnsurePostVisitReviewAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IServiceScopeFactory ScopeFactory()
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IGoogleCalendarSyncService)))
            .Returns(new Mock<IGoogleCalendarSyncService>().Object);
        provider.Setup(p => p.GetService(typeof(ILogger<UpdateAppointmentCommandHandler>)))
            .Returns(NullLogger<UpdateAppointmentCommandHandler>.Instance);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
