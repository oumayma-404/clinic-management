using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Recall.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Recall;

/// <summary>
/// § 3.3 / § 6.3 — « Rappel envoyé à … » must mean a reminder was actually sent (AC-P3.1–3.11).
///
/// Two halves, and both are needed: the command must refuse when nothing could be queued (AC-P3.2), and the
/// dispatcher must undo the 30-day snooze when the queued message ultimately fails (AC-P3.5). Fixing only the
/// first would move the silent month-long suppression one step later instead of removing it, which is the
/// whole defect.
/// </summary>
public class RecallDeliveryTruthTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime SendTime = new(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ command side (AC-P3.1–3.4)

    // [AC-P3.2] No channel configured → the command FAILS, and the patient is neither stamped nor snoozed.
    // This is the original defect: the handler used to ignore the enqueue entirely and report success.
    [Theory]
    [InlineData(RecallDispatchOutcome.NoChannelConfigured)]
    [InlineData(RecallDispatchOutcome.NoDeliverablePhone)]
    [InlineData(RecallDispatchOutcome.Failed)]
    public async Task A_Recall_That_Queued_Nothing_Fails_And_Does_Not_Snooze(RecallDispatchOutcome outcome)
    {
        var patient = ReachablePatient();
        var h = new CommandHarness(patient, outcome);

        var result = await h.Handler().Handle(
            new SendRecallCommand { PatientId = patient.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(patient.RecallSnoozedUntil);
        Assert.Null(patient.LastRecallContactedAt);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-P3.2] The refusal has to be actionable — it names the reminder settings and the manual alternative,
    // otherwise the user is simply stuck where they used to get a (false) success.
    [Fact]
    public async Task The_No_Channel_Refusal_Points_At_The_Settings_And_The_Manual_Alternative()
    {
        var patient = ReachablePatient();
        var h = new CommandHarness(patient, RecallDispatchOutcome.NoChannelConfigured);

        var result = await h.Handler().Handle(
            new SendRecallCommand { PatientId = patient.Id }, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("Rappels", result.Error);
        Assert.Contains("Marquer comme contacté", result.Error);
    }

    // [AC-P3.4] A configured channel: behaviour is unchanged — stamped, snoozed 30 days, success.
    [Fact]
    public async Task An_Enqueued_Recall_Still_Stamps_And_Snoozes()
    {
        var patient = ReachablePatient();
        var h = new CommandHarness(patient, RecallDispatchOutcome.Enqueued);

        var result = await h.Handler().Handle(
            new SendRecallCommand { PatientId = patient.Id, Reason = "contrôle" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(patient.LastRecallContactedAt);
        Assert.NotNull(patient.RecallSnoozedUntil);
        Assert.True(patient.RecallSnoozedUntil > DateTime.UtcNow.AddDays(29));
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------ dispatcher side (AC-P3.5–3.8)

    // [AC-P3.5] Enqueuing is not sending. When the only channel's row reaches Failed, the snooze is undone and
    // the patient returns to the relance list, and the feed row says they must be recontacted.
    [Fact]
    public async Task A_Recall_That_Fails_On_Every_Channel_Returns_The_Patient_To_The_List()
    {
        var patient = ContactedPatient();
        var recall = RecallRow(NotificationType.SMS, patient.Id);
        var h = new JobHarness(patient, recall, batch: new[] { recall });

        await h.Job(ReminderSendResult.Transient("gateway 500"), maxRetries: 1).ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, recall.Status);
        Assert.Null(patient.RecallSnoozedUntil);
        Assert.Null(patient.LastRecallContactedAt);
        h.Generator.Verify(
            g => g.ReminderDeliveryFailedAsync(
                ClinicId, null, It.IsAny<string>(), "SMS", It.IsAny<string?>(), true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-P3.6] A partial send resolves to a STATED state: one channel really reached the patient, so the
    // snooze stands even though the other channel failed — and the feed row does not claim they need calling.
    [Fact]
    public async Task A_Partial_Send_Keeps_The_Snooze_When_One_Channel_Succeeded()
    {
        var patient = ContactedPatient();
        var failing = RecallRow(NotificationType.SMS, patient.Id);
        var sent = RecallRow(NotificationType.WhatsApp, patient.Id);
        sent.MarkAsSent();
        var h = new JobHarness(patient, failing, batch: new[] { failing, sent });

        await h.Job(ReminderSendResult.Transient("gateway 500"), maxRetries: 1).ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, failing.Status);
        Assert.NotNull(patient.RecallSnoozedUntil); // still contacted — on the channel that worked
        h.Generator.Verify(
            g => g.ReminderDeliveryFailedAsync(
                ClinicId, null, It.IsAny<string>(), "SMS", It.IsAny<string?>(), false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-P3.6] A sibling still Pending means the send has not resolved yet — a later tick decides. Undoing
    // the snooze here would put the patient back on the list while a message is still on its way.
    [Fact]
    public async Task A_Still_Pending_Sibling_Leaves_The_Decision_To_A_Later_Tick()
    {
        var patient = ContactedPatient();
        var failing = RecallRow(NotificationType.SMS, patient.Id);
        var stillPending = RecallRow(NotificationType.WhatsApp, patient.Id);
        var h = new JobHarness(patient, failing, batch: new[] { failing, stillPending });

        await h.Job(ReminderSendResult.Transient("gateway 500"), maxRetries: 1).ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, failing.Status);
        Assert.NotNull(patient.RecallSnoozedUntil);
    }

    // [AC-P3.7/3.11] The feed write is best-effort in the strict sense: a generator that throws must not abort
    // the batch, must not re-send, and must leave the row Failed.
    [Fact]
    public async Task A_Failing_Feed_Write_Never_Breaks_The_Dispatch()
    {
        var patient = ContactedPatient();
        var recall = RecallRow(NotificationType.SMS, patient.Id);
        var h = new JobHarness(patient, recall, batch: new[] { recall });
        h.Generator
            .Setup(g => g.ReminderDeliveryFailedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("feed down"));

        await h.Job(ReminderSendResult.Transient("gateway 500"), maxRetries: 1).ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, recall.Status);
    }

    // ------------------------------------------------------------------ helpers

    private static Patient ReachablePatient() =>
        new(Guid.NewGuid(), ClinicId, "Jean", "Dupont",
            new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            phoneNumber: new PhoneNumber("20123456"));

    /// <summary>A patient the « Relancer » click has already stamped + snoozed at enqueue time.</summary>
    private static Patient ContactedPatient()
    {
        var patient = ReachablePatient();
        patient.MarkRecallContacted(DateTime.UtcNow.AddDays(30), "contrôle");
        return patient;
    }

    private static Notification RecallRow(NotificationType channel, Guid patientId) =>
        new(Guid.NewGuid(), channel, "Relance patient", "Bonjour Jean…", SendTime,
            appointmentId: null, patientId: patientId, clinicId: ClinicId);

    private sealed class CommandHarness
    {
        public Mock<IUnitOfWork> Uow { get; } = new();
        private readonly Mock<IPatientRepository> _patients = new();
        private readonly Mock<IReminderScheduler> _scheduler = new();
        private readonly Mock<ICurrentClinicResolver> _resolver = new();

        public CommandHarness(Patient patient, RecallDispatchOutcome outcome)
        {
            _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
            _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            _scheduler
                .Setup(s => s.ScheduleRecallAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outcome);
        }

        public SendRecallCommandHandler Handler() =>
            new(_patients.Object, _scheduler.Object, _resolver.Object, Uow.Object);
    }

    private sealed class JobHarness
    {
        public Mock<INotificationGenerator> Generator { get; } = new();
        private readonly Mock<INotificationRepository> _notifications = new();
        private readonly Mock<IPatientRepository> _patients = new();
        private readonly Notification _due;

        public JobHarness(Patient patient, Notification due, IEnumerable<Notification> batch)
        {
            _due = due;
            _notifications.Setup(r => r.GetPendingNotificationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { due });
            _notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _notifications.Setup(r => r.GetRecallBatchAsync(patient.Id, SendTime, It.IsAny<CancellationToken>()))
                .ReturnsAsync(batch);
            _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        }

        public NotificationJob Job(ReminderSendResult result, int maxRetries)
        {
            var probe = new Mock<IInternetProbe>();
            probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var settings = new Mock<IReminderSettingsProvider>();
            settings.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = new[] { NotificationType.SMS, NotificationType.WhatsApp }
                });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reminders:MaxRetries"] = maxRetries.ToString()
                })
                .Build();

            var uow = new Mock<IUnitOfWork>();
            uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            return new NotificationJob(
                _notifications.Object, _patients.Object, new Mock<IAppointmentRepository>().Object, uow.Object,
                probe.Object, settings.Object, config,
                new IReminderChannelSender[] { new StubSender(_due.Type, result) },
                Generator.Object, NullLogger<NotificationJob>.Instance);
        }
    }

    private sealed class StubSender : IReminderChannelSender
    {
        private readonly ReminderSendResult _result;

        public StubSender(NotificationType channel, ReminderSendResult result)
        {
            Channel = channel;
            _result = result;
        }

        public NotificationType Channel { get; }

        public Task<ReminderSendResult> SendAsync(
            string phoneE164, string message, ResolvedReminderSettings settings,
            CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }
}
