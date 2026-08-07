using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Notifications;

/// <summary>
/// The OS-push fan-out (<c>mobile-native-shells</c> Part 6, AC-43…AC-47, AC-55).
///
/// <para><b>The load-bearing case is <see cref="The_Push_Label_Is_The_Feed_Rows_Own_Title"/>.</b> AC-47 says the
/// payload carries « a fixed French category label » and AC-45 says the audience « equals the in-app feed's »,
/// and both are asserted <b>against the feed</b> rather than against a retyped table: the real
/// <see cref="NotificationGenerator"/> runs inside the real decorator, so the <c>StaffNotification</c> and the
/// <c>PushDelivery</c> produced by one call are compared with each other. A constant here that merely looked
/// right would still be a second authority, and the drift it allowed would be a lock screen saying something
/// the app does not.</para>
/// </summary>
public class PushFanOutTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AppointmentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid DoctorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private const string ActorId = "local|actor";
    private const string ColleagueId = "local|colleague";
    private const string DentistId = "local|dentist";

    /// <summary>
    /// Wires the real generator inside the real decorator over mocked repositories, and captures both sides of
    /// what one call produces.
    /// </summary>
    private sealed class Harness
    {
        public List<StaffNotification> FeedRows { get; } = new();
        public List<PushDelivery> PushRows { get; } = new();
        public Mock<IDeviceRegistrationRepository> Devices { get; } = new();
        public Mock<IOsPushAvailability> Availability { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public INotificationGenerator Generator { get; }

        public Harness(
            IEnumerable<User>? staff = null,
            IEnumerable<DeviceRegistration>? devices = null,
            bool pushAvailable = true,
            Doctor? doctor = null)
        {
            var feed = new Mock<IStaffNotificationRepository>();
            feed.Setup(f => f.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
                .Callback<StaffNotification, CancellationToken>((n, _) => FeedRows.Add(n))
                .Returns(Task.CompletedTask);
            feed.Setup(f => f.GetReminderByAppointmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StaffNotification?)null);
            feed.Setup(f => f.GetPostVisitReviewByAppointmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StaffNotification?)null);

            var doctors = new Mock<IDoctorRepository>();
            doctors.Setup(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(doctor);

            var users = new Mock<IUserRepository>();
            var staffList = (staff ?? new[] { Actor(), Colleague() }).ToList();
            users.Setup(u => u.GetByClinicIdAsync(
                    It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedResult<User>(staffList, 1, staffList.Count, staffList.Count));

            var deviceList = (devices ?? new[] { Device(ColleagueId) }).ToList();
            Devices.Setup(d => d.GetActiveForUsersAsync(
                    It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IEnumerable<string> ids, CancellationToken _) =>
                    deviceList.Where(x => ids.Contains(x.UserId)).ToList());

            var deliveries = new Mock<IPushDeliveryRepository>();
            deliveries.Setup(d => d.AddRangeAsync(It.IsAny<IEnumerable<PushDelivery>>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<PushDelivery>, CancellationToken>((rows, _) => PushRows.AddRange(rows))
                .Returns(Task.CompletedTask);

            Availability.Setup(a => a.IsAvailableAtAll).Returns(pushAvailable);
            Availability.Setup(a => a.SupportsPush(It.IsAny<DevicePlatform>())).Returns(pushAvailable);

            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Generator = new PushNotificationGeneratorDecorator(
                new NotificationGenerator(
                    feed.Object, doctors.Object, UnitOfWork.Object,
                    Mock.Of<IRealtimeNotifier>(), NullLogger<NotificationGenerator>.Instance),
                users.Object,
                doctors.Object,
                Devices.Object,
                deliveries.Object,
                UnitOfWork.Object,
                Availability.Object,
                new ConfigurationBuilder().Build(),
                NullLogger<PushNotificationGeneratorDecorator>.Instance);
        }
    }

    private static User Actor() => Named(User.CreateLocalUser(
        ClinicId, User.RoleSecretary, "actor@cabinet.tn", "hash", "Actor"), ActorId);

    private static User Colleague() => Named(User.CreateLocalUser(
        ClinicId, User.RoleDoctor, "colleague@cabinet.tn", "hash", "Colleague"), ColleagueId);

    private static User Dentist() => Named(User.CreateLocalUser(
        ClinicId, User.RoleDoctor, "dentist@cabinet.tn", "hash", "Dentist"), DentistId);

    /// <summary>
    /// <c>User.Id</c> is generated inside the factory, so the fixture forces the stable ids the assertions read.
    /// Reflection rather than a test-only setter: adding one to production code to make a test convenient is how a
    /// private setter stops meaning anything.
    /// </summary>
    private static User Named(User user, string id)
    {
        typeof(Entity<string>).GetProperty(nameof(Entity<string>.Id))!.SetValue(user, id);
        return user;
    }

    private static DeviceRegistration Device(string userId, DevicePlatform platform = DevicePlatform.Android) =>
        DeviceRegistration.Create(ClinicId, userId, platform, $"token-{userId}-{platform}", "1.0.0", DateTime.UtcNow);

    // ---- AC-47 / AC-45: the payload and the audience are the feed's, not a second list ------------

    // [AC-47] The whole of what reaches a lock screen is the category's fixed phrase — and it is the SAME string
    // the feed row carries, compared here rather than retyped. Nothing else may leak: the feed's own message
    // names the patient, and that must not travel.
    [Fact]
    public async Task The_Push_Label_Is_The_Feed_Rows_Own_Title()
    {
        var harness = new Harness();

        await harness.Generator.AppointmentCreatedAsync(
            ClinicId, AppointmentId, ActorId, "Ben Salah", DateTime.UtcNow.AddDays(3));

        var feedRow = Assert.Single(harness.FeedRows);
        var pushRow = Assert.Single(harness.PushRows);

        Assert.Equal(feedRow.Title, pushRow.Label);
        Assert.Equal(feedRow.Category, pushRow.Category);
        Assert.Equal(AppointmentId, pushRow.AppointmentId);
        // The name is in the feed's message and must not be anywhere in what is sent.
        Assert.DoesNotContain("Ben Salah", pushRow.Label);
    }

    // [AC-45] The actor never receives a push for their own action — the feed excludes them from their own panel,
    // and a banner would be the one place that exclusion did not hold.
    [Fact]
    public async Task The_Actor_Gets_No_Push_For_Their_Own_Action()
    {
        var harness = new Harness(
            staff: new[] { Actor(), Colleague() },
            devices: new[] { Device(ActorId), Device(ColleagueId) });

        await harness.Generator.AppointmentCreatedAsync(
            ClinicId, AppointmentId, ActorId, "Ben Salah", DateTime.UtcNow.AddDays(3));

        Assert.All(harness.PushRows, row => Assert.NotEqual(ActorId, row.RecipientUserId));
        Assert.Equal(ColleagueId, Assert.Single(harness.PushRows).RecipientUserId);
    }

    // [AC-45] One row per DEVICE, not per user: a dentist with a phone and a tablet must be reached on both, and a
    // per-user row would silently pick one.
    [Fact]
    public async Task One_Row_Is_Queued_Per_Device()
    {
        var harness = new Harness(
            staff: new[] { Actor(), Colleague() },
            devices: new[]
            {
                Device(ColleagueId, DevicePlatform.Android),
                Device(ColleagueId, DevicePlatform.Ios)
            });

        await harness.Generator.AppointmentCreatedAsync(
            ClinicId, AppointmentId, ActorId, "Ben Salah", DateTime.UtcNow.AddDays(3));

        Assert.Equal(2, harness.PushRows.Count);
        Assert.All(harness.PushRows, row => Assert.Equal(ColleagueId, row.RecipientUserId));
    }

    // [AC-45] A deactivated colleague is excluded. This is the one departure from the feed's SQL and it is
    // deliberate — see the decorator's own note: they cannot open the app to read the feed anyway, but their
    // device stays registered because somebody who was switched off does not sign out.
    [Fact]
    public async Task A_Deactivated_Colleague_Gets_No_Push()
    {
        var deactivated = Colleague();
        deactivated.Deactivate();

        var harness = new Harness(
            staff: new[] { Actor(), deactivated },
            devices: new[] { Device(ColleagueId) });

        await harness.Generator.AppointmentCreatedAsync(
            ClinicId, AppointmentId, ActorId, "Ben Salah", DateTime.UtcNow.AddDays(3));

        Assert.Empty(harness.PushRows);
    }

    // ---- AC-43 / AC-44: which categories reach a locked phone --------------------------------------

    // [AC-44] The four operational alerts stay in the app. Waking a dentist at home for a box of gloves is how the
    // OS permission gets revoked — and revoking it costs the five that matter.
    [Fact]
    public async Task The_Operational_Alerts_Produce_No_Push_While_Still_Reaching_The_Feed()
    {
        var harness = new Harness();

        await harness.Generator.LowStockAsync(ClinicId, Guid.NewGuid(), "Gants", 2, 10);
        await harness.Generator.EnsureStockExpiringSoonAsync(
            ClinicId, Guid.NewGuid(), "Anesthésique", DateTime.UtcNow.AddDays(10));
        await harness.Generator.EnsureBackupStaleAsync(ClinicId, DateTime.UtcNow.AddDays(-3), 24);
        await harness.Generator.ReminderDeliveryFailedAsync(
            ClinicId, AppointmentId, "Ben Salah", "SMS", "numéro invalide", false);

        Assert.Empty(harness.PushRows);
        Assert.Equal(4, harness.FeedRows.Count);
    }

    // [AC-43] The ~24 h reminder pushes at the feed row's own effective time — both read from
    // StaffNotificationRules, so a banner cannot arrive at a different hour from the row it announces.
    //
    // ⚠️ The appointment is pinned to 13:00 UTC on purpose, so the due moment lands inside working hours. This
    // assertion is only true outside quiet hours — see the test below, which is where the two legitimately part
    // company — and a `DateTime.UtcNow.AddDays(5)` fixture would have made this pass or fail depending on the
    // hour the suite happened to run.
    [Fact]
    public async Task The_Reminder_Push_Waits_For_The_Same_Moment_The_Feed_Row_Does()
    {
        var harness = new Harness();
        var appointment = DateTime.UtcNow.Date.AddDays(5).AddHours(13);

        await harness.Generator.ScheduleAppointmentReminderAsync(ClinicId, AppointmentId, "Ben Salah", appointment);

        var feedRow = Assert.Single(harness.FeedRows);
        var pushRow = Assert.Single(harness.PushRows);

        Assert.Equal(feedRow.EffectiveFeedTime, pushRow.SendNotBefore);
        Assert.Equal(StaffNotificationRules.ReminderDueTimeUtc(appointment), pushRow.SendNotBefore);
    }

    // [AC-46] The one place the push and the feed row deliberately DISAGREE, and it is worth its own test because
    // the obvious assertion above is what hides it: an in-app row appearing at 02:00 wakes nobody, so the feed has
    // no quiet-hours floor at all, while a banner at 02:00 is exactly what the floor exists to prevent. The push is
    // deferred to 08:00 clinic-local; the feed row stays where it was.
    [Fact]
    public async Task A_Reminder_Falling_In_Quiet_Hours_Is_Deferred_While_The_Feed_Row_Is_Not()
    {
        var harness = new Harness();
        // 01:00 UTC = 02:00 clinic-local, inside the 21:00→08:00 window.
        var appointment = DateTime.UtcNow.Date.AddDays(5).AddHours(1);

        await harness.Generator.ScheduleAppointmentReminderAsync(ClinicId, AppointmentId, "Ben Salah", appointment);

        var feedRow = Assert.Single(harness.FeedRows);
        var pushRow = Assert.Single(harness.PushRows);

        Assert.Equal(StaffNotificationRules.ReminderDueTimeUtc(appointment), feedRow.EffectiveFeedTime);
        Assert.True(pushRow.SendNotBefore > feedRow.EffectiveFeedTime);
        // 08:00 clinic-local (UTC+1, no DST) is 07:00 UTC.
        Assert.Equal(7, pushRow.SendNotBefore.Hour);
        Assert.Equal(0, pushRow.SendNotBefore.Minute);
    }

    // [AC-43] Inside the lead window the generator schedules NO feed row, so there must be no push either — a
    // banner announcing a notification that does not exist would open a screen with nothing on it.
    [Fact]
    public async Task No_Reminder_Push_Is_Queued_Inside_The_Lead_Window()
    {
        var harness = new Harness();

        await harness.Generator.ScheduleAppointmentReminderAsync(
            ClinicId, AppointmentId, "Ben Salah", DateTime.UtcNow.AddHours(3));

        Assert.Empty(harness.FeedRows);
        Assert.Empty(harness.PushRows);
    }

    // [AC-45] A doctor-targeted post-visit review reaches ONLY that doctor, resolved through the same rule the
    // feed row's TargetUserId comes from.
    [Fact]
    public async Task A_Post_Visit_Review_Pushes_Only_To_The_Visits_Dentist()
    {
        var doctor = new Doctor(DoctorId, ClinicId, "Amine", "Trabelsi", "Dentiste");
        doctor.LinkToUser(DentistId);

        var harness = new Harness(
            staff: new[] { Actor(), Colleague(), Dentist() },
            devices: new[] { Device(ColleagueId), Device(DentistId) },
            doctor: doctor);

        await harness.Generator.EnsurePostVisitReviewAsync(
            ClinicId, AppointmentId, DoctorId, "Ben Salah", DateTime.UtcNow.AddHours(1));

        var feedRow = Assert.Single(harness.FeedRows);
        var pushRow = Assert.Single(harness.PushRows);

        Assert.Equal(DentistId, feedRow.TargetUserId);
        Assert.Equal(DentistId, pushRow.RecipientUserId);
    }

    // ---- AC-55: a push failure never touches the operation that caused it -------------------------

    // [AC-55] The feed row is written and the caller returns normally even when the push side throws. The whole
    // chain is a post-commit side effect of an operation that has already committed.
    [Fact]
    public async Task A_Push_Failure_Neither_Throws_Nor_Costs_The_Feed_Row()
    {
        var harness = new Harness();
        harness.Devices
            .Setup(d => d.GetActiveForUsersAsync(
                It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("device store is down"));

        await harness.Generator.AppointmentCreatedAsync(
            ClinicId, AppointmentId, ActorId, "Ben Salah", DateTime.UtcNow.AddDays(3));

        Assert.Single(harness.FeedRows);
        Assert.Empty(harness.PushRows);
    }

    // [AC-51] Where push is unavailable nothing is queued at all — a row in a queue that cannot drain is the
    // starvation this design refuses, and the availability seam is asked once rather than per row.
    [Fact]
    public async Task Nothing_Is_Queued_Where_Push_Is_Unavailable()
    {
        var harness = new Harness(pushAvailable: false);

        await harness.Generator.AppointmentCreatedAsync(
            ClinicId, AppointmentId, ActorId, "Ben Salah", DateTime.UtcNow.AddDays(3));

        Assert.Single(harness.FeedRows);
        Assert.Empty(harness.PushRows);
    }

    // ---- The classification itself ----------------------------------------------------------------

    // [AC-43][AC-44] Every category answers, and the five/four split is the spec's. A category added later throws
    // rather than defaulting — a default of `true` would put an unreviewed message on a lock screen, and `false`
    // would look like a decision nobody made.
    [Fact]
    public void Every_Notification_Category_Declares_Whether_It_Reaches_A_Locked_Phone()
    {
        var pushing = Enum.GetValues<NotificationCategory>()
            .Where(StaffNotificationRules.ReachesALockedPhone)
            .ToList();

        Assert.Equal(
            new[]
            {
                NotificationCategory.AppointmentCreated,
                NotificationCategory.AppointmentCancelled,
                NotificationCategory.AppointmentRescheduled,
                NotificationCategory.Reminder,
                NotificationCategory.PostVisitReview
            },
            pushing);
    }
}
