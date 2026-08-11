using ClinicManagement.API.BackgroundJobs;
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

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The reminder dispatcher (spec AC-5, AC-6, AC-9): connectivity-gated, routes each due row to the sender
/// matching its channel, normalizes the phone to +216, and applies the bounded-retry lifecycle. Offline it
/// sends nothing and consumes no retry budget.
/// </summary>
public class NotificationJobTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>A configurable in-memory sender that records how it was called.</summary>
    private sealed class FakeSender : IReminderChannelSender
    {
        private readonly ReminderSendResult _result;

        public FakeSender(NotificationType channel, ReminderSendResult result)
        {
            Channel = channel;
            _result = result;
        }

        public NotificationType Channel { get; }
        public int Calls { get; private set; }
        public string? LastPhone { get; private set; }
        public ResolvedReminderSettings? LastSettings { get; private set; }

        public Task<ReminderSendResult> SendAsync(
            string phoneE164, string message, ResolvedReminderSettings settings, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPhone = phoneE164;
            LastSettings = settings;
            return Task.FromResult(_result);
        }
    }

    private static Notification Reminder(NotificationType type, Guid patientId) =>
        new(Guid.NewGuid(), type, "Rappel de rendez-vous", "Rappel : Jean le 03/01 chez Clinique Test.",
            DateTime.UtcNow.AddMinutes(-1), Guid.NewGuid(), patientId);

    private static Patient PatientWithPhone(Guid id, string phone) =>
        new(id, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            new Email("jean.dupont@example.com"), new PhoneNumber(phone));

    private static NotificationJob BuildJob(
        bool online,
        IEnumerable<Notification> pending,
        Mock<IPatientRepository> patients,
        Mock<IUnitOfWork> uow,
        IEnumerable<IReminderChannelSender> senders,
        int maxRetries = 3,
        Appointment? appointment = null)
    {
        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(pending.ToList());
        // L3a — the dispatcher reviews parked rows after the batch. Nothing here parks any, so an empty page
        // keeps these routing/lifecycle scenarios exactly as they were.
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(online);

        // The dispatcher resolves each row's clinic settings before sending; the FakeSender ignores them, so a
        // permissive stub (both channels enabled) keeps these routing/lifecycle tests focused on the job.
        var settingsProvider = new Mock<IReminderSettingsProvider>();
        settingsProvider
            .Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings
            {
                EnabledChannels = new[] { NotificationType.SMS, NotificationType.WhatsApp }
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reminders:MaxRetries"] = maxRetries.ToString() })
            .Build();

        // The dispatcher re-checks the appointment status AND (since L3b) its time at send time; return an
        // active (Scheduled) appointment so these routing/lifecycle tests behave as before. The default fixture's
        // message carries no full `dd/MM/yyyy HH:mm` moment, so the staleness check is a pass-through here.
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment ?? new Appointment(
                Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, DateTime.UtcNow, TimeSpan.FromMinutes(30)));

        return new NotificationJob(
            notifications.Object, patients.Object, appointments.Object, uow.Object, probe.Object, settingsProvider.Object, config, senders,
            new Mock<INotificationGenerator>().Object,
            SubscriptionsNotEnforced(), new Mock<IClinicSubscriptionRepository>().Object,
            // I6 wired an audit actor into every job. A permissive mock keeps these scenarios exactly as they
            // were: the job declares itself, nothing here observes it.
            new Mock<IAuditActorProvider>().Object,
            new Mock<ITenantScope>().Object,
            NullLogger<NotificationJob>.Instance);
    }

    /// <summary>
    /// The deployment kind these scenarios run on: subscriptions are not enforced, so Part G's outbox gate reads no
    /// entitlement at all and every case below behaves exactly as it did before it existed (AC-7.1/7.2). The parking
    /// itself is covered by <c>OutboxParkingTests</c>.
    /// </summary>
    private static ISubscriptionPolicy SubscriptionsNotEnforced()
    {
        var policy = new Mock<ISubscriptionPolicy>();
        policy.Setup(p => p.RequiresSubscription).Returns(false);
        return policy.Object;
    }

    // [AC-5] Offline: send nothing, leave the row Pending, and do NOT increment the retry count.
    [Fact]
    public async Task Offline_Sends_Nothing_And_Does_Not_Touch_The_Retry_Count()
    {
        var reminder = Reminder(NotificationType.SMS, Guid.NewGuid());
        var uow = new Mock<IUnitOfWork>();
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var job = BuildJob(online: false, new[] { reminder }, new Mock<IPatientRepository>(), uow,
            new IReminderChannelSender[] { sender });

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls);
        Assert.Equal(NotificationStatus.Pending, reminder.Status);
        Assert.Equal(0, reminder.RetryCount);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-6] Online + reachable patient + successful send → Sent, with the phone normalized to +216.
    [Fact]
    public async Task Online_Success_Marks_Sent_And_Normalizes_The_Phone()
    {
        var patientId = Guid.NewGuid();
        var reminder = Reminder(NotificationType.SMS, patientId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender });

        await job.ProcessPendingNotifications();

        Assert.Equal("+21620123456", sender.LastPhone);
        Assert.Equal(NotificationStatus.Sent, reminder.Status);
    }

    // [AC-6] Missing patient → Failed immediately, no send.
    [Fact]
    public async Task Missing_Patient_Marks_Failed_Without_Sending()
    {
        var reminder = Reminder(NotificationType.SMS, Guid.NewGuid());
        var patients = new Mock<IPatientRepository>(); // GetByIdAsync returns null
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender });

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls);
        Assert.Equal(NotificationStatus.Failed, reminder.Status);
    }

    // [AC-6] Empty/unparseable phone → Failed immediately, no send.
    [Fact]
    public async Task Unparseable_Phone_Marks_Failed_Without_Sending()
    {
        var patientId = Guid.NewGuid();
        var reminder = Reminder(NotificationType.SMS, patientId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "not-a-phone"));
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender });

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls);
        Assert.Equal(NotificationStatus.Failed, reminder.Status);
    }

    // [AC-6] A transient failure below the cap keeps the row Pending and increments the retry count.
    [Fact]
    public async Task Transient_Failure_Below_Cap_Stays_Pending_And_Increments_Retry()
    {
        var patientId = Guid.NewGuid();
        var reminder = Reminder(NotificationType.SMS, patientId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Transient("gateway 503"));
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender }, maxRetries: 3);

        await job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Pending, reminder.Status);
        Assert.Equal(1, reminder.RetryCount);
        Assert.Equal("gateway 503", reminder.ErrorMessage);
    }

    // [AC-6] A transient failure that reaches the cap crosses the row to Failed.
    [Fact]
    public async Task Transient_Failure_At_Cap_Marks_Failed()
    {
        var patientId = Guid.NewGuid();
        var reminder = Reminder(NotificationType.SMS, patientId);
        reminder.RecordFailedAttempt("earlier", maxRetries: 99); // RetryCount → 1 (stays Pending)
        reminder.RecordFailedAttempt("earlier", maxRetries: 99); // RetryCount → 2 (stays Pending)
        Assert.Equal(NotificationStatus.Pending, reminder.Status);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Transient("gateway 503"));
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender }, maxRetries: 3);

        await job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, reminder.Status);
        Assert.Equal(3, reminder.RetryCount);
    }

    // [AC-9 edge] A channel enabled but not configured (NotConfigured) sends nothing and does not spam Failed.
    //
    // L3a changed the resting state from Pending to **Blocked**: the row still survives and still sends once the
    // operator configures the channel (ReviewBlockedRowsAsync unblocks it), but it no longer occupies a slot at
    // the front of every oldest-first dispatch batch — which is what starved the queue for the whole install.
    [Fact]
    public async Task NotConfigured_Channel_Parks_The_Row_Without_Failing()
    {
        var patientId = Guid.NewGuid();
        var reminder = Reminder(NotificationType.SMS, patientId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));
        var uow = new Mock<IUnitOfWork>();
        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.NotConfigured);
        var job = BuildJob(true, new[] { reminder }, patients, uow, new IReminderChannelSender[] { sender });

        await job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, reminder.Status);
        // No attempt was made, so no retry budget may be consumed — a misconfiguration must not spend the
        // reminder's retries before it ever gets a chance to send.
        Assert.Equal(0, reminder.RetryCount);
        Assert.Contains("non configure", Strip(reminder.ErrorMessage));
        // The park itself is a write: the reason has to survive the tick, or the « N rappels bloqués » counter
        // and the row's explanation would both be lost.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-9] Each due row is routed to the sender matching its channel and committed on its own attempt.
    [Fact]
    public async Task Routes_Each_Row_To_Its_Channel_Sender_And_Commits_Per_Row()
    {
        var smsPatientId = Guid.NewGuid();
        var waPatientId = Guid.NewGuid();
        var smsReminder = Reminder(NotificationType.SMS, smsPatientId);
        var waReminder = Reminder(NotificationType.WhatsApp, waPatientId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(smsPatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(smsPatientId, "20123456"));
        patients.Setup(r => r.GetByIdAsync(waPatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(waPatientId, "20999888"));
        var sms = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var whatsApp = new FakeSender(NotificationType.WhatsApp, ReminderSendResult.Sent);
        var uow = new Mock<IUnitOfWork>();
        var job = BuildJob(true, new[] { smsReminder, waReminder }, patients, uow,
            new IReminderChannelSender[] { sms, whatsApp });

        await job.ProcessPendingNotifications();

        Assert.Equal(1, sms.Calls);
        Assert.Equal(1, whatsApp.Calls);
        Assert.Equal(NotificationStatus.Sent, smsReminder.Status);
        Assert.Equal(NotificationStatus.Sent, waReminder.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2)); // one commit per row
    }

    // [AC-5] The dispatcher resolves the effective settings for the row's own ClinicId and hands them to the
    // sender (so each clinic sends under its own resolved identity/credentials).
    [Fact]
    public async Task Resolves_Settings_For_The_Rows_Clinic_And_Passes_Them_To_The_Sender()
    {
        var clinicId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var reminder = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous", "Rappel : Jean le 03/01.",
            DateTime.UtcNow.AddMinutes(-1), appointmentId: Guid.NewGuid(), patientId: patientId, clinicId: clinicId);

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { reminder });
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var resolved = new ResolvedReminderSettings
        {
            EnabledChannels = new[] { NotificationType.SMS },
            SmsSenderId = "ClinicSms",
        };
        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(resolved);

        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow, TimeSpan.FromMinutes(30)));
        var job = new NotificationJob(
            notifications.Object, patients.Object, appointments.Object, new Mock<IUnitOfWork>().Object, probe.Object, provider.Object,
            config, new IReminderChannelSender[] { sender }, new Mock<INotificationGenerator>().Object,
            SubscriptionsNotEnforced(), new Mock<IClinicSubscriptionRepository>().Object,
            // I6: permissive audit-actor mock — see the shared builder above.
            new Mock<IAuditActorProvider>().Object,
            new Mock<ITenantScope>().Object,
            NullLogger<NotificationJob>.Instance);

        await job.ProcessPendingNotifications();

        provider.Verify(p => p.ResolveAsync(clinicId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(resolved, sender.LastSettings);
    }

    // [AC-4/AC-7] A channel disabled (per-clinic or install) after the row was enqueued must not send: the
    // dispatcher parks a row whose channel isn't in the resolved EnabledChannels (no send, no Failed) — same
    // contract as a NotConfigured channel, and since L3a the same **Blocked** resting state.
    [Fact]
    public async Task Disabled_Channel_At_Dispatch_Parks_The_Row_Without_Sending()
    {
        var clinicId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var reminder = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous", "Rappel : Jean le 03/01.",
            DateTime.UtcNow.AddMinutes(-1), appointmentId: Guid.NewGuid(), patientId: patientId, clinicId: clinicId);

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { reminder });
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // SMS row, but the clinic now has only WhatsApp enabled → the SMS row must be skipped.
        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings { EnabledChannels = new[] { NotificationType.WhatsApp } });

        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var uow = new Mock<IUnitOfWork>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow, TimeSpan.FromMinutes(30)));
        var job = new NotificationJob(
            notifications.Object, patients.Object, appointments.Object, uow.Object, probe.Object, provider.Object,
            config, new IReminderChannelSender[] { sender }, new Mock<INotificationGenerator>().Object,
            SubscriptionsNotEnforced(), new Mock<IClinicSubscriptionRepository>().Object,
            // I6: permissive audit-actor mock — see the shared builder above.
            new Mock<IAuditActorProvider>().Object,
            new Mock<ITenantScope>().Object,
            NullLogger<NotificationJob>.Instance);

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls);
        Assert.Equal(NotificationStatus.Blocked, reminder.Status);
        Assert.Equal(0, reminder.RetryCount);
        Assert.Contains("desactive", Strip(reminder.ErrorMessage));
    }

    // fix-appointment-lifecycle #11: a reminder whose appointment is cancelled by dispatch time must not be
    // sent, even though its Pending row is still due (safety net for a void failure / cancel-vs-tick race).
    [Fact]
    public async Task Cancelled_Appointment_At_Dispatch_Is_Not_Sent()
    {
        var patientId = Guid.NewGuid();
        var reminder = Reminder(NotificationType.SMS, patientId);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { reminder });
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var settingsProvider = new Mock<IReminderSettingsProvider>();
        settingsProvider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings
            {
                EnabledChannels = new[] { NotificationType.SMS, NotificationType.WhatsApp }
            });

        // The reminder's appointment has since been cancelled.
        var cancelled = new Appointment(Guid.NewGuid(), ClinicId, patientId, null, DateTime.UtcNow, TimeSpan.FromMinutes(30));
        cancelled.Cancel();
        var appointments = new Mock<IAppointmentRepository>();
        appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(cancelled);

        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var job = new NotificationJob(
            notifications.Object, patients.Object, appointments.Object, uow.Object, probe.Object,
            settingsProvider.Object, config, new IReminderChannelSender[] { sender },
            new Mock<INotificationGenerator>().Object,
            SubscriptionsNotEnforced(), new Mock<IClinicSubscriptionRepository>().Object,
            // I6 wired an audit actor into every job. A permissive mock keeps these scenarios exactly as they
            // were: the job declares itself, nothing here observes it.
            new Mock<IAuditActorProvider>().Object,
            new Mock<ITenantScope>().Object,
            NullLogger<NotificationJob>.Instance);

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls); // never sent for a cancelled visit
        Assert.Equal(NotificationStatus.Failed, reminder.Status); // dropped terminally, not left Pending to retry
    }

    /*
     * -- L3 -----------------------------------------------------------------------------------------------------
     * The two defects the dispatcher itself had to close: a queue that starves silently, and a reminder that
     * announces the wrong day.
     */

    // L3b - a reminder whose appointment has MOVED must not send. The status re-check above cannot see this: a
    // moved appointment is still perfectly active. The body and ScheduledFor are frozen at enqueue, so any writer
    // that reschedules without re-enqueuing leaves a row stating the old moment.
    [Fact]
    public async Task A_Reminder_Announcing_The_Old_Day_Is_Not_Sent()
    {
        var patientId = Guid.NewGuid();
        var appointmentAt = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

        // The body says what the scheduler would have written for the ORIGINAL time...
        var reminder = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous",
            $"Rappel : Jean, vous avez un rendez-vous le {ReminderMessage.FormatAppointmentMoment(appointmentAt)} chez Clinique Test.",
            DateTime.UtcNow.AddMinutes(-1), appointmentId: Guid.NewGuid(), patientId: patientId, clinicId: ClinicId);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));

        // ...while the appointment has since moved two days later.
        var moved = new Appointment(
            Guid.NewGuid(), ClinicId, patientId, null, appointmentAt.AddDays(2), TimeSpan.FromMinutes(30));

        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender }, appointment: moved);

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls);
        // Failed, not Blocked: nothing an operator configures makes a stale body true, and unlike the cancelled
        // case the patient IS still expected - so this must be visible rather than parked quietly.
        Assert.Equal(NotificationStatus.Failed, reminder.Status);
        Assert.Contains("deplace", Strip(reminder.ErrorMessage));
    }

    // L3b - the same check must NOT fire on a reminder that is still accurate, or the backstop would silently
    // suppress every reminder in the product.
    [Fact]
    public async Task A_Reminder_Still_Naming_The_Right_Moment_Sends_Normally()
    {
        var patientId = Guid.NewGuid();
        var appointmentAt = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

        var reminder = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous",
            $"Rappel : Jean, vous avez un rendez-vous le {ReminderMessage.FormatAppointmentMoment(appointmentAt)} chez Clinique Test.",
            DateTime.UtcNow.AddMinutes(-1), appointmentId: Guid.NewGuid(), patientId: patientId, clinicId: ClinicId);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PatientWithPhone(patientId, "20123456"));

        var unchanged = new Appointment(
            Guid.NewGuid(), ClinicId, patientId, null, appointmentAt, TimeSpan.FromMinutes(30));

        var sender = new FakeSender(NotificationType.SMS, ReminderSendResult.Sent);
        var job = BuildJob(true, new[] { reminder }, patients, new Mock<IUnitOfWork>(),
            new IReminderChannelSender[] { sender }, appointment: unchanged);

        await job.ProcessPendingNotifications();

        Assert.Equal(1, sender.Calls);
        Assert.Equal(NotificationStatus.Sent, reminder.Status);
    }

    // L3a - a parked row returns to the queue once its channel can send again. Without this the Blocked status
    // would be a one-way door and "it sends once the operator configures the channel" - the behaviour the
    // original Pending-forever comment was protecting - would have been broken by the fix.
    [Fact]
    public async Task A_Blocked_Row_Is_Returned_To_The_Queue_When_Its_Channel_Becomes_Sendable()
    {
        var blocked = Reminder(NotificationType.SMS, Guid.NewGuid());
        blocked.MarkAsBlocked(OutboxBlockReason.ChannelUnconfigured, "Canal non configure");

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { blocked });
        notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // SMS is now enabled AND fully configured - the same `SmsConfigured` predicate the sender uses.
        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings
            {
                EnabledChannels = new[] { NotificationType.SMS },
                SmsApiUrl = "https://gateway.example/send",
                SmsSenderId = "Clinique",
                SmsApiKey = "k",
            });

        var job = BuildJobWith(notifications, new Mock<IPatientRepository>(), new Mock<IUnitOfWork>(), probe,
            provider, new IReminderChannelSender[] { new FakeSender(NotificationType.SMS, ReminderSendResult.Sent) });

        await job.ProcessPendingNotifications();

        // Back to Pending - the NEXT tick sends it. Unblocking is not sending: doing both in one pass would put
        // the row through the sender inside a housekeeping loop.
        Assert.Equal(NotificationStatus.Pending, blocked.Status);
        Assert.Null(blocked.ErrorMessage);
        Assert.Null(blocked.BlockedReason);
    }

    // L3a - a parked row whose channel is STILL unsendable stays parked, so the review pass cannot turn into a
    // per-minute cycle of unblock-then-reblock (which would be the starvation defect wearing a new status).
    [Fact]
    public async Task A_Blocked_Row_Stays_Blocked_While_Its_Channel_Cannot_Send()
    {
        var blocked = Reminder(NotificationType.SMS, Guid.NewGuid());
        blocked.MarkAsBlocked(OutboxBlockReason.ChannelUnconfigured, "Canal non configure");

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { blocked });

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Enabled, but with no credentials - exactly the state that parked it.
        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings { EnabledChannels = new[] { NotificationType.SMS } });

        var uow = new Mock<IUnitOfWork>();
        var job = BuildJobWith(notifications, new Mock<IPatientRepository>(), uow, probe, provider,
            new IReminderChannelSender[] { new FakeSender(NotificationType.SMS, ReminderSendResult.Sent) });

        await job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, blocked.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // L3a - the due scan is asked for a per-clinic bound, not just a batch size. Asserting the *argument* is the
    // only thing a mocked repository can prove here; the fair-share arithmetic itself lives in the repository.
    [Fact]
    public async Task The_Due_Scan_Is_Bounded_Per_Clinic_As_Well_As_Per_Batch()
    {
        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetDueForDispatchAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());
        notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Notification>());

        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings { EnabledChannels = Array.Empty<NotificationType>() });

        var job = BuildJobWith(notifications, new Mock<IPatientRepository>(), new Mock<IUnitOfWork>(), probe,
            provider, Array.Empty<IReminderChannelSender>());

        await job.ProcessPendingNotifications();

        notifications.Verify(
            r => r.GetDueForDispatchAsync(
                It.Is<int>(b => b > 0), It.Is<int>(perClinic => perClinic > 0), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The long-hand builder, for the scenarios that need to control the repository mock itself. Shares the
    /// permissive audit-actor and empty-config decisions with <c>BuildJob</c> so a constructor change is one edit.
    /// </summary>
    private static NotificationJob BuildJobWith(
        Mock<INotificationRepository> notifications,
        Mock<IPatientRepository> patients,
        Mock<IUnitOfWork> uow,
        Mock<IInternetProbe> probe,
        Mock<IReminderSettingsProvider> settingsProvider,
        IEnumerable<IReminderChannelSender> senders)
    {
        var appointments = new Mock<IAppointmentRepository>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        return new NotificationJob(
            notifications.Object, patients.Object, appointments.Object, uow.Object, probe.Object,
            settingsProvider.Object, config, senders, new Mock<INotificationGenerator>().Object,
            SubscriptionsNotEnforced(), new Mock<IClinicSubscriptionRepository>().Object,
            new Mock<IAuditActorProvider>().Object, new Mock<ITenantScope>().Object,
            NullLogger<NotificationJob>.Instance);
    }

    /// <summary>
    /// Accent-insensitive fold, so an assertion about a French reason does not depend on how this source file
    /// happens to be encoded.
    /// </summary>
    private static string Strip(string? value) =>
        string.Concat((value ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark));
}
