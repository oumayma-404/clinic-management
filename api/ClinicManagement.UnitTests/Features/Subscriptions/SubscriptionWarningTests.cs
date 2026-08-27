using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// The subscription-expiry warnings (<c>clinic-subscription</c> Part E · FR-5 · AC-3.4–3.7).
///
/// <para>These run the <b>real</b> <see cref="NotificationGenerator"/> over an in-memory feed rather than asserting
/// that the job called a mock, because every acceptance criterion here is about the <b>rows</b>: four of them, each
/// genuinely new so it badges the bell, none of them a fifth, and all of them withdrawn on an extension. A mocked
/// generator would prove the job invoked a method and nothing about any of that.</para>
///
/// <para>Every case drives the pass over a <b>fixed</b> clinic-local today, and « simulating days −8 → 0 » is done by
/// running the same job repeatedly against a moving date — which is the only way to see that the fourth threshold
/// still produces an unread row after the first three have been read.</para>
/// </summary>
public class SubscriptionWarningTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime EndsOn = new(2026, 8, 20);

    // ---- The in-memory feed -----------------------------------------------------------------------

    /// <summary>
    /// The three members the warning pair actually uses, over a list. Everything else throws: a fake that quietly
    /// answers a read this feature does not make would let a wrong implementation pass by taking another path.
    /// </summary>
    private sealed class FeedRepository : IStaffNotificationRepository
    {
        public List<StaffNotification> Rows { get; } = new();

        public Task AddAsync(StaffNotification notification, CancellationToken cancellationToken = default)
        {
            Rows.Add(notification);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(StaffNotification notification, CancellationToken cancellationToken = default)
        {
            Rows.Remove(notification);
            return Task.CompletedTask;
        }

        public Task<StaffNotification?> GetSubscriptionWarningAsync(
            Guid clinicId, int thresholdDays, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(
                n => n.ClinicId == clinicId
                     && n.Category == NotificationCategory.SubscriptionExpiring
                     && n.SubscriptionThresholdDays == thresholdDays));

        public Task<IReadOnlyList<StaffNotification>> GetSubscriptionWarningsAsync(
            Guid clinicId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StaffNotification>>(Rows
                .Where(n => n.ClinicId == clinicId && n.Category == NotificationCategory.SubscriptionExpiring)
                .ToList());

        // The WhatsApp-forfait pair (vendor-whatsapp-messaging-quota FR-6) throws like every other member this
        // feature does not use: a subscription warning must never reach a messaging read, and answering one here
        // would let that mistake pass.
        public Task<StaffNotification?> GetMessagingWarningAsync(
            Guid clinicId, string monthKey, int thresholdPercent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<StaffNotification>> GetMessagingWarningsAsync(
            Guid clinicId, string? monthKey = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StaffNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<StaffNotification>> GetRecentForUserAsync(
            Guid clinicId, string userId, DateTime nowUtc, int take, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int> CountUnreadAsync(
            Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffNotification>> GetUnreadForUserAsync(
            Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<Guid>> GetUnreadIdsForUserAsync(
            Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<Guid>> GetReadNotificationIdsAsync(
            string userId, IReadOnlyCollection<Guid> notificationIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReadMarkerExistsAsync(
            Guid notificationId, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task AddReadMarkerAsync(NotificationRead read, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<StaffNotification?> GetReminderByAppointmentAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffNotification?> GetPostVisitReviewByAppointmentAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffNotification?> GetStockExpiringSoonByItemAsync(
            Guid stockItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffNotification?> GetBackupStaleAsync(
            Guid clinicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffNotification?> GetArchiveStaleAsync(
            Guid clinicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffNotification>> GetPendingReviewsForUserAsync(
            Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public FeedRepository Feed { get; } = new();
        public Mock<ITenantScope> TenantScope { get; } = new();
        public Mock<IAuditActorProvider> AuditActor { get; } = new();
        public ClinicSubscription? Subscription { get; set; }

        private readonly SubscriptionWarningJob _job;

        public Harness(ClinicSubscription? subscription, bool requiresSubscription = true)
        {
            Subscription = subscription;

            var clinics = new Mock<IClinicRepository>();
            clinics.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new Clinic(ClinicId, "Cabinet Test", city: "Tunis") });

            var subscriptions = new Mock<IClinicSubscriptionRepository>();
            subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Subscription);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var policy = new Mock<ISubscriptionPolicy>();
            policy.Setup(p => p.RequiresSubscription).Returns(requiresSubscription);

            var generator = new NotificationGenerator(
                Feed, Mock.Of<IDoctorRepository>(), unitOfWork.Object, Mock.Of<IRealtimeNotifier>(),
                NullLogger<NotificationGenerator>.Instance);

            _job = new SubscriptionWarningJob(
                clinics.Object, subscriptions.Object, generator, policy.Object,
                AuditActor.Object, TenantScope.Object, NullLogger<SubscriptionWarningJob>.Instance);
        }

        /// <summary>Runs the daily pass as if today were <paramref name="clinicToday"/>.</summary>
        public Task RunOn(DateTime clinicToday) => _job.WarnExpiringSubscriptions(clinicToday);

        public IReadOnlyList<StaffNotification> Warnings => Feed.Rows
            .Where(n => n.Category == NotificationCategory.SubscriptionExpiring)
            .ToList();

        public IReadOnlyList<int?> Thresholds => Warnings.Select(n => n.SubscriptionThresholdDays).ToList();
    }

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

    private static ClinicSubscription OpenEnded()
    {
        var subscription = ClinicSubscription.For(ClinicId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.OpenEnded(
                ClinicId, SubscriptionPeriodKind.Grandfathered,
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        }, DateTime.UtcNow);
        return subscription;
    }

    // ---- AC-3.4: four rows, and only four -----------------------------------------------------------

    // [AC-3.4][FR-5] Simulating days −8 → 0 produces exactly FOUR rows, one per threshold. Day −8 is outside the
    // window and must produce none, which is what makes « from 7 days » a boundary rather than « soon ».
    [Fact]
    public async Task Simulating_The_Countdown_Produces_Exactly_Four_Rows_One_Per_Threshold()
    {
        var harness = new Harness(EndingOn(EndsOn));

        for (var daysOut = 8; daysOut >= 0; daysOut--)
        {
            await harness.RunOn(EndsOn.AddDays(-daysOut));
        }

        Assert.Equal(4, harness.Warnings.Count);
        Assert.Equal(new int?[] { 7, 3, 1, 0 }, harness.Thresholds);
    }

    // [AC-3.4] Eight days out is one day too early — nothing is written at all.
    [Fact]
    public async Task Outside_The_Window_Nothing_Is_Written()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-8));

        Assert.Empty(harness.Warnings);
    }

    // [AC-3.4] Each row is genuinely NEW — its own id and its own threshold — which is what badges the bell. A
    // restated single row would carry the read markers of the warning already dismissed, so the last three would be
    // invisible to exactly the person paying attention. Asserted as distinct ids, the property that makes it true.
    [Fact]
    public async Task Every_Threshold_Gets_Its_Own_Row_Rather_Than_A_Restatement()
    {
        var harness = new Harness(EndingOn(EndsOn));

        foreach (var daysOut in new[] { 7, 3, 1, 0 })
        {
            await harness.RunOn(EndsOn.AddDays(-daysOut));
        }

        Assert.Equal(4, harness.Warnings.Select(n => n.Id).Distinct().Count());
    }

    // [AC-3.4] The countdown is in the title, so four rows in one bell are distinguishable without reading them.
    [Fact]
    public async Task The_Title_Names_The_Threshold_And_The_Message_Names_The_Date()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-3));

        var row = Assert.Single(harness.Warnings);
        Assert.Contains("3 jours", row.Title);
        Assert.Contains("20/08/2026", row.Message);
        // Says what still works before what will not — it is read chairside (SubscriptionRefusals' own rule).
        Assert.Contains("consulter et exporter", row.Message);
    }

    // ---- AC-3.5: idempotence -----------------------------------------------------------------------

    // [AC-3.5] Running the job twice on the same day adds nothing.
    [Fact]
    public async Task Running_Twice_On_The_Same_Day_Adds_Nothing()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-3));
        await harness.RunOn(EndsOn.AddDays(-3));

        Assert.Single(harness.Warnings);
    }

    // [AC-3.5] And the daily pass never yields a FIFTH row, however many days the countdown runs — six days inside
    // the 7-day threshold still produce one row for it, because dedupe is per (cabinet, threshold).
    [Fact]
    public async Task A_Countdown_That_Sits_Inside_One_Threshold_For_Days_Yields_One_Row()
    {
        var harness = new Harness(EndingOn(EndsOn));

        foreach (var daysOut in new[] { 7, 6, 5, 4 })
        {
            await harness.RunOn(EndsOn.AddDays(-daysOut));
        }

        var row = Assert.Single(harness.Warnings);
        Assert.Equal(7, row.SubscriptionThresholdDays);
    }

    // [AC-3.5] The row's wording must not churn either: it is derived from the THRESHOLD, not from the live
    // countdown, so four days of re-running leave the message byte-identical. A message rebuilt from « days
    // remaining » would differ daily, restate, and make every open browser refetch — the churn the dedupe exists to
    // prevent, and invisible to a test that only counts rows.
    [Fact]
    public async Task The_Wording_Does_Not_Change_While_The_Threshold_Holds()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-7));
        var first = Assert.Single(harness.Warnings).Message;
        await harness.RunOn(EndsOn.AddDays(-4));

        Assert.Equal(first, Assert.Single(harness.Warnings).Message);
    }

    // [FR-5] A job that did not run for four days produces the row for the threshold the cabinet is actually at,
    // not the one it slept through — « largest reached », so a missed run does not warn about 7 days on day 1.
    [Fact]
    public async Task A_Missed_Run_Warns_At_The_Threshold_Actually_Reached()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-1));

        Assert.Equal(1, Assert.Single(harness.Warnings).SubscriptionThresholdDays);
    }

    // ---- FR-5: the re-arm --------------------------------------------------------------------------

    // [FR-5] Extending past 7 days clears the outstanding rows, and approaching again later warns again — all four
    // times. Clearing is not tidiness: it IS the re-arm, since dedupe would otherwise suppress every threshold the
    // cabinet had already crossed once.
    [Fact]
    public async Task Extending_Past_The_Window_Clears_The_Rows_And_Re_Arms_Every_Threshold()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-7));
        await harness.RunOn(EndsOn.AddDays(-1));
        Assert.Equal(2, harness.Warnings.Count);

        // The vendor grants a year; the same day's pass now sees a date far out of the window.
        var extended = EndsOn.AddYears(1);
        harness.Subscription = EndingOn(extended);
        await harness.RunOn(EndsOn.AddDays(-1));
        Assert.Empty(harness.Warnings);

        // A year later it approaches again, and is warned all four times rather than none.
        foreach (var daysOut in new[] { 7, 3, 1, 0 })
        {
            await harness.RunOn(extended.AddDays(-daysOut));
        }

        Assert.Equal(new int?[] { 7, 3, 1, 0 }, harness.Thresholds);
    }

    // [FR-5] A grant that moves the end date without changing which threshold the cabinet is at restates the row in
    // place — same id, new date. The message names the end date, so leaving it would tell the cabinet it expires on
    // a day it does not; a second row for the same threshold would be the fifth notification AC-3.5 forbids.
    [Fact]
    public async Task A_Grant_That_Keeps_The_Threshold_Restates_The_Row_In_Place()
    {
        var today = EndsOn.AddDays(-6); // 6 days out → threshold 7
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(today);
        var id = Assert.Single(harness.Warnings).Id;

        harness.Subscription = EndingOn(EndsOn.AddDays(1)); // now 7 days out → still threshold 7
        await harness.RunOn(today);

        var row = Assert.Single(harness.Warnings);
        Assert.Equal(id, row.Id);
        Assert.Equal(7, row.SubscriptionThresholdDays);
        Assert.Contains("21/08/2026", row.Message);
    }

    // [AC-3.5] A grant that moves the cabinet to a DIFFERENT threshold inside the window writes a genuinely **new**
    // row — new id, unread, so it badges the bell — rather than rewriting the old one. Rewriting would carry the
    // read markers of a warning already dismissed, so « 3 jours restants » would land silently on a bell that had
    // been cleared. That half is unchanged and is what this case exists to protect.
    //
    // ⚠️ **What changed: the superseded row is now withdrawn rather than kept as history.** It used to survive, so
    // the bell showed « il vous reste 1 jour … se termine le 21/08 » beside « 3 jours … 22/08 » — two rows
    // asserting two different end dates, one of them false. The sibling case above already held that a row naming a
    // superseded date must be corrected (« leaving it would tell the cabinet it expires on a day it does not »);
    // this only applies the same rule when the threshold moved too. Withdrawing a warning is not foreign to the
    // design either — `ClearSubscriptionWarningsAsync` does it wholesale for FR-5's re-arm. The « rendez-vous
    // reporté » analogy does not hold: that row is a record of something that happened, while this one is a live
    // claim about a date.
    [Fact]
    public async Task A_Grant_That_Moves_The_Threshold_Writes_A_New_Row_Rather_Than_Rewriting_The_Old_One()
    {
        var today = EndsOn.AddDays(-1); // 1 day out → threshold 1
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(today);
        var supersededId = Assert.Single(harness.Warnings).Id;

        harness.Subscription = EndingOn(EndsOn.AddDays(2)); // now 3 days out → threshold 3
        await harness.RunOn(today);

        var row = Assert.Single(harness.Warnings);
        Assert.NotEqual(supersededId, row.Id); // a NEW row, not the old one rewritten — the load-bearing half
        Assert.Equal(3, row.SubscriptionThresholdDays);
        Assert.Contains("22/08/2026", row.Message);
    }

    // The other direction, and the reason the withdrawal is keyed on the message rather than on « the threshold
    // changed »: a countdown advancing 7 → 3 with the date untouched must keep both rows. They agree about when the
    // entitlement ends, so neither is false, and the earlier one is the record of a warning already given.
    [Fact]
    public async Task A_Countdown_Advancing_With_The_Date_Unchanged_Keeps_Both_Rows()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-7)); // threshold 7
        await harness.RunOn(EndsOn.AddDays(-3)); // threshold 3, same end date

        Assert.Equal(new int?[] { 7, 3 }, harness.Thresholds);
        Assert.All(harness.Warnings, w => Assert.Contains("20/08/2026", w.Message));
    }

    // ---- The two states left exactly as they are ---------------------------------------------------

    // [AC-3.4] Past the end date the cabinet is not warned again — AC-3.4's four warnings are the ones BEFORE it
    // stops being able to work, and a fifth would contradict AC-3.5.
    [Fact]
    public async Task An_Expired_Cabinet_Gets_No_Further_Row()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(5));

        Assert.Empty(harness.Warnings);
    }

    // [AC-3.4] And crucially its EXISTING rows are not withdrawn: the cabinet is now meeting a refused save, and
    // those rows are what explain it. Clearing here would be the same branch as the re-arm, which is why it is
    // asserted rather than left to read correctly.
    [Fact]
    public async Task An_Expired_Cabinet_Keeps_The_Warnings_It_Was_Already_Given()
    {
        var harness = new Harness(EndingOn(EndsOn));

        foreach (var daysOut in new[] { 7, 3, 1, 0 })
        {
            await harness.RunOn(EndsOn.AddDays(-daysOut));
        }

        await harness.RunOn(EndsOn.AddDays(1));

        Assert.Equal(4, harness.Warnings.Count);
    }

    // [EC-11] A suspended cabinet is neither warned nor cleared. The reader surfaces no countdown for one on
    // purpose, and « votre abonnement se termine dans 3 jours » would send a practice suspended for another reason
    // to pay for something that will not unblock it.
    [Fact]
    public async Task A_Suspended_Cabinet_Is_Neither_Warned_Nor_Cleared()
    {
        var subscription = EndingOn(EndsOn);
        var harness = new Harness(subscription);
        await harness.RunOn(EndsOn.AddDays(-7));
        Assert.Single(harness.Warnings);

        subscription.Suspend("Impayé", "vendor", new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc));
        await harness.RunOn(EndsOn.AddDays(-1));

        var row = Assert.Single(harness.Warnings);
        Assert.Equal(7, row.SubscriptionThresholdDays);
    }

    // [AC-1.2][AC-7.1] An open-ended entitlement is never warned about — there is no date to count down to, which
    // is every grandfathered cabinet and every cabinet on a deployment that does not enforce subscriptions.
    [Fact]
    public async Task An_Open_Ended_Entitlement_Is_Never_Warned_About()
    {
        var harness = new Harness(OpenEnded());

        await harness.RunOn(new DateTime(2026, 8, 13));

        Assert.Empty(harness.Warnings);
    }

    // ---- The job's own obligations -----------------------------------------------------------------

    // [AC-7.1][AC-7.2] Where subscriptions are not enforced the pass does nothing at all — it does not even read a
    // clinic. Program.cs does not register it there, and this is the second line of defence for an install
    // reprofiled while a recurring registration sits in Hangfire storage.
    [Fact]
    public async Task Where_Subscriptions_Are_Not_Enforced_The_Pass_Does_Nothing()
    {
        var harness = new Harness(EndingOn(EndsOn), requiresSubscription: false);

        await harness.RunOn(EndsOn.AddDays(-1));

        Assert.Empty(harness.Warnings);
        harness.TenantScope.Verify(
            t => t.UseSystemWide(It.IsAny<string>()), Times.Never);
    }

    // [US-2 R-1] The pass declares a system-wide tenant scope and names itself to the audit ledger. Without the
    // first, both entitlement tables are filtered to nothing and the job logs a clean run having warned nobody —
    // which is the failure mode that reads as « the feature works, no cabinet is expiring ».
    [Fact]
    public async Task The_Pass_Declares_A_System_Wide_Scope_And_Names_Itself()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-1));

        harness.TenantScope.Verify(t => t.UseSystemWide(It.IsAny<string>()), Times.Once);
        harness.AuditActor.Verify(a => a.RunAs(nameof(SubscriptionWarningJob)), Times.Once);
    }

    // [FR-13] A cabinet with no entitlement row is a fault on our side (Part A gives every cabinet one and
    // verify-schema asserts it). It must not be warned about a date we do not have — and must not have rows cleared
    // either, since they may be the only record of a warning already given.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Is_Skipped_Rather_Than_Warned()
    {
        var harness = new Harness(subscription: null);

        await harness.RunOn(EndsOn.AddDays(-1));

        Assert.Empty(harness.Warnings);
    }

    // ---- AC-3.6 / AC-3.7 --------------------------------------------------------------------------

    // [AC-3.6] The category never reaches a locked phone. Asserted against the rule the push fan-out actually
    // reads, not against the decorator — that switch is the single decision point, and it THROWS on an
    // unclassified category, so this also pins that the rules half landed in the same commit (R-9).
    [Fact]
    public void The_Warning_Never_Reaches_A_Locked_Phone()
    {
        Assert.False(StaffNotificationRules.ReachesALockedPhone(NotificationCategory.SubscriptionExpiring));
    }

    // [R-9] And notification writes in every OTHER category still work — the proof the same commit classified the
    // new one. Omitting it would make this throw for all nine, i.e. break every notification write in the product
    // rather than only the new one.
    [Fact]
    public void Every_Other_Category_Is_Still_Classified()
    {
        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            StaffNotificationRules.ReachesALockedPhone(category);
        }
    }

    // [AC-3.7] Addressed to the whole practice: no actor is excluded and no single user is targeted, so every role
    // sees it. An actor id would hide the row from whoever « caused » it — and nobody causes a date arriving.
    [Fact]
    public async Task The_Warning_Is_Addressed_To_The_Whole_Practice()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-1));

        var row = Assert.Single(harness.Warnings);
        Assert.Null(row.ActorUserId);
        Assert.Null(row.TargetUserId);
    }

    // [AC-3.4] It deep-links to « Abonnement » and carries no id, like the recall and backup alerts.
    [Fact]
    public async Task The_Warning_Deep_Links_To_The_Subscription_Screen()
    {
        var harness = new Harness(EndingOn(EndsOn));

        await harness.RunOn(EndsOn.AddDays(-1));

        var row = Assert.Single(harness.Warnings);
        Assert.Equal(NotificationTargetKind.Subscription, row.TargetKind);
        Assert.Null(row.AppointmentId);
        Assert.Null(row.StockItemId);
    }
}
