using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Features.Recall.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// US-5 — a manual « Relancer » is refused honestly when the WhatsApp forfait is spent
/// (<c>vendor-whatsapp-messaging-quota</c> AC-5.1–5.4).
///
/// <para>Two halves, because the decision lives in two places and neither can see the other. The <b>scheduler</b>
/// decides whether anything is queued at all (AC-5.1/5.3), and the <b>command</b> decides what the user is told and
/// whether the patient is touched (AC-5.2/5.4). A test of one alone would pass against a broken other.</para>
/// </summary>
public class RecallMessagingRefusalTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ---- The scheduler: is anything queued? --------------------------------------------------------

    /// <summary>
    /// AC-5.1 — WhatsApp is the only sendable channel and the forfait is spent, so <b>nothing is enqueued</b> and the
    /// outcome names the forfait. Queuing a row here would leave the patient snoozed for 30 days behind a message
    /// nobody could send this month.
    /// </summary>
    [Fact]
    public async Task A_WhatsApp_Only_Cabinet_With_A_Spent_Forfait_Is_Refused_And_Queues_Nothing()
    {
        var harness = new SchedulerHarness(exhausted: true, "WhatsApp");

        var outcome = await harness.ScheduleRecall();

        Assert.Equal(RecallDispatchOutcome.MessagingAllowanceExhausted, outcome);
        Assert.Empty(harness.Added);
    }

    /// <summary>
    /// AC-5.3 — SMS is <b>also</b> sendable, so the relance succeeds and <b>both</b> rows are queued. A channel being
    /// exhausted is not the same as having no channel; the WhatsApp row is held at dispatch by the gate, and the SMS
    /// one goes out normally (AC-4.6).
    /// </summary>
    [Fact]
    public async Task With_Sms_Also_Sendable_The_Relance_Succeeds_And_Both_Rows_Are_Queued()
    {
        var harness = new SchedulerHarness(exhausted: true, "Sms", "WhatsApp");

        var outcome = await harness.ScheduleRecall();

        Assert.Equal(RecallDispatchOutcome.Enqueued, outcome);
        Assert.Equal(2, harness.Added.Count);
        Assert.Contains(harness.Added, n => n.Type == NotificationType.SMS);
        Assert.Contains(harness.Added, n => n.Type == NotificationType.WhatsApp);
    }

    /// <summary>A forfait with room left changes nothing at all: the WhatsApp row is queued as before.</summary>
    [Fact]
    public async Task A_Forfait_With_Room_Left_Queues_Normally()
    {
        var harness = new SchedulerHarness(exhausted: false, "WhatsApp");

        var outcome = await harness.ScheduleRecall();

        Assert.Equal(RecallDispatchOutcome.Enqueued, outcome);
        Assert.Single(harness.Added);
    }

    /// <summary>
    /// AC-4.3 — a cabinet with <b>no</b> counting row is <b>not</b> refused here, and that is the opposite of what the
    /// dispatch gate does with the same cabinet.
    ///
    /// <para>The row is enqueued and then parked by the gate under <c>MessagingAllowanceMissing</c>, with its own
    /// sentence. Refusing at enqueue instead would tell a practice its forfait is « épuisé » when our own bookkeeping
    /// is what is missing — the exact conflation AC-4.3 exists to prevent.</para>
    /// </summary>
    [Fact]
    public async Task A_Cabinet_With_No_Counting_Row_Is_Not_Refused_As_Exhausted()
    {
        var harness = new SchedulerHarness(exhausted: null, "WhatsApp");

        var outcome = await harness.ScheduleRecall();

        Assert.Equal(RecallDispatchOutcome.Enqueued, outcome);
        Assert.Single(harness.Added);
    }

    /// <summary>
    /// EC-16 — where the deployment does not sell vendor messaging the forfait is never consulted, so the recall path
    /// is byte-for-byte what it was.
    /// </summary>
    [Fact]
    public async Task The_Forfait_Is_Never_Read_Where_The_Deployment_Does_Not_Sell_Vendor_Messaging()
    {
        var harness = new SchedulerHarness(exhausted: true, sellsVendorMessaging: false, channels: new[] { "WhatsApp" });

        var outcome = await harness.ScheduleRecall();

        Assert.Equal(RecallDispatchOutcome.Enqueued, outcome);
        harness.Allowances.Verify(
            a => a.GetMonthAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---- The command: what is the user told, and is the patient touched? ---------------------------

    /// <summary>
    /// AC-5.1/5.4 — the refusal carries the forfait's <b>own</b> sentence from <c>MessagingRefusals</c>, naming the
    /// cause and « Marquer comme contacté ». It is deliberately <b>not</b> the no-channel sentence, which would tell
    /// the practice to configure a channel it has already configured — advice it cannot act on.
    /// </summary>
    [Fact]
    public async Task The_Refusal_Names_The_Forfait_And_Not_A_Missing_Channel()
    {
        var harness = new CommandHarness(RecallDispatchOutcome.MessagingAllowanceExhausted);

        var result = await harness.Send();

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingRefusals.RecallExhausted, result.Error);
        Assert.Contains("Marquer comme contacté", result.Error);
        Assert.DoesNotContain("Paramètres", result.Error!);
    }

    /// <summary>
    /// AC-5.2 — the patient is left <b>exactly</b> as they were: still on the relance list, not snoozed, not marked
    /// contacted. The whole point of the refusal is that nobody was reached.
    /// </summary>
    [Fact]
    public async Task The_Patient_Is_Left_Untouched()
    {
        var harness = new CommandHarness(RecallDispatchOutcome.MessagingAllowanceExhausted);

        await harness.Send();

        Assert.Null(harness.Patient.RecallSnoozedUntil);
        Assert.Null(harness.Patient.LastRecallContactedAt);
        harness.Patients.Verify(
            p => p.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// AC-5.4 — the two refusals are <b>different sentences</b>. A shared one would hide « your forfait is spent »
    /// behind advice about a channel that is already configured.
    /// </summary>
    [Fact]
    public async Task The_Forfait_Refusal_Reads_Differently_From_The_No_Channel_Refusal()
    {
        var forfait = await new CommandHarness(RecallDispatchOutcome.MessagingAllowanceExhausted).Send();
        var noChannel = await new CommandHarness(RecallDispatchOutcome.NoChannelConfigured).Send();

        Assert.True(forfait.IsFailure);
        Assert.True(noChannel.IsFailure);
        Assert.NotEqual(noChannel.Error, forfait.Error);
    }

    // ---- Harnesses ---------------------------------------------------------------------------------

    private sealed class SchedulerHarness
    {
        public List<Notification> Added { get; } = new();
        public Mock<IMessagingAllowanceRepository> Allowances { get; } = new();

        private readonly ReminderScheduler _scheduler;

        /// <param name="exhausted">
        /// True/false for a counting row with/without room; <b>null</b> for a cabinet that has no row at all.
        /// </param>
        public SchedulerHarness(bool? exhausted, params string[] channels)
            : this(exhausted, sellsVendorMessaging: true, channels)
        {
        }

        public SchedulerHarness(bool? exhausted, bool sellsVendorMessaging, string[] channels)
        {
            var notifications = new Mock<INotificationRepository>();
            notifications.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                .Callback<Notification, CancellationToken>((n, _) => Added.Add(n))
                .ReturnsAsync((Notification n, CancellationToken _) => n);

            var clinics = new Mock<IClinicRepository>();
            clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Clinic(ClinicId, "Clinique Test"));

            var patients = new Mock<IPatientRepository>();
            patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Patient(
                    PatientId, ClinicId, "Jean", "Dupont",
                    new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                    phoneNumber: new PhoneNumber("+21620123456")));

            var enabled = ParseChannels(channels);
            var settings = new Mock<IReminderSettingsProvider>();
            settings.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = enabled,
                    LeadTimeHours = new[] { 24 },
                    SmsApiUrl = "https://sms.example/send",
                    SmsSenderId = "Clinique",
                    SmsApiKey = "k",
                    WhatsAppApiUrl = "https://graph.example/v21.0",
                    WhatsAppPhoneNumberId = "1",
                    WhatsAppTemplateName = "rappel",
                    WhatsAppAccessToken = "t",
                });

            var availability = new Mock<IVendorMessagingAvailability>();
            availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsVendorMessaging);

            if (exhausted is { } spent)
            {
                var row = ClinicMessagingMonth.For(
                    ClinicId, ClinicClock.CurrentMonthKey(), spent ? 1 : 200, DateTime.UtcNow);
                if (spent)
                {
                    row.RecordSend(DateTime.UtcNow);
                }

                Allowances
                    .Setup(a => a.GetMonthAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(row);
            }
            else
            {
                Allowances
                    .Setup(a => a.GetMonthAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((ClinicMessagingMonth?)null);
            }

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reminders:MinLeadHours"] = "1",
                ["Reminders:QuietHoursStartLocal"] = "0",
                ["Reminders:QuietHoursEndLocal"] = "0",
            }).Build();

            _scheduler = new ReminderScheduler(
                notifications.Object, clinics.Object, patients.Object, settings.Object,
                availability.Object, Allowances.Object, unitOfWork.Object, config,
                NullLogger<ReminderScheduler>.Instance);
        }

        public Task<RecallDispatchOutcome> ScheduleRecall() =>
            _scheduler.ScheduleRecallAsync(ClinicId, PatientId, "Jean Dupont", "un contrôle");

        private static IReadOnlyList<NotificationType> ParseChannels(string[] channels) => channels
            .Select(c => string.Equals(c, "Sms", StringComparison.OrdinalIgnoreCase)
                ? NotificationType.SMS
                : NotificationType.WhatsApp)
            .ToList();
    }

    private sealed class CommandHarness
    {
        public Patient Patient { get; }
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        private readonly SendRecallCommandHandler _handler;

        public CommandHarness(RecallDispatchOutcome outcome)
        {
            Patient = new Patient(
                PatientId, ClinicId, "Jean", "Dupont",
                new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                phoneNumber: new PhoneNumber("+21620123456"));

            Patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(Patient);

            var scheduler = new Mock<IReminderScheduler>();
            scheduler.Setup(s => s.ScheduleRecallAsync(
                    ClinicId, PatientId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(outcome);

            var resolver = new Mock<ICurrentClinicResolver>();
            resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));

            _handler = new SendRecallCommandHandler(
                Patients.Object, scheduler.Object, resolver.Object, UnitOfWork.Object);
        }

        public Task<Result<bool>> Send() =>
            _handler.Handle(new SendRecallCommand { PatientId = PatientId }, CancellationToken.None);
    }
}
