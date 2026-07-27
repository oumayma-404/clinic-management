using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
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
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IReminderSettingsProvider> SettingsProvider { get; } = new();
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

            // Every patient in these fixtures is reachable unless a test says otherwise — the scheduler now
            // gates enqueue on a deliverable phone.
            Patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => ReachablePatient(id));

            // The scheduler resolves the enabled channels through the provider (per-clinic override or
            // per-install default). Mirror the configured channels so these enqueue tests stay focused.
            //
            // BOTH provider methods are stubbed on purpose. The scheduler reads EnabledChannels off the FULL
            // ResolveAsync result; stubbing only ResolveEnabledChannelsAsync left ResolveAsync returning null,
            // which threw inside the class's own swallow-and-log wrapper — so three of these tests failed with
            // an empty collection and no visible cause.
            SettingsProvider
                .Setup(p => p.ResolveEnabledChannelsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ParseChannels(channels));
            SettingsProvider
                .Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = ParseChannels(channels),
                    LeadTimeHours = new[] { 24, 6 },
                });
        }

        private static IReadOnlyList<NotificationType> ParseChannels(string[] channels)
        {
            var result = new List<NotificationType>();
            foreach (var channel in channels)
            {
                if (string.Equals(channel, "Sms", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(NotificationType.SMS);
                }
                else if (string.Equals(channel, "WhatsApp", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(NotificationType.WhatsApp);
                }
            }

            return result;
        }

        /// <summary>This patient cannot be reached — the scheduler must not enqueue anything for them.</summary>
        public void PatientHasNoPhone(Guid patientId) =>
            Patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Patient(
                    patientId, ClinicId, "Sans", "Téléphone",
                    new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));

        private static Patient ReachablePatient(Guid patientId) =>
            new(patientId, ClinicId, "Jean", "Dupont",
                new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                phoneNumber: new PhoneNumber("+21620123456"));

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
            new(Notifications.Object, Clinics.Object, Patients.Object, SettingsProvider.Object, Uow.Object,
                Config(), NullLogger<ReminderScheduler>.Instance);
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

    // [AC-4] Each enqueued reminder records the owning clinic id so the dispatcher can later resolve that
    // clinic's channel credentials at send time.
    [Fact]
    public async Task Schedule_Stamps_The_ClinicId_On_Each_Reminder()
    {
        var h = new Harness("Sms", "WhatsApp");
        var appt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc);

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, Guid.NewGuid(), Guid.NewGuid(), "Jean Dupont", appt);

        Assert.NotEmpty(h.Added);
        Assert.All(h.Added, n => Assert.Equal(ClinicId, n.ClinicId));
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

    // [AC-52] No row is enqueued for a patient who cannot be reached. Gating at enqueue rather than at
    // dispatch is the point: a queued-then-failed reminder is noise an operator has to triage, repeatedly,
    // for a patient whose phone number does not exist.
    [Fact]
    public async Task Schedule_Enqueues_Nothing_For_A_Patient_Without_A_Phone()
    {
        var h = new Harness("Sms", "WhatsApp");
        var patientId = Guid.NewGuid();
        h.PatientHasNoPhone(patientId);

        await h.Scheduler().ScheduleForAppointmentAsync(
            ClinicId, Guid.NewGuid(), patientId, "Sans Téléphone",
            DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc));

        Assert.Empty(h.Added);
    }

    // [AC-52] Same gate on the relance path.
    [Fact]
    public async Task Recall_Enqueues_Nothing_For_A_Patient_Without_A_Phone()
    {
        var h = new Harness("Sms");
        var patientId = Guid.NewGuid();
        h.PatientHasNoPhone(patientId);

        await h.Scheduler().ScheduleRecallAsync(ClinicId, patientId, "Sans Téléphone", "contrôle");

        Assert.Empty(h.Added);
    }
}
