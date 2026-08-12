using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// Background sending on a cabinet that may not record new work (<c>clinic-subscription</c> Part G · FR-8 · EC-7):
/// SMS, WhatsApp and OS push all stop, and a queued row is <b>parked with a stated reason</b> rather than sent or
/// discarded — so extending the entitlement before the visit still gets the reminder out.
///
/// <para><b>⚠️ Every case here has a twin about the un-park pass</b>, because that is the half FR-8 names as the trap:
/// both reviewers ask only whether the <i>channel</i> can send, and a row parked for expiry passes all of those
/// checks. So the parked-row scenarios run with the channel <b>fully configured and enabled</b> — the state that
/// would release it — and assert it stays parked anyway.</para>
///
/// <para>Dates are expressed relative to <c>ClinicClock.ClinicToday()</c> rather than to a fixed calendar day: the
/// two jobs resolve the clinic's today themselves (their entry points take no parameter, Hangfire calls them), and
/// « expired » has to mean expired whenever the suite runs.</para>
/// </summary>
public class OutboxParkingTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string UserId = "local|dentist";

    // ---- The entitlement states -------------------------------------------------------------------

    private static ClinicSubscription EndingOn(DateTime endsOn)
    {
        var subscription = ClinicSubscription.For(ClinicId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.Create(
                ClinicId, SubscriptionPeriodKind.Paid,
                recordedOnClinicDay: new DateTime(2026, 1, 1),
                recordedAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                explicitEndsOn: endsOn)
        }, DateTime.UtcNow);
        return subscription;
    }

    private static DateTime Yesterday => ClinicClock.ClinicToday().AddDays(-1);

    private static ClinicSubscription Expired() => EndingOn(Yesterday);

    private static ClinicSubscription Valid() => EndingOn(ClinicClock.ClinicToday().AddMonths(6));

    private static ClinicSubscription Suspended()
    {
        var subscription = Valid();
        subscription.Suspend("Impayé", "vendor", DateTime.UtcNow);
        return subscription;
    }

    // ---- The reminder outbox ----------------------------------------------------------------------

    private sealed class FakeSender : IReminderChannelSender
    {
        public NotificationType Channel => NotificationType.SMS;
        public int Calls { get; private set; }

        public Task<ReminderSendResult> SendAsync(
            string phoneE164, string message, ResolvedReminderSettings settings,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ReminderSendResult.Sent);
        }
    }

    private sealed class ReminderHarness
    {
        public FakeSender Sender { get; } = new();
        public Mock<IClinicSubscriptionRepository> Subscriptions { get; } = new();
        public NotificationJob Job { get; }

        public ReminderHarness(
            ClinicSubscription? subscription,
            IEnumerable<Notification>? due = null,
            IEnumerable<Notification>? blocked = null,
            bool requiresSubscription = true)
        {
            Subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(subscription);

            var notifications = new Mock<INotificationRepository>();
            notifications.Setup(r => r.GetDueForDispatchAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((due ?? Array.Empty<Notification>()).ToList());
            notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((blocked ?? Array.Empty<Notification>()).ToList());

            var patients = new Mock<IPatientRepository>();
            patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new Patient(
                    id, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                    new Email("jean.dupont@example.com"), new PhoneNumber("20123456")));

            // The visit is AFTER the entitlement ends, which is EC-7's whole situation: the row is perfectly valid
            // and would send but for the cabinet's state.
            var appointments = new Mock<IAppointmentRepository>();
            appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Appointment(
                    Guid.NewGuid(), ClinicId, Guid.NewGuid(), null, DateTime.UtcNow.AddDays(3),
                    TimeSpan.FromMinutes(30)));

            // ⚠️ Fully enabled AND fully configured — the state that releases a parked row. Anything less and
            // « it stayed parked » would prove only that the channel was still broken.
            var settings = new Mock<IReminderSettingsProvider>();
            settings.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = new[] { NotificationType.SMS },
                    SmsApiUrl = "https://gateway.example/send",
                    SmsSenderId = "Clinique",
                    SmsApiKey = "k",
                });

            var probe = new Mock<IInternetProbe>();
            probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var policy = new Mock<ISubscriptionPolicy>();
            policy.Setup(p => p.RequiresSubscription).Returns(requiresSubscription);

            Job = new NotificationJob(
                notifications.Object, patients.Object, appointments.Object, unitOfWork.Object, probe.Object,
                settings.Object, new ConfigurationBuilder().Build(),
                new IReminderChannelSender[] { Sender }, new Mock<INotificationGenerator>().Object,
                policy.Object, Subscriptions.Object,
                // The WhatsApp forfait reads nothing here (`SellsVendorMessaging = false` by default), so these
                // entitlement-parking cases keep asserting the subscription gate alone.
                new Mock<IVendorMessagingAvailability>().Object, new Mock<IMessagingAllowanceRepository>().Object,
                new Mock<IAuditActorProvider>().Object, new Mock<ITenantScope>().Object,
                NullLogger<NotificationJob>.Instance);
        }
    }

    private static Notification QueuedReminder() =>
        new(Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous",
            "Rappel : Jean le 03/01 chez Clinique Test.", DateTime.UtcNow.AddMinutes(-1),
            Guid.NewGuid(), Guid.NewGuid(), ClinicId);

    // [EC-7] Queued before expiry, for a visit after it: parked with the machine-readable reason and NOT sent.
    [Fact]
    public async Task An_Expired_Cabinets_Queued_Reminder_Is_Parked_And_Not_Sent()
    {
        var reminder = QueuedReminder();
        var harness = new ReminderHarness(Expired(), due: new[] { reminder });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(0, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Blocked, reminder.Status);
        Assert.Equal(OutboxBlockReason.SubscriptionExpired, reminder.BlockedReason);
        // A stated reason, naming the date — « pas envoyé » with no explanation is the defect the status fixes.
        Assert.Equal(OutboxSubscriptionGate.Expired(Yesterday), reminder.ErrorMessage);
    }

    // [EC-7] Parked, not failed: the row survives, keeps no retry spent, and stays out of the terminal statuses the
    // purge can drop (FR-14 — nothing is deleted however long a cabinet stays expired).
    [Fact]
    public async Task Parking_Spends_No_Retry_And_Leaves_The_Row_In_A_Non_Terminal_State()
    {
        var reminder = QueuedReminder();
        var harness = new ReminderHarness(Expired(), due: new[] { reminder });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(0, reminder.RetryCount);
        Assert.NotEqual(NotificationStatus.Failed, reminder.Status);
        Assert.NotEqual(NotificationStatus.Sent, reminder.Status);
    }

    // [R-8][FR-8] THE trap: the review pass asks only whether the channel can send, and here it can — enabled, with
    // credentials. Without the entitlement term this row would be released and dispatched within a minute on a
    // cabinet that has not paid.
    [Fact]
    public async Task The_Review_Pass_Leaves_It_Parked_Even_With_The_Channel_Fully_Configured()
    {
        var parked = QueuedReminder();
        parked.MarkAsBlocked(OutboxBlockReason.SubscriptionExpired, OutboxSubscriptionGate.Expired(Yesterday));
        var harness = new ReminderHarness(Expired(), blocked: new[] { parked });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, parked.Status);
        Assert.Equal(OutboxBlockReason.SubscriptionExpired, parked.BlockedReason);
        Assert.Equal(0, harness.Sender.Calls);
    }

    // [EC-7] Extended before the visit ⇒ released. Back to Pending with the reason cleared; the NEXT tick sends it,
    // which is what keeps the housekeeping pass out of the sender.
    [Fact]
    public async Task Extending_The_Cabinet_Releases_The_Parked_Reminder()
    {
        var parked = QueuedReminder();
        parked.MarkAsBlocked(OutboxBlockReason.SubscriptionExpired, OutboxSubscriptionGate.Expired(Yesterday));
        var harness = new ReminderHarness(Valid(), blocked: new[] { parked });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Pending, parked.Status);
        Assert.Null(parked.BlockedReason);
        Assert.Null(parked.ErrorMessage);
        Assert.Equal(0, harness.Sender.Calls);
    }

    // [EC-11] A suspended cabinet parks too, and its sentence carries no date and never says « expiré »: paying
    // would not lift a suspension, so the wording must not send the practice to renew.
    [Fact]
    public async Task A_Suspended_Cabinets_Reminder_Is_Parked_With_The_Suspension_Wording()
    {
        var reminder = QueuedReminder();
        var harness = new ReminderHarness(Suspended(), due: new[] { reminder });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(NotificationStatus.Blocked, reminder.Status);
        Assert.Equal(OutboxSubscriptionGate.Suspended, reminder.ErrorMessage);
        Assert.DoesNotContain("expir", reminder.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // [AC-7.1/7.2] Where subscriptions are not enforced the gate reads NOTHING — not one extra query on the two
    // other deployment kinds, which is what « byte for byte unchanged » has to mean for a minutely job.
    [Fact]
    public async Task Where_Subscriptions_Are_Not_Enforced_Nothing_Is_Parked_And_No_Entitlement_Is_Read()
    {
        var reminder = QueuedReminder();
        var harness = new ReminderHarness(Expired(), due: new[] { reminder }, requiresSubscription: false);

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Sent, reminder.Status);
        harness.Subscriptions.Verify(
            s => s.GetByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [FR-13] A cabinet with NO entitlement row keeps sending, deliberately — unlike the HTTP gate, which refuses.
    // Nothing here is authorization: the work was recorded legitimately while the cabinet could write, and silencing
    // a practice's reminders over our own missing row would be a defect the practice cannot see or fix. That fault
    // is surfaced by verify-schema and subscription-report instead.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Row_Still_Sends()
    {
        var reminder = QueuedReminder();
        var harness = new ReminderHarness(subscription: null, due: new[] { reminder });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Sent, reminder.Status);
    }

    // A reminder with no clinic at all (a legacy row enqueued before per-clinic settings) has no entitlement to
    // consult, so it is not parked — the same reason the HTTP gate lets a caller who is not a cabinet through.
    [Fact]
    public async Task A_Reminder_With_No_Clinic_Is_Not_Parked()
    {
        var legacy = new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel de rendez-vous",
            "Rappel : Jean le 03/01 chez Clinique Test.", DateTime.UtcNow.AddMinutes(-1),
            Guid.NewGuid(), Guid.NewGuid());
        var harness = new ReminderHarness(Expired(), due: new[] { legacy });

        await harness.Job.ProcessPendingNotifications();

        Assert.Equal(1, harness.Sender.Calls);
        Assert.Equal(NotificationStatus.Sent, legacy.Status);
    }

    // ---- The push outbox -------------------------------------------------------------------------

    private sealed class FakePushSender : IPushSender
    {
        public DevicePlatform Platform => DevicePlatform.Android;
        public int Calls { get; private set; }

        public Task<PushSendResult> SendAsync(
            PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(PushSendResult.Sent);
        }
    }

    private sealed class PushHarness
    {
        public FakePushSender Sender { get; } = new();
        public PushDispatchJob Job { get; }

        public PushHarness(
            ClinicSubscription? subscription,
            DeviceRegistration device,
            IEnumerable<PushDelivery>? due = null,
            IEnumerable<PushDelivery>? blocked = null)
        {
            var subscriptions = new Mock<IClinicSubscriptionRepository>();
            subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(subscription);

            var deliveries = new Mock<IPushDeliveryRepository>();
            deliveries.Setup(d => d.GetDueForDispatchAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((due ?? Array.Empty<PushDelivery>()).ToList());
            deliveries.Setup(d => d.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((blocked ?? Array.Empty<PushDelivery>()).ToList());

            var devices = new Mock<IDeviceRegistrationRepository>();
            devices.Setup(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(device);

            // Sendable platform, live device — everything the review pass looks at is in order.
            var availability = new Mock<IOsPushAvailability>();
            availability.Setup(a => a.SupportsPush(It.IsAny<DevicePlatform>())).Returns(true);

            var probe = new Mock<IInternetProbe>();
            probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var policy = new Mock<ISubscriptionPolicy>();
            policy.Setup(p => p.RequiresSubscription).Returns(true);

            Job = new PushDispatchJob(
                deliveries.Object, devices.Object, new Mock<IAppointmentRepository>().Object, unitOfWork.Object,
                probe.Object, availability.Object, new ConfigurationBuilder().Build(),
                new IPushSender[] { Sender }, policy.Object, subscriptions.Object,
                new Mock<IAuditActorProvider>().Object, new Mock<ITenantScope>().Object,
                NullLogger<PushDispatchJob>.Instance);
        }
    }

    private static DeviceRegistration LiveDevice() => DeviceRegistration.Create(
        ClinicId, UserId, DevicePlatform.Android, "token-1", "1.0.0", DateTime.UtcNow);

    private static PushDelivery QueuedPush(DeviceRegistration device) => PushDelivery.Create(
        ClinicId, device.Id, UserId, NotificationCategory.AppointmentCreated, "Nouveau rendez-vous",
        appointmentId: null, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(-2));

    // [FR-8] « OS push stops » — the push queue has the identical shape and needed both halves too.
    [Fact]
    public async Task An_Expired_Cabinets_Queued_Push_Is_Parked_And_Not_Sent()
    {
        var device = LiveDevice();
        var row = QueuedPush(device);
        var harness = new PushHarness(Expired(), device, due: new[] { row });

        await harness.Job.DispatchQueuedPushes();

        Assert.Equal(0, harness.Sender.Calls);
        Assert.Equal(PushDeliveryStatus.Blocked, row.Status);
        Assert.Equal(OutboxBlockReason.SubscriptionExpired, row.BlockedReason);
        Assert.Equal(OutboxSubscriptionGate.Expired(Yesterday), row.FailureReason);
    }

    // [R-8] The push reviewer's own gap: device live, platform sendable — everything it checks says « release ».
    [Fact]
    public async Task The_Push_Review_Pass_Leaves_It_Parked_While_The_Cabinet_May_Not_Write()
    {
        var device = LiveDevice();
        var parked = QueuedPush(device);
        parked.MarkAsBlocked(
            OutboxBlockReason.SubscriptionExpired, OutboxSubscriptionGate.Expired(Yesterday), DateTime.UtcNow);
        var harness = new PushHarness(Expired(), device, blocked: new[] { parked });

        await harness.Job.DispatchQueuedPushes();

        Assert.Equal(PushDeliveryStatus.Blocked, parked.Status);
        Assert.Equal(OutboxBlockReason.SubscriptionExpired, parked.BlockedReason);
    }

    [Fact]
    public async Task Extending_The_Cabinet_Releases_The_Parked_Push()
    {
        var device = LiveDevice();
        var parked = QueuedPush(device);
        parked.MarkAsBlocked(
            OutboxBlockReason.SubscriptionExpired, OutboxSubscriptionGate.Expired(Yesterday), DateTime.UtcNow);
        var harness = new PushHarness(Valid(), device, blocked: new[] { parked });

        await harness.Job.DispatchQueuedPushes();

        Assert.Equal(PushDeliveryStatus.Pending, parked.Status);
        Assert.Null(parked.BlockedReason);
        Assert.Null(parked.FailureReason);
        Assert.Equal(0, harness.Sender.Calls);
    }

    // ---- The gate itself -------------------------------------------------------------------------

    // One entitlement read per cabinet per tick, however many of its rows are in the batch — a minutely job over a
    // 50-row batch must not issue 50 identical queries.
    [Fact]
    public async Task The_Gate_Reads_A_Cabinets_Entitlement_Once_Per_Tick()
    {
        var subscriptions = new Mock<IClinicSubscriptionRepository>();
        subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Expired());
        var policy = new Mock<ISubscriptionPolicy>();
        policy.Setup(p => p.RequiresSubscription).Returns(true);

        var gate = new OutboxSubscriptionGate(policy.Object, subscriptions.Object, ClinicClock.ClinicToday());

        Assert.NotNull(await gate.ReviewAsync(ClinicId));
        Assert.NotNull(await gate.ReviewAsync(ClinicId));
        Assert.NotNull(await gate.ReviewAsync(ClinicId));

        subscriptions.Verify(
            s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The last working day is still a working day (AC-1.1): an entitlement ending TODAY sends.
    [Fact]
    public async Task A_Cabinet_On_Its_Last_Day_Still_Sends()
    {
        var subscriptions = new Mock<IClinicSubscriptionRepository>();
        subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EndingOn(ClinicClock.ClinicToday()));
        var policy = new Mock<ISubscriptionPolicy>();
        policy.Setup(p => p.RequiresSubscription).Returns(true);

        var gate = new OutboxSubscriptionGate(policy.Object, subscriptions.Object, ClinicClock.ClinicToday());

        Assert.Null(await gate.ReviewAsync(ClinicId));
    }
}
