using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
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
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// The dispatcher's half of the WhatsApp reminder forfait (<c>vendor-whatsapp-messaging-quota</c> Part 1, steps 13–15a):
/// counting a send, holding one the forfait cannot cover, the ensure-create that must not cost a send, the
/// past-appointment guard, and the age bound that keeps the parked pile finite.
///
/// <para><b>⚠️ The three highest-value cases are the ones a row count cannot replace.</b>
/// <see cref="A_Send_And_Its_Counted_Unit_Ride_One_Commit"/> pins FR-1's atomicity (EC-14);
/// <see cref="A_Collision_On_The_Counting_Rows_Creation_Cannot_Cost_A_Send"/> pins § 14a — the defect there is
/// <i>silent</i> and costs real money, one message paid for and uncounted while its duplicate counts twice; and
/// <see cref="A_Held_Recall_Row_With_No_Appointment_Drains"/> pins the one drain that reaches a row nothing else can,
/// which AC-5.3 creates on purpose.</para>
///
/// <para>Dates are relative to <c>ClinicClock.ClinicToday()</c>: the job resolves the clinic's today itself (Hangfire
/// calls its entry point with no arguments), so « this month » and « already passed » have to mean that whenever the
/// suite runs — the same decision <c>OutboxParkingTests</c> documents.</para>
/// </summary>
public class NotificationJobMessagingTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static string ThisMonth => ClinicClock.CurrentMonthKey();

    private sealed class FakeSender : IReminderChannelSender
    {
        private readonly ReminderSendResult _result;

        public FakeSender(NotificationType channel, ReminderSendResult? result = null)
        {
            Channel = channel;
            _result = result ?? ReminderSendResult.Sent;
        }

        public NotificationType Channel { get; }
        public int Calls { get; private set; }

        public Task<ReminderSendResult> SendAsync(
            string phoneE164, string message, ResolvedReminderSettings settings,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class Harness
    {
        public FakeSender Sender { get; }
        public Mock<IMessagingAllowanceRepository> Allowances { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IVendorMessagingAvailability> Availability { get; } = new();
        public NotificationJob Job { get; }

        /// <summary>Every save, in order, with the state of the row that rode it — the atomicity assertion's evidence.</summary>
        public List<(NotificationStatus Status, int Consumed)> Saves { get; } = new();

        public ClinicMessagingMonth? Month { get; private set; }
        public List<ClinicMessagingMonth> Created { get; } = new();

        public Harness(
            IEnumerable<Notification>? due = null,
            IEnumerable<Notification>? blocked = null,
            ClinicMessagingMonth? month = null,
            IEnumerable<MessagingAllowanceEntry>? ledger = null,
            bool sellsVendorMessaging = true,
            NotificationType channel = NotificationType.WhatsApp,
            DateTime? appointmentAt = null,
            int heldMaxDays = 30,
            bool collideOnCreate = false,
            ReminderSendResult? sendResult = null)
        {
            Month = month;
            Sender = new FakeSender(channel, sendResult);
            Availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsVendorMessaging);

            var rows = (due ?? Array.Empty<Notification>()).ToList();

            Allowances
                .Setup(r => r.GetMonthAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Month);
            Allowances
                .Setup(r => r.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ledger ?? Array.Empty<MessagingAllowanceEntry>()).ToList());
            Allowances
                .Setup(r => r.AddMonthAsync(It.IsAny<ClinicMessagingMonth>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicMessagingMonth, CancellationToken>((m, _) =>
                {
                    Created.Add(m);
                    // The daily provisioning pass loses or wins the race depending on the fixture. When it wins, the
                    // row is already there by the time our INSERT commits — which is exactly § 14a's window.
                    if (!collideOnCreate)
                    {
                        Month = m;
                    }
                })
                .Returns(Task.CompletedTask);

            var notifications = new Mock<INotificationRepository>();
            notifications
                .Setup(r => r.GetDueForDispatchAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows);
            notifications
                .Setup(r => r.GetBlockedForReviewAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((blocked ?? Array.Empty<Notification>()).ToList());
            notifications
                .Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var patients = new Mock<IPatientRepository>();
            patients
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new Patient(
                    id, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                    new Email("jean.dupont@example.com"), new PhoneNumber("20123456")));

            var appointments = new Mock<IAppointmentRepository>();
            appointments
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Appointment(
                    Guid.NewGuid(), ClinicId, Guid.NewGuid(), null,
                    appointmentAt ?? DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(30)));

            // Fully enabled AND fully configured — the state that would send. Anything less and « it was held » would
            // prove only that the channel was broken.
            var settings = new Mock<IReminderSettingsProvider>();
            settings
                .Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = new[] { NotificationType.SMS, NotificationType.WhatsApp },
                    SmsApiUrl = "https://gateway.example/send",
                    SmsSenderId = "Clinique",
                    SmsApiKey = "k",
                    WhatsAppApiUrl = "https://graph.example/messages",
                    WhatsAppPhoneNumberId = "12345",
                    WhatsAppAccessToken = "t",
                    WhatsAppTemplateName = "rappel_rdv",
                });

            var probe = new Mock<IInternetProbe>();
            probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var saveCollides = collideOnCreate;
            UnitOfWork
                .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    // The ensure-create's own save is the first one, and it is the one the daily pass collides with.
                    if (saveCollides && Created.Count > 0 && Saves.Count == 0)
                    {
                        saveCollides = false;
                        // The pass's row is now visible to a re-read, which is what the catch does next.
                        Month = ClinicMessagingMonth.For(ClinicId, ThisMonth, 200, DateTime.UtcNow);
                        throw new ConflictException("duplicate key value violates unique constraint");
                    }

                    Saves.Add((rows.Count > 0 ? rows[0].Status : NotificationStatus.Pending, Month?.ConsumedMessages ?? -1));
                    return Task.FromResult(1);
                });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reminders:HeldMaxDays"] = heldMaxDays.ToString(),
                })
                .Build();

            Job = new NotificationJob(
                notifications.Object, patients.Object, appointments.Object, UnitOfWork.Object, probe.Object,
                settings.Object, config, new IReminderChannelSender[] { Sender },
                new Mock<INotificationGenerator>().Object,
                // Subscriptions are not enforced here, so the entitlement gate reads nothing and every verdict below
                // belongs to the forfait (AC-4.7's ordering has its own case in OutboxParkingTests).
                Mock.Of<ISubscriptionPolicy>(p => p.RequiresSubscription == false),
                new Mock<IClinicSubscriptionRepository>().Object,
                Availability.Object, Allowances.Object,
                new Mock<IAuditActorProvider>().Object, new Mock<ITenantScope>().Object,
                NullLogger<NotificationJob>.Instance);
        }
    }

    private static Notification WhatsAppReminder(DateTime? scheduledFor = null, Guid? appointmentId = null) =>
        new(Guid.NewGuid(), NotificationType.WhatsApp, "Rappel de rendez-vous",
            "Rappel : Jean, vous avez un rendez-vous chez Clinique Test.",
            scheduledFor ?? DateTime.UtcNow.AddMinutes(-1),
            appointmentId: appointmentId ?? Guid.NewGuid(),
            patientId: Guid.NewGuid(),
            clinicId: ClinicId);

    /// <summary>A recall row: no appointment at all, which is what AC-5.3 creates and step 15's guard cannot reach.</summary>
    private static Notification RecallRow(DateTime scheduledFor) =>
        new(Guid.NewGuid(), NotificationType.WhatsApp, "Rappel de suivi",
            "Bonjour Jean, il est temps de reprendre rendez-vous.",
            scheduledFor, appointmentId: null, patientId: Guid.NewGuid(), clinicId: ClinicId);

    private static ClinicMessagingMonth Month(int allowance, int consumed)
    {
        var month = ClinicMessagingMonth.For(ClinicId, ThisMonth, allowance, DateTime.UtcNow);
        for (var i = 0; i < consumed; i++)
        {
            month.RecordSend(DateTime.UtcNow);
        }

        return month;
    }

    private static Notification Blocked(Notification row, OutboxBlockReason reason)
    {
        row.MarkAsBlocked(reason, "Forfait épuisé — envoi en attente");
        return row;
    }

    // ---- FR-1: counting, and its atomicity -------------------------------------------------------

    /// <summary>
    /// [FR-1][EC-14] The <c>Sent</c> mark and the counted unit ride <b>one</b> commit, so a crash loses both or
    /// neither. Asserted on the save's own observation rather than on the two final values: staging the increment into
    /// a <i>second</i> save would leave both correct at the end while losing one of them on a crash.
    /// </summary>
    [Fact]
    public async Task A_Send_And_Its_Counted_Unit_Ride_One_Commit()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(due: new[] { row }, month: Month(allowance: 200, consumed: 5));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Equal(6, harness.Month!.ConsumedMessages);

        // Exactly one save, and at the moment it happened the row was already Sent AND the unit already counted.
        var sends = harness.Saves.Where(s => s.Status == NotificationStatus.Sent).ToList();
        Assert.Single(sends);
        Assert.Equal(6, sends[0].Consumed);
    }

    [Fact]
    public async Task An_Sms_Send_Counts_Nothing() // [AC-4.6]
    {
        var row = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel", "Rappel : Jean.", DateTime.UtcNow.AddMinutes(-1),
            appointmentId: Guid.NewGuid(), patientId: Guid.NewGuid(), clinicId: ClinicId);

        var harness = new Harness(
            due: new[] { row }, month: Month(allowance: 200, consumed: 5), channel: NotificationType.SMS);

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Sent, row.Status);
        // The forfait pays for WhatsApp and nothing else.
        Assert.Equal(5, harness.Month!.ConsumedMessages);
    }

    /// <summary>
    /// A send that Meta did not accept spends no unit. The row stays <c>Pending</c> for a later tick, and the forfait is
    /// untouched — only a delivered message costs the vendor anything.
    /// </summary>
    [Fact]
    public async Task A_Failed_Send_Counts_Nothing()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(
            due: new[] { row },
            month: Month(allowance: 200, consumed: 5),
            sendResult: ReminderSendResult.Transient("Meta a refusé l'envoi"));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.NotEqual(NotificationStatus.Sent, row.Status);
        Assert.Equal(5, harness.Month!.ConsumedMessages);
    }

    // ---- § 14a: the ensure-create must not cost a send -------------------------------------------

    /// <summary>
    /// [§ 14a] A cabinet's first WhatsApp reminder of the month is <b>not held</b>: the counting row is created from
    /// the fold before the send. Left to the daily pass, every rollover would park a practice's reminders for up to
    /// 24 h — and the first sends of a month are the ones most likely to still be useful.
    /// </summary>
    [Fact]
    public async Task The_First_Send_Of_A_Month_Creates_Its_Counting_Row_Rather_Than_Being_Held()
    {
        var row = WhatsAppReminder();
        var standing = MessagingAllowanceEntry.Create(
            ClinicId, MessagingAllowanceKind.Standing, 200, ThisMonth, DateTime.UtcNow.AddMonths(-2));

        var harness = new Harness(due: new[] { row }, month: null, ledger: new[] { standing });

        await harness.Job.ProcessPendingNotifications();

        Assert.Single(harness.Created);
        // The figure comes from the FOLD, not from the policy's configured default.
        Assert.Equal(200, harness.Created[0].AllowanceMessages);
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Equal(1, harness.Month!.ConsumedMessages);
    }

    /// <summary>
    /// [§ 14a][EC-15] A unique-violation collision on <c>(ClinicId, MonthKey)</c> — the daily provisioning pass
    /// inserting the same row in this exact window — <b>cannot cost a send</b>. It is caught, the row is re-read, and
    /// the send proceeds.
    ///
    /// <para>This is the case that makes the ensure-create's own save load-bearing. Staged into the send's commit
    /// instead, the collision would throw <i>after</i> Meta had accepted the message: the row stays un-<c>Sent</c>, the
    /// next tick re-sends it, and one message is paid for and uncounted while its duplicate counts twice — silently,
    /// for a reason nobody chose.</para>
    /// </summary>
    [Fact]
    public async Task A_Collision_On_The_Counting_Rows_Creation_Cannot_Cost_A_Send()
    {
        var row = WhatsAppReminder();
        var standing = MessagingAllowanceEntry.Create(
            ClinicId, MessagingAllowanceKind.Standing, 200, ThisMonth, DateTime.UtcNow.AddMonths(-2));

        var harness = new Harness(
            due: new[] { row }, month: null, ledger: new[] { standing }, collideOnCreate: true);

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Sent, row.Status);
        // The pass's row is the one that survives, and this send is counted against it.
        Assert.Equal(1, harness.Month!.ConsumedMessages);
    }

    /// <summary>
    /// [AC-4.3] A cabinet whose ledger reaches this month with <b>nothing</b> gets no row created and is held under
    /// its own reason. Creating a zeroed row would turn our own bookkeeping gap into a statement that the vendor
    /// allowed the practice nothing, and would make « non mesuré » unreachable for ever.
    /// </summary>
    [Fact]
    public async Task A_Cabinet_With_No_Ledger_Gets_No_Row_And_Is_Held_As_Missing()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(due: new[] { row }, month: null, ledger: Array.Empty<MessagingAllowanceEntry>());

        await harness.Job.ProcessPendingNotifications();

        Assert.Empty(harness.Created);
        Assert.Equal(0, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Blocked, row.Status);
        Assert.Equal(OutboxBlockReason.MessagingAllowanceMissing, row.BlockedReason);
    }

    // ---- FR-4: holding, and the pre-send property ------------------------------------------------

    /// <summary>
    /// [AC-4.1] An exhausted cabinet's reminder is <b>held, not sent and not failed</b> — and the hold is
    /// <b>pre-send</b>: the sender is never called and no unit is counted, which is what « consomme rien » means.
    /// </summary>
    [Fact]
    public async Task An_Exhausted_Cabinet_Holds_Its_Reminder_Before_The_Sender_Is_Reached()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(due: new[] { row }, month: Month(allowance: 200, consumed: 200));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(0, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Blocked, row.Status);
        Assert.Equal(OutboxBlockReason.MessagingAllowanceExhausted, row.BlockedReason);
        Assert.Equal(200, harness.Month!.ConsumedMessages);
        // Not failed, and no retry budget spent: nothing was attempted.
        Assert.Equal(0, row.RetryCount);
    }

    /// <summary>[AC-4.2][EC-2] A grant releases the held row within one review cycle — the case holding exists for.</summary>
    [Fact]
    public async Task A_Grant_Releases_The_Held_Row_On_The_Next_Review()
    {
        var row = Blocked(WhatsAppReminder(), OutboxBlockReason.MessagingAllowanceExhausted);
        // The vendor has topped the cabinet up: the fold now leaves messages, so the row goes back to the queue.
        var harness = new Harness(blocked: new[] { row }, month: Month(allowance: 400, consumed: 200));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Null(row.BlockedReason);
    }

    /// <summary>
    /// [AC-4.8] A still-exhausted cabinet's held row is <b>not</b> released, even though the channel is fully enabled
    /// and configured — the state that would release it. This is the half FR-8 named as the trap, one feature over.
    /// </summary>
    [Fact]
    public async Task A_Still_Exhausted_Cabinet_Keeps_Its_Row_Held()
    {
        var row = Blocked(WhatsAppReminder(), OutboxBlockReason.MessagingAllowanceExhausted);
        var harness = new Harness(blocked: new[] { row }, month: Month(allowance: 200, consumed: 200));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, row.Status);
        Assert.Equal(OutboxBlockReason.MessagingAllowanceExhausted, row.BlockedReason);
    }

    /// <summary>
    /// A <b>channel</b>-parked row on an exhausted cabinet is not released into a queue that is about to park it again
    /// for the other reason — which is why the gate is asked for <i>every</i> parked row rather than only its own.
    /// </summary>
    [Fact]
    public async Task A_Channel_Parked_Row_Is_Not_Released_Onto_An_Exhausted_Cabinet()
    {
        var row = Blocked(WhatsAppReminder(), OutboxBlockReason.ChannelDisabled);
        var harness = new Harness(blocked: new[] { row }, month: Month(allowance: 200, consumed: 200));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, row.Status);
    }

    // ---- AC-4.5/4.5a: the past-appointment guard, on the release path ----------------------------

    /// <summary>
    /// [AC-4.5][AC-4.5a][EC-1] A released reminder whose appointment has <b>passed</b> is not sent — it fails as
    /// obsolete. This is what stops AC-4.2's month rollover being read as a rescue: by the time the month turns, a
    /// held reminder's visit is in the past.
    /// </summary>
    [Fact]
    public async Task A_Released_Reminder_Whose_Visit_Has_Passed_Fails_As_Obsolete()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(
            due: new[] { row },
            month: Month(allowance: 200, consumed: 0),
            appointmentAt: DateTime.UtcNow.AddHours(-2));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(0, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Failed, row.Status);
        Assert.Contains("passé", row.ErrorMessage);
        // And no unit is spent on a message nobody sent.
        Assert.Equal(0, harness.Month!.ConsumedMessages);
    }

    [Fact]
    public async Task A_Reminder_For_A_Visit_Still_To_Come_Sends()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(
            due: new[] { row },
            month: Month(allowance: 200, consumed: 0),
            appointmentAt: DateTime.UtcNow.AddHours(2));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Sent, row.Status);
    }

    // ---- Step 15a: the age bound, and the row it exists for -------------------------------------

    /// <summary>
    /// [step 15a][R-5] A <b>recall</b> row — <c>appointmentId: null</c>, which AC-5.3 creates deliberately — held past
    /// <c>Reminders:HeldMaxDays</c> drains.
    ///
    /// <para>This is the case the past-appointment guard structurally cannot reach: a recall row has no appointment, so
    /// nothing can ever make it obsolete, it is non-terminal, and the purge excludes it by construction. Without this
    /// bound it would be re-examined on every review tick for ever — the starvation shape this outbox has already been
    /// bitten by twice.</para>
    /// </summary>
    [Fact]
    public async Task A_Held_Recall_Row_With_No_Appointment_Drains()
    {
        var row = Blocked(RecallRow(DateTime.UtcNow.AddDays(-45)), OutboxBlockReason.MessagingAllowanceExhausted);
        // Still exhausted, so nothing releases it; the bound is what ends it.
        var harness = new Harness(blocked: new[] { row }, month: Month(allowance: 200, consumed: 200));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, row.Status);
        Assert.Contains("obsol", row.ErrorMessage);
    }

    /// <summary>
    /// The bound is <b>reason-agnostic</b>: the two pre-existing channel reasons have the identical defect on a recall
    /// row today, so « how long may a send wait? » gets one answer rather than one per reason.
    /// </summary>
    [Fact]
    public async Task The_Age_Bound_Drains_A_Channel_Parked_Recall_Row_Too()
    {
        var row = Blocked(RecallRow(DateTime.UtcNow.AddDays(-45)), OutboxBlockReason.ChannelUnconfigured);
        var harness = new Harness(blocked: new[] { row }, month: Month(allowance: 200, consumed: 0));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, row.Status);
    }

    /// <summary>
    /// [AC-4.4] The other direction, and the one that makes the bound a bound rather than a purge: a row <b>inside</b>
    /// the window is still <b>held</b>, never failed. « Never purged while it could still be sent » is the promise.
    /// </summary>
    [Fact]
    public async Task A_Held_Row_Inside_The_Window_Is_Still_Held()
    {
        var row = Blocked(RecallRow(DateTime.UtcNow.AddDays(-5)), OutboxBlockReason.MessagingAllowanceExhausted);
        var harness = new Harness(blocked: new[] { row }, month: Month(allowance: 200, consumed: 200));

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, row.Status);
        Assert.Equal(OutboxBlockReason.MessagingAllowanceExhausted, row.BlockedReason);
    }

    /// <summary>
    /// The bound is configurable, and a shorter window drains a row the default would still hold — which is what shows
    /// the setting is actually read rather than the 30 being compiled in.
    /// </summary>
    [Fact]
    public async Task The_Age_Bound_Reads_Its_Configured_Window()
    {
        var row = Blocked(RecallRow(DateTime.UtcNow.AddDays(-10)), OutboxBlockReason.MessagingAllowanceExhausted);
        var harness = new Harness(
            blocked: new[] { row }, month: Month(allowance: 200, consumed: 200), heldMaxDays: 7);

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Failed, row.Status);
    }

    // ---- EC-16 ------------------------------------------------------------------------------------

    /// <summary>
    /// [EC-16] Where the deployment does not sell vendor messaging, nothing about the forfait happens: no row is
    /// created, nothing is counted, nothing is held, and the send behaves byte-for-byte as it did before.
    /// </summary>
    [Fact]
    public async Task A_Deployment_That_Does_Not_Sell_Messaging_Is_Untouched()
    {
        var row = WhatsAppReminder();
        var harness = new Harness(
            due: new[] { row }, month: null, ledger: Array.Empty<MessagingAllowanceEntry>(),
            sellsVendorMessaging: false);

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Empty(harness.Created);
        harness.Allowances.Verify(
            r => r.GetMonthAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
