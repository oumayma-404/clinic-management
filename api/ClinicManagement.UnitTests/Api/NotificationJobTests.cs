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
        int maxRetries = 3)
    {
        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetPendingNotificationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(pending);
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

        return new NotificationJob(
            notifications.Object, patients.Object, uow.Object, probe.Object, settingsProvider.Object, config, senders,
            NullLogger<NotificationJob>.Instance);
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

    // [AC-9 edge] A channel enabled but not configured (NotConfigured) sends nothing and does not spam
    // Failed — the row is left Pending with no retry consumed.
    [Fact]
    public async Task NotConfigured_Channel_Leaves_Row_Pending_Without_Failing()
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

        Assert.Equal(NotificationStatus.Pending, reminder.Status);
        Assert.Equal(0, reminder.RetryCount);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        notifications.Setup(r => r.GetPendingNotificationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { reminder });
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
        var job = new NotificationJob(
            notifications.Object, patients.Object, new Mock<IUnitOfWork>().Object, probe.Object, provider.Object,
            config, new IReminderChannelSender[] { sender }, NullLogger<NotificationJob>.Instance);

        await job.ProcessPendingNotifications();

        provider.Verify(p => p.ResolveAsync(clinicId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(resolved, sender.LastSettings);
    }

    // [AC-4/AC-7] A channel disabled (per-clinic or install) after the row was enqueued must not send: the
    // dispatcher skips a row whose channel isn't in the resolved EnabledChannels, leaving it Pending (no send,
    // no Failed) — same contract as a NotConfigured channel.
    [Fact]
    public async Task Disabled_Channel_At_Dispatch_Leaves_Row_Pending_Without_Sending()
    {
        var clinicId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var reminder = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous", "Rappel : Jean le 03/01.",
            DateTime.UtcNow.AddMinutes(-1), appointmentId: Guid.NewGuid(), patientId: patientId, clinicId: clinicId);

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetPendingNotificationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { reminder });
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
        var job = new NotificationJob(
            notifications.Object, patients.Object, uow.Object, probe.Object, provider.Object,
            config, new IReminderChannelSender[] { sender }, NullLogger<NotificationJob>.Instance);

        await job.ProcessPendingNotifications();

        Assert.Equal(0, sender.Calls);
        Assert.Equal(NotificationStatus.Pending, reminder.Status);
        Assert.Equal(0, reminder.RetryCount);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
