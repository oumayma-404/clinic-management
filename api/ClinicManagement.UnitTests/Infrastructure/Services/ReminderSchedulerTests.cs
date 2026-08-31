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
        public Mock<IVendorMessagingAvailability> MessagingAvailability { get; } = new();
        public Mock<IMessagingAllowanceRepository> Allowances { get; } = new();
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

            // ⚠️ L3c made the enqueue read the appointment's existing rows for its (channel, tier) idempotency
            // check, so this stub is no longer optional. Unstubbed, Moq hands back a completed task carrying
            // `null`, the `.Where(...)` inside throws, and the scheduler's own swallow-and-log wrapper turns that
            // into « nothing was enqueued » with no visible cause — the same trap this class already documents for
            // `ResolveAsync`. `HasExistingReminders` overrides it per test.
            Notifications.Setup(r => r.GetByAppointmentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Notification>());
            Clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Clinic(ClinicId, "Clinique Test"));

            // These fixtures are about enqueue on a deployment that does NOT sell vendor messaging, which is the
            // default and leaves the recall path byte-for-byte as it was (EC-16). The forfait's own refusal is
            // covered by RecallMessagingRefusalTests, which switches this on.
            MessagingAvailability.SetupGet(a => a.SellsVendorMessaging).Returns(false);

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
            // Credentials are filled in because a channel a fixture "enables" is meant to represent a WORKING
            // channel: the recall path enqueues only on a *sendable* channel (`SmsConfigured`/
            // `WhatsAppConfigured`), since a toggled-on-but-unconfigured channel leaves its row Pending for
            // ever and would keep the patient snoozed 30 days with nothing that can resolve (AC-P3.2).
            SettingsProvider
                .Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = ParseChannels(channels),
                    LeadTimeHours = new[] { 24, 6 },
                    SmsApiUrl = "https://sms.example/send",
                    SmsSenderId = "Clinique",
                    SmsApiKey = "k",
                    WhatsAppApiUrl = "https://graph.example/v20.0",
                    WhatsAppPhoneNumberId = "1",
                    WhatsAppTemplateName = "rappel",
                    WhatsAppAccessToken = "t",
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

        /// <summary>
        /// A patient with a perfectly good phone who has told the cabinet not to send automated messages. The
        /// number is deliberately valid: the whole point is that consent, not deliverability, is what stops it.
        /// </summary>
        public void PatientRefusedReminders(Guid patientId)
        {
            var patient = ReachablePatient(patientId);
            patient.SetReminderConsent(
                PatientReminderConsent.Refused, new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), "reception@cabinet.tn");

            Patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(patient);
        }

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
                // Quiet hours OFF for these fixtures (equal bounds is the documented way to disable the floor).
                // These tests are about ENQUEUE - one row per channel per tier, the idempotency key, the
                // sendability gate - and they build their appointment from DateTime.UtcNow, so with the shipped
                // 21:00-08:00 default a tier would land inside the window at some hours of the day and be pulled
                // back, making the suite pass or fail depending on when it runs. The floor itself is covered by
                // ReminderScheduleTests, where the instants are fixed.
                ["Reminders:QuietHoursStartLocal"] = "0",
                ["Reminders:QuietHoursEndLocal"] = "0",
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
            new(Notifications.Object, Clinics.Object, Patients.Object, SettingsProvider.Object,
                MessagingAvailability.Object, Allowances.Object, Uow.Object,
                Config(), NullLogger<ReminderScheduler>.Instance);
    }

    private static Notification PendingReminder(Guid appointmentId, NotificationType type = NotificationType.SMS) =>
        new(Guid.NewGuid(), type, "Rappel de rendez-vous", "…", DateTime.UtcNow.AddHours(1), appointmentId, Guid.NewGuid());

    // [AC-1] Booking enqueues one Pending reminder per channel at the computed send time, with the rendered
    // French message (patient name + clinic name) and the appointment/patient links.
    [Fact]
    /// <summary>
    /// L3c — one row per <b>(channel × future tier)</b>, not per channel.
    ///
    /// <para>This test used to assert « 2 rows, both at the largest future tier ». That was a faithful pin of the
    /// old contract and of the defect inside it: <c>ComputeSendTimeUtc</c> returned a single instant, so with
    /// « 24, 6 » configured — which the settings screen invites, placeholder and all — the 6 h nudge was silently
    /// discarded. For a no-show problem that is the one that works.</para>
    /// </summary>
    public async Task Schedule_Enqueues_One_Pending_Per_Channel_And_Tier()
    {
        var h = new Harness("Sms", "WhatsApp");
        var appt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc);
        var appointmentId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, appointmentId, patientId, "Jean Dupont", appt);

        // Two channels × the two future tiers the harness configures (24 h and 6 h).
        Assert.Equal(4, h.Added.Count);
        foreach (var channel in new[] { NotificationType.SMS, NotificationType.WhatsApp })
        {
            var forChannel = h.Added.Where(n => n.Type == channel).ToList();
            Assert.Equal(2, forChannel.Count);
            Assert.Contains(forChannel, n => Close(n.ScheduledFor, appt.AddHours(-24)));
            Assert.Contains(forChannel, n => Close(n.ScheduledFor, appt.AddHours(-6)));
        }

        Assert.All(h.Added, n =>
        {
            Assert.Equal(NotificationStatus.Pending, n.Status);
            Assert.Equal(appointmentId, n.AppointmentId);
            Assert.Equal(patientId, n.PatientId);
            Assert.Contains("Jean Dupont", n.Message);
            Assert.Contains("Clinique Test", n.Message);
        });
    }

    /// <summary>
    /// L3c idempotency, on <b>(appointment, channel, tier)</b> — and the tier's identity on the wire IS its send
    /// instant. Without it the minutely dispatcher double-sends every tier the moment any path enqueues twice
    /// (a second update, a Google-side move racing an in-app one).
    /// </summary>
    [Fact]
    public async Task Scheduling_Twice_Adds_Nothing_The_Second_Time()
    {
        var h = new Harness("Sms");
        var appointmentId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var appt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc);

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, appointmentId, patientId, "Jean", appt);
        var afterFirst = h.Added.Count;
        h.HasExistingReminders(appointmentId, h.Added.ToArray());

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, appointmentId, patientId, "Jean", appt);

        Assert.Equal(afterFirst, h.Added.Count);
    }

    /// <summary>
    /// L3a — sendability is checked at <b>enqueue</b> on the appointment path now, not only on the recall path.
    /// A channel toggled on with no credentials produces a row that can never resolve, and an unresolvable row at
    /// the front of an oldest-first, batch-capped due scan starves the queue for the whole install.
    /// </summary>
    [Fact]
    public async Task An_Enabled_But_Unconfigured_Channel_Enqueues_Nothing()
    {
        var h = new Harness("Sms");
        // Enabled, but with the credentials stripped — the state that used to produce a permanently Pending row.
        h.SettingsProvider
            .Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings
            {
                EnabledChannels = new[] { NotificationType.SMS },
                LeadTimeHours = new[] { 24 },
            });

        await h.Scheduler().ScheduleForAppointmentAsync(
            ClinicId, Guid.NewGuid(), Guid.NewGuid(), "Jean",
            DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc));

        Assert.Empty(h.Added);
    }

    /// <summary>Tolerance helper: the fixtures are built from <c>DateTime.UtcNow</c>, so the instants drift by ms.</summary>
    private static bool Close(DateTime actual, DateTime expected) =>
        (actual - expected).Duration() < TimeSpan.FromSeconds(1);

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
        // L3c — one row per future tier (24 h and 6 h), where this used to expect exactly one.
        Assert.Equal(2, h.Added.Count);
        Assert.Contains(h.Added, n => Close(n.ScheduledFor, newAppt.AddHours(-24)));
        Assert.Contains(h.Added, n => Close(n.ScheduledFor, newAppt.AddHours(-6)));

        // ⚠️ And the re-enqueue is NOT suppressed by the dedup read. `RemoveAsync` only *stages* the delete, so
        // the read still returns the voided row; the scheduler threads the voided ids through so the dedup cannot
        // skip re-creating exactly the reminders this reschedule exists to replace.
        Assert.DoesNotContain(h.Added, n => n.Id == oldPending.Id);
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

        var outcome = await h.Scheduler().ScheduleRecallAsync(ClinicId, patientId, "Sans Téléphone", "contrôle");

        Assert.Empty(h.Added);
        // [AC-P3.1] and it says so, rather than returning void and letting the caller assume a send.
        Assert.Equal(RecallDispatchOutcome.NoDeliverablePhone, outcome);
    }

    /// <summary>
    /// ⚠️ <b>A patient who refused gets nothing on the appointment path.</b> Recording a phone number used to
    /// enrol somebody into reminders with no way out for the patient or the cabinet; this is the way out, and
    /// this fixture is what proves it is wired to <i>this</i> path and not only to the relance below.
    ///
    /// <para>The phone is valid throughout. If this ever fails while the phone-less fixtures pass, the consent
    /// check has been dropped and the deliverability check is carrying the test.</para>
    /// </summary>
    [Fact]
    public async Task Booking_Enqueues_Nothing_For_A_Patient_Who_Refused_Reminders()
    {
        var h = new Harness("Sms", "WhatsApp");
        var patientId = Guid.NewGuid();
        h.PatientRefusedReminders(patientId);
        var appt = DateTime.UtcNow.AddDays(3);

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, Guid.NewGuid(), patientId, "Jean Dupont", appt);

        Assert.Empty(h.Added);
    }

    /// <summary>
    /// The same refusal on the relance path — and it is reported as <b>its own outcome</b>, not as a missing
    /// phone. That distinction is the whole point: « numéro invalide » sends reception off to correct a number,
    /// and correcting the number is exactly what must not put the message back in the queue.
    /// </summary>
    [Fact]
    public async Task Recall_Refuses_And_Names_Consent_Rather_Than_The_Phone()
    {
        var h = new Harness("Sms");
        var patientId = Guid.NewGuid();
        h.PatientRefusedReminders(patientId);

        var outcome = await h.Scheduler().ScheduleRecallAsync(ClinicId, patientId, "Jean Dupont", "contrôle");

        Assert.Empty(h.Added);
        Assert.Equal(RecallDispatchOutcome.ReminderConsentRefused, outcome);
        Assert.NotEqual(RecallDispatchOutcome.NoDeliverablePhone, outcome);
    }

    /// <summary>
    /// The grandfathering decision, asserted rather than assumed. Every patient recorded before this column
    /// existed is <c>NotRecorded</c>, and treating that as a refusal would have silently muted every reminder
    /// in every cabinet on the day it shipped. If somebody later decides consent must be explicit, this is the
    /// test that has to be changed deliberately — which is the point of writing it down.
    /// </summary>
    [Fact]
    public async Task A_Patient_Nobody_Has_Asked_Yet_Still_Receives_Reminders()
    {
        var h = new Harness("Sms");
        var appt = DateTime.UtcNow.AddDays(3);

        await h.Scheduler().ScheduleForAppointmentAsync(ClinicId, Guid.NewGuid(), Guid.NewGuid(), "Jean Dupont", appt);

        Assert.NotEmpty(h.Added);
    }

    // [AC-P3.1/AC-P3.2] No channel enabled: nothing is enqueued AND the caller is told which of the two
    // "nothing happened" cases it was, because the fix differs — configure a channel vs. fix a phone number.
    [Fact]
    public async Task Recall_Reports_No_Channel_Configured_When_The_Clinic_Has_None()
    {
        var h = new Harness(); // no channels at all — the defect's original trigger
        var patientId = Guid.NewGuid();

        var outcome = await h.Scheduler().ScheduleRecallAsync(ClinicId, patientId, "Jean Dupont", "contrôle");

        Assert.Empty(h.Added);
        Assert.Equal(RecallDispatchOutcome.NoChannelConfigured, outcome);
    }

    // [AC-P3.1/AC-P3.4] The happy path still enqueues one row per channel, and reports Enqueued so the
    // command may stamp « contacté » and snooze.
    [Fact]
    public async Task Recall_Reports_Enqueued_And_Adds_One_Row_Per_Channel()
    {
        var h = new Harness("Sms", "WhatsApp");
        var patientId = Guid.NewGuid();

        var outcome = await h.Scheduler().ScheduleRecallAsync(ClinicId, patientId, "Jean Dupont", "contrôle");

        Assert.Equal(RecallDispatchOutcome.Enqueued, outcome);
        Assert.Equal(2, h.Added.Count);
        // Every row of one send shares the same ScheduledFor — that is what lets the dispatcher recognise
        // the batch and decide the patient's state only once all channels have resolved (AC-P3.6).
        Assert.Single(h.Added.Select(n => n.ScheduledFor).Distinct());
        Assert.All(h.Added, n => Assert.Null(n.AppointmentId));
    }
}
