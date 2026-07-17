using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The reminder enqueuer (spec AC-1..AC-4, AC-9): enqueues one Pending reminder per configured channel at
/// the tiered send time, voids unsent reminders on cancel, and void + re-enqueues on reschedule — all
/// best-effort (a persistence failure never throws back to the appointment handler).
/// </summary>
public class ReminderSchedulerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class Harness
    {
        public Mock<INotificationRepository> Notifications { get; } = new();
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public List<Notification> Added { get; } = new();
        public List<Notification> Removed { get; } = new();

        private readonly string[] _channels;

        public Harness(params string[] channels)
        {
            _channels = channels;

            Notifications.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                .Callback<Notification, CancellationToken>((n, _) => Added.Add(n))
                .ReturnsAsync((Notification n, CancellationToken _) => n);
            Notifications.Setup(r => r.RemoveAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                .Callback<Notification, CancellationToken>((n, _) => Removed.Add(n))
                .Returns(Task.CompletedTask);
            Clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Clinic(ClinicId, "Clinique Test"));
        }

        public void HasExistingReminders(Guid appointmentId, params Notification[] existing) =>
            Notifications.Setup(r => r.GetByAppointmentIdAsync(appointmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

        private IConfiguration Config()
        {
            var dict = new Dictionary<string, string?>
            {
                ["Reminders:MinLeadHours"] = "1",
                ["Reminders:LeadTimesHours:0"] = "24",
                ["Reminders:LeadTimesHours:1"] = "6",
            };
            for (var i = 0; i < _channels.Length; i++)
            {
                dict[$"Reminders:Channels:{i}"] = _channels[i];
            }

            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        public ReminderScheduler Scheduler() =>
            new(Notifications.Object, Clinics.Object, Uow.Object, Config(), NullLogger<ReminderScheduler>.Instance);
    }

    private static Notification PendingReminder(Guid appointmentId, NotificationType type = NotificationType.SMS) =>
        new(Guid.NewGuid(), type, "Rappel de rendez-vous", "…", DateTime.UtcNow.AddHours(1), appointmentId, Guid.NewGuid());

    // [AC-1] Booking enqueues one Pending reminder per channel at the computed send time, with the rendered
    // French message (patient name + clinic name) and the appointment/patient links.
    [Fact]
    public async Task Schedule_Enqueues_One_Pending_Per_Channel_At_The_Computed_Send_Time()
    {
        var h = new Harness("Sms", "WhatsApp");
        var appt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc);
        var appointmentId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, appointmentId, patientId, "Jean Dupont", appt);

        Assert.Equal(2, h.Added.Count);
        Assert.Contains(h.Added, n => n.Type == NotificationType.SMS);
        Assert.Contains(h.Added, n => n.Type == NotificationType.WhatsApp);
        Assert.All(h.Added, n =>
        {
            Assert.Equal(NotificationStatus.Pending, n.Status);
            Assert.Equal(appointmentId, n.AppointmentId);
            Assert.Equal(patientId, n.PatientId);
            Assert.Equal(appt.AddHours(-24), n.ScheduledFor, TimeSpan.FromSeconds(1)); // largest future tier
            Assert.Contains("Jean Dupont", n.Message);
            Assert.Contains("Clinique Test", n.Message);
        });
    }

    // [AC-9] No channels configured → nothing is enqueued (no failure noise).
    [Fact]
    public async Task Schedule_Enqueues_Nothing_When_No_Channels_Configured()
    {
        var h = new Harness(); // no channels

        await h.Scheduler().ScheduleForAppointmentAsync(
            ClinicId, Guid.NewGuid(), Guid.NewGuid(), "Jean", DateTime.UtcNow.AddDays(2));

        Assert.Empty(h.Added);
    }

    // [AC-1 edge] An appointment inside the min-lead window enqueues no reminder.
    [Fact]
    public async Task Schedule_Enqueues_Nothing_When_Appointment_Is_Too_Soon()
    {
        var h = new Harness("Sms");

        await h.Scheduler().ScheduleForAppointmentAsync(
            ClinicId, Guid.NewGuid(), Guid.NewGuid(), "Jean", DateTime.UtcNow.AddMinutes(30));

        Assert.Empty(h.Added);
    }

    // [AC-4] Voiding removes only the unsent (Pending) reminders; already-Sent ones are left untouched.
    [Fact]
    public async Task Void_Removes_Only_Unsent_Reminders()
    {
        var h = new Harness("Sms");
        var appointmentId = Guid.NewGuid();
        var pending = PendingReminder(appointmentId);
        var sent = PendingReminder(appointmentId);
        sent.MarkAsSent();
        h.HasExistingReminders(appointmentId, pending, sent);

        await h.Scheduler().VoidForAppointmentAsync(appointmentId);

        Assert.Single(h.Removed);
        Assert.Same(pending, h.Removed[0]);
    }

    // [AC-3] Rescheduling voids the unsent reminders and re-enqueues fresh ones for the new time.
    [Fact]
    public async Task Reschedule_Voids_Unsent_And_ReEnqueues_For_The_New_Time()
    {
        var h = new Harness("Sms");
        var appointmentId = Guid.NewGuid();
        var oldPending = PendingReminder(appointmentId);
        h.HasExistingReminders(appointmentId, oldPending);
        var newAppt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3), DateTimeKind.Utc);

        await h.Scheduler().RescheduleForAppointmentAsync(ClinicId, appointmentId, Guid.NewGuid(), "Jean", newAppt);

        Assert.Contains(oldPending, h.Removed);
        var added = Assert.Single(h.Added);
        Assert.Equal(newAppt.AddHours(-24), added.ScheduledFor, TimeSpan.FromSeconds(1));
    }

    // [AC-3 edge] Rescheduling into the min-lead window voids the unsent reminders and enqueues nothing.
    [Fact]
    public async Task Reschedule_Into_Soon_Window_Voids_And_Enqueues_Nothing()
    {
        var h = new Harness("Sms");
        var appointmentId = Guid.NewGuid();
        var oldPending = PendingReminder(appointmentId);
        h.HasExistingReminders(appointmentId, oldPending);

        await h.Scheduler().RescheduleForAppointmentAsync(
            ClinicId, appointmentId, Guid.NewGuid(), "Jean", DateTime.UtcNow.AddMinutes(30));

        Assert.Contains(oldPending, h.Removed);
        Assert.Empty(h.Added);
    }

    // [AC-2] Enqueuing is best-effort: a persistence failure is swallowed, never thrown to the caller.
    [Fact]
    public async Task Schedule_Never_Throws_When_Persistence_Fails()
    {
        var h = new Harness("Sms");
        h.Notifications.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var exception = await Record.ExceptionAsync(() =>
            h.Scheduler().ScheduleForAppointmentAsync(
                ClinicId, Guid.NewGuid(), Guid.NewGuid(), "Jean", DateTime.UtcNow.AddDays(2)));

        Assert.Null(exception);
    }
}
