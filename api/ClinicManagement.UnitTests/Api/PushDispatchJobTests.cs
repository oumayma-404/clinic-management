using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The OS-push dispatcher (<c>mobile-native-shells</c> Part 6, AC-49, AC-50, AC-53, AC-72).
///
/// <para>Most of this file is about the checks that run <b>at send time</b> rather than at enqueue, and the reason
/// is that a push has no request behind it: it draws on a lock screen, so every guard the API applies per request
/// is already spent by the time a row is dispatched. A queued row outliving a sign-out, a rebind or a cancellation
/// is not a stale message — it is a notification delivered to the wrong person or about a visit that is not
/// happening.</para>
/// </summary>
public class PushDispatchJobTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AppointmentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string UserId = "local|dentist";

    private sealed class Harness
    {
        public Mock<IPushDeliveryRepository> Deliveries { get; } = new();
        public Mock<IDeviceRegistrationRepository> Devices { get; } = new();
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<IOsPushAvailability> Availability { get; } = new();
        public FakeSender Sender { get; } = new();
        public Mock<ITenantScope> TenantScope { get; } = new();
        public List<PushDelivery> Saved { get; } = new();
        public List<DeviceRegistration> SavedDevices { get; } = new();

        public PushDispatchJob Job { get; }

        public Harness(
            IEnumerable<PushDelivery>? due = null,
            IEnumerable<PushDelivery>? blocked = null,
            DeviceRegistration? device = null,
            Appointment? appointment = null,
            bool online = true,
            bool pushSupported = true)
        {
            Deliveries.Setup(d => d.GetDueForDispatchAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((due ?? Array.Empty<PushDelivery>()).ToList());
            Deliveries.Setup(d => d.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((blocked ?? Array.Empty<PushDelivery>()).ToList());
            Deliveries.Setup(d => d.UpdateAsync(It.IsAny<PushDelivery>(), It.IsAny<CancellationToken>()))
                .Callback<PushDelivery, CancellationToken>((row, _) => Saved.Add(row))
                .Returns(Task.CompletedTask);
            Deliveries.Setup(d => d.PurgeTerminalOlderThanAsync(
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            Devices.Setup(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(device);
            Devices.Setup(d => d.UpdateAsync(It.IsAny<DeviceRegistration>(), It.IsAny<CancellationToken>()))
                .Callback<DeviceRegistration, CancellationToken>((d, _) => SavedDevices.Add(d))
                .Returns(Task.CompletedTask);

            Appointments.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(appointment);

            Availability.Setup(a => a.SupportsPush(It.IsAny<DevicePlatform>())).Returns(pushSupported);
            Availability.Setup(a => a.UnavailableReason(It.IsAny<DevicePlatform>()))
                .Returns(pushSupported ? null : "Notifications indisponibles");

            var probe = new Mock<IInternetProbe>();
            probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(online);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            // Part G's outbox gate: these scenarios run on a deployment that does not enforce subscriptions, so it
            // reads no entitlement and every case below behaves as it did before it existed (AC-7.1/7.2). The
            // parking itself is covered by OutboxParkingTests.
            var policy = new Mock<ISubscriptionPolicy>();
            policy.Setup(p => p.RequiresSubscription).Returns(false);

            Job = new PushDispatchJob(
                Deliveries.Object, Devices.Object, Appointments.Object, unitOfWork.Object,
                probe.Object, Availability.Object, new ConfigurationBuilder().Build(),
                new[] { (IPushSender)Sender }, policy.Object,
                new Mock<IClinicSubscriptionRepository>().Object,
                Mock.Of<IAuditActorProvider>(), TenantScope.Object,
                NullLogger<PushDispatchJob>.Instance);
        }
    }

    /// <summary>A sender whose next outcome the test chooses, and which records what it was asked to send.</summary>
    private sealed class FakeSender : IPushSender
    {
        public DevicePlatform Platform => DevicePlatform.Android;
        public PushSendResult Next { get; set; } = PushSendResult.Sent;
        public List<PushMessage> Sent { get; } = new();

        public Task<PushSendResult> SendAsync(
            PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(Next);
        }
    }

    private static DeviceRegistration Device(
        Guid clinicId = default, string userId = UserId, bool active = true)
    {
        var device = DeviceRegistration.Create(
            clinicId == default ? ClinicId : clinicId, userId, DevicePlatform.Android, "token-1", "1.0.0",
            DateTime.UtcNow);

        if (!active)
        {
            device.Deactivate(DateTime.UtcNow);
        }

        return device;
    }

    private static PushDelivery Row(
        DeviceRegistration device,
        NotificationCategory category = NotificationCategory.AppointmentCreated,
        string recipient = UserId,
        Guid? appointmentId = null) =>
        PushDelivery.Create(
            ClinicId, device.Id, recipient, category, "Nouveau rendez-vous", appointmentId,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(-2));

    private static Appointment ActiveAppointment() =>
        new(AppointmentId, ClinicId, patientId: null, doctorId: null, DateTime.UtcNow.AddDays(1),
            TimeSpan.FromMinutes(30));

    // ---- AC-72: the job cannot silently read nothing ----------------------------------------------

    // [AC-72] Without a declared scope the query filters return NOTHING and the job logs a clean run — the whole
    // failure mode US-2 exists to prevent. Asserted on the call, because a passing empty run looks identical.
    [Fact]
    public async Task The_Job_Declares_Its_Cross_Clinic_Read()
    {
        var harness = new Harness();

        await harness.Job.DispatchQueuedPushes();

        harness.TenantScope.Verify(s => s.UseSystemWide(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Offline_The_Job_Sends_Nothing_And_Leaves_Rows_Pending()
    {
        var device = Device();
        var harness = new Harness(due: new[] { Row(device) }, device: device, online: false);

        await harness.Job.DispatchQueuedPushes();

        Assert.Empty(harness.Sender.Sent);
        Assert.Empty(harness.Saved);
    }

    // ---- AC-49: a dead token is terminal per DEVICE, not per message ------------------------------

    // [AC-49] The row fails AND the registration is deactivated. Failing only the row would leave every future
    // notification for an uninstalled app burning its whole retry budget, for ever.
    [Fact]
    public async Task An_Unregistered_Token_Fails_The_Row_And_Deactivates_The_Device()
    {
        var device = Device();
        var harness = new Harness(due: new[] { Row(device) }, device: device);
        harness.Sender.Next = PushSendResult.TokenInvalid("UNREGISTERED");

        await harness.Job.DispatchQueuedPushes();

        Assert.Equal(PushDeliveryStatus.Failed, Assert.Single(harness.Saved).Status);
        Assert.False(Assert.Single(harness.SavedDevices).IsActive);
    }

    // A transient failure keeps the row Pending so a later tick retries it — the opposite decision from the one
    // above, and the reason TokenInvalid is its own outcome rather than a flavour of failure.
    [Fact]
    public async Task A_Transient_Failure_Leaves_The_Row_Pending_And_The_Device_Alone()
    {
        var device = Device();
        var harness = new Harness(due: new[] { Row(device) }, device: device);
        harness.Sender.Next = PushSendResult.Transient("gateway 503");

        await harness.Job.DispatchQueuedPushes();

        Assert.Equal(PushDeliveryStatus.Pending, Assert.Single(harness.Saved).Status);
        Assert.Empty(harness.SavedDevices);
    }

    // ---- AC-50: a row that cannot send leaves the scan ------------------------------------------

    // [AC-50] Unsendable rows must not accumulate at the front of an oldest-first, batch-capped scan. Blocked is
    // non-terminal and records why.
    [Fact]
    public async Task An_Unsendable_Platform_Parks_The_Row_With_Its_Reason()
    {
        var device = Device();
        var harness = new Harness(due: new[] { Row(device) }, device: device, pushSupported: false);

        await harness.Job.DispatchQueuedPushes();

        var row = Assert.Single(harness.Saved);
        Assert.Equal(PushDeliveryStatus.Blocked, row.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.FailureReason));
        Assert.Empty(harness.Sender.Sent);
    }

    // [AC-50] The other half — without this the status would be a one-way door and the operator who finally
    // supplies the credentials would see the backlog stay put.
    [Fact]
    public async Task A_Blocked_Row_Returns_To_The_Queue_Once_Its_Platform_Is_Sendable()
    {
        var device = Device();
        var parked = Row(device);
        parked.MarkAsBlocked(
            OutboxBlockReason.ChannelUnconfigured, "Notifications Android non configurées", DateTime.UtcNow);

        var harness = new Harness(blocked: new[] { parked }, device: device);

        await harness.Job.DispatchQueuedPushes();

        var row = Assert.Single(harness.Saved);
        Assert.Equal(PushDeliveryStatus.Pending, row.Status);
        Assert.Null(row.FailureReason);
    }

    // A row whose platform is still unsendable stays parked rather than cycling every minute.
    [Fact]
    public async Task A_Blocked_Row_Stays_Parked_While_Its_Platform_Is_Still_Unsendable()
    {
        var device = Device();
        var parked = Row(device);
        parked.MarkAsBlocked(
            OutboxBlockReason.ChannelUnconfigured, "Notifications Android non configurées", DateTime.UtcNow);

        var harness = new Harness(blocked: new[] { parked }, device: device, pushSupported: false);

        await harness.Job.DispatchQueuedPushes();

        Assert.Empty(harness.Saved);
        Assert.Equal(PushDeliveryStatus.Blocked, parked.Status);
    }

    // ---- Dispatch-time eligibility: the checks a lock screen bypasses ----------------------------

    // [AC-40] A device deregistered between enqueue and dispatch receives nothing.
    [Fact]
    public async Task A_Deregistered_Device_Receives_Nothing()
    {
        var device = Device(active: false);
        var harness = new Harness(due: new[] { Row(device) }, device: device);

        await harness.Job.DispatchQueuedPushes();

        Assert.Empty(harness.Sender.Sent);
        Assert.Equal(PushDeliveryStatus.Failed, Assert.Single(harness.Saved).Status);
    }

    // [AC-41] The shared-tablet case at its most dangerous: the token was rebound to a colleague after this row was
    // queued, so delivering it now would put one user's notification on another's lock screen. No request-time
    // check can see this, because there is no request.
    [Fact]
    public async Task A_Rebound_Device_Does_Not_Receive_The_Previous_Users_Push()
    {
        var device = Device(userId: "local|colleague");
        var harness = new Harness(due: new[] { Row(device, recipient: UserId) }, device: device);

        await harness.Job.DispatchQueuedPushes();

        Assert.Empty(harness.Sender.Sent);
        Assert.Equal(PushDeliveryStatus.Failed, Assert.Single(harness.Saved).Status);
    }

    // [AC-53] A device that has moved clinic since the row was queued is refused for the same reason.
    [Fact]
    public async Task A_Device_That_Changed_Clinic_Receives_Nothing()
    {
        var device = Device(clinicId: OtherClinicId);
        var harness = new Harness(due: new[] { Row(device) }, device: device);

        await harness.Job.DispatchQueuedPushes();

        Assert.Empty(harness.Sender.Sent);
        Assert.Equal(PushDeliveryStatus.Failed, Assert.Single(harness.Saved).Status);
    }

    // A reminder is the one category that can be overtaken by events, so it — and only it — re-reads its
    // appointment. Cancelled means the banner must not fire, which is why the fan-out does not delete the row at
    // cancellation time: this check also covers the reschedule race a delete could not.
    [Fact]
    public async Task A_Reminder_For_A_Cancelled_Appointment_Is_Not_Sent()
    {
        var device = Device();
        var appointment = ActiveAppointment();
        appointment.Cancel();

        var harness = new Harness(
            due: new[] { Row(device, NotificationCategory.Reminder, appointmentId: AppointmentId) },
            device: device,
            appointment: appointment);

        await harness.Job.DispatchQueuedPushes();

        Assert.Empty(harness.Sender.Sent);
        Assert.Equal(PushDeliveryStatus.Failed, Assert.Single(harness.Saved).Status);
    }

    // The four event categories are about something that already happened, so they are NOT re-checked against the
    // appointment — a « rendez-vous annulé » push must still go out after the appointment is cancelled.
    [Fact]
    public async Task A_Cancellation_Push_Is_Sent_Even_Though_The_Appointment_Is_Cancelled()
    {
        var device = Device();
        var appointment = ActiveAppointment();
        appointment.Cancel();

        var harness = new Harness(
            due: new[] { Row(device, NotificationCategory.AppointmentCancelled, appointmentId: AppointmentId) },
            device: device,
            appointment: appointment);

        await harness.Job.DispatchQueuedPushes();

        Assert.Single(harness.Sender.Sent);
        Assert.Equal(PushDeliveryStatus.Sent, Assert.Single(harness.Saved).Status);
    }

    // ---- AC-47: what actually leaves the building --------------------------------------------------

    // [AC-47] Verified by reading what is SENT, not by inspecting a banner: the token, the fixed label, the
    // category and the routing id — and nothing else exists on the message to leak.
    [Fact]
    public async Task What_Is_Sent_Is_The_Label_The_Category_And_A_Routing_Id()
    {
        var device = Device();
        var harness = new Harness(
            due: new[] { Row(device, appointmentId: AppointmentId) }, device: device);

        await harness.Job.DispatchQueuedPushes();

        var message = Assert.Single(harness.Sender.Sent);
        Assert.Equal("token-1", message.Token);
        Assert.Equal("Nouveau rendez-vous", message.Label);
        Assert.Equal(NotificationCategory.AppointmentCreated, message.Category);
        Assert.Equal(AppointmentId, message.AppointmentId);
    }
}
