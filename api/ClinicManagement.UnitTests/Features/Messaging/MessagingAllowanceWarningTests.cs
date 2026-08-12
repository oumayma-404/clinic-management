using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// The three WhatsApp-forfait warnings (<c>vendor-whatsapp-messaging-quota</c> Part 2 · FR-6 · AC-3.1–3.7).
///
/// <para>These run the <b>real</b> <see cref="NotificationGenerator"/> over an in-memory feed rather than asserting
/// that a mock was called, on <c>SubscriptionWarningTests</c>' reasoning: every acceptance criterion here is about the
/// <b>rows</b> — three of them from one jump, each genuinely new so it badges the bell, the untrue ones withdrawn, and
/// last month's swept so all three re-arm. A mocked generator would prove a method was invoked and nothing about any
/// of that.</para>
///
/// <para>Each case drives the pass over a <b>fixed</b> clinic-local today, which is what
/// <c>MessagingAllowanceJob.ReviewMessagingAllowances(DateTime)</c> exists for: the month boundary is the only
/// boundary that matters here and a fixture reading the clock would agree with a clock-dependent implementation by
/// construction.</para>
/// </summary>
public class MessagingAllowanceWarningTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime August = new(2026, 8, 12);
    private static readonly DateTime September = new(2026, 9, 3);
    private const string AugustKey = "2026-08";
    private const string SeptemberKey = "2026-09";

    // ---- The pure threshold arithmetic (FR-6) -------------------------------------------------------

    /// <summary>
    /// AC-3.1 — <b>every</b> threshold crossed, not the largest. The 80 % row is the one that could still have been
    /// acted on, so a jump from 70 % to 100 % in one afternoon has to yield three.
    ///
    /// <para>This is deliberately the opposite of <c>SubscriptionStateReader.ThresholdReached</c>, and asserting it
    /// here is what stops somebody "aligning" the two.</para>
    /// </summary>
    [Theory]
    [InlineData(0, 200, new int[0])]
    [InlineData(159, 200, new int[0])]   // 79 % — one send short of the first threshold
    [InlineData(160, 200, new[] { 80 })] // exactly 80 %
    [InlineData(190, 200, new[] { 80, 95 })]
    [InlineData(200, 200, new[] { 80, 95, 100 })]
    [InlineData(250, 200, new[] { 80, 95, 100 })] // over-consumed: a cancelled top-up (AC-7.4), still three
    public void Every_Crossed_Threshold_Is_Reported(int consumed, int allowance, int[] expected) =>
        Assert.Equal(expected, MessagingAllowanceThresholds.Crossed(consumed, allowance));

    /// <summary>
    /// FR-6 — a cabinet the vendor has allowed <b>no</b> messages produces the 100 % row alone. Nothing is 80 % of
    /// zero, and three rows would be three restatements of one fact.
    /// </summary>
    [Fact]
    public void A_Zero_Allowance_Produces_The_Hundred_Percent_Row_Only()
    {
        Assert.Equal(new[] { 100 }, MessagingAllowanceThresholds.Crossed(0, 0));
        Assert.Equal(new[] { 100 }, MessagingAllowanceThresholds.Crossed(5, 0));
    }

    // ---- The rows the daily pass produces ----------------------------------------------------------

    /// <summary>
    /// AC-3.1/3.2 — three thresholds crossed at once are three <b>separate</b> rows, each with its own id so each
    /// badges the bell. One reworded row would leave the last two invisible to whoever had already read the first.
    /// </summary>
    [Fact]
    public async Task Three_Thresholds_Crossed_At_Once_Produce_Three_Distinct_Rows()
    {
        var harness = new Harness(consumed: 200, allowance: 200);

        await harness.RunOn(August);

        Assert.Equal(new[] { 80, 95, 100 }, harness.Thresholds);
        Assert.Equal(3, harness.Warnings.Select(n => n.Id).Distinct().Count());
        Assert.All(harness.Warnings, n => Assert.Equal(AugustKey, n.MessagingAllowanceMonth));
    }

    /// <summary>
    /// AC-3.5 — the wording is derived from the threshold, the allowance and the month, so a threshold that holds for
    /// days restates nothing. <b>The highest-value case in this file</b>: keying the message on the live consumed
    /// count compiles, produces perfectly plausible French, and rewrites the row on every send the cabinet makes —
    /// making every open browser refetch minutely, which no row count would notice.
    /// </summary>
    [Fact]
    public async Task The_Wording_Does_Not_Change_While_More_Is_Consumed_Under_The_Same_Threshold()
    {
        var harness = new Harness(consumed: 160, allowance: 200);
        await harness.RunOn(August);

        var first = Assert.Single(harness.Warnings);
        var id = first.Id;
        var message = first.Message;

        // Ten more sends, still inside the 80 % band.
        harness.SetConsumed(170);
        await harness.RunOn(August);

        var second = Assert.Single(harness.Warnings);
        Assert.Equal(id, second.Id);          // the same row — not withdrawn and rewritten
        Assert.Equal(message, second.Message); // and not restated
        Assert.DoesNotContain("170", second.Message);
    }

    /// <summary>AC-3.3 — clinic-wide, no actor, no target user, deep-linking to « Rappels ».</summary>
    [Fact]
    public async Task The_Row_Is_Clinic_Wide_And_Deep_Links_To_Rappels()
    {
        var harness = new Harness(consumed: 200, allowance: 200);

        await harness.RunOn(August);

        Assert.All(harness.Warnings, n =>
        {
            Assert.Null(n.ActorUserId);
            Assert.Null(n.TargetUserId);
            Assert.Equal(NotificationTargetKind.MessagingAllowance, n.TargetKind);
            Assert.Equal(NotificationCategory.MessagingAllowanceLow, n.Category);
        });
    }

    /// <summary>
    /// AC-3.4 — <b>never</b> an OS push. Asserted rather than left to the absence of a call, because the criterion is
    /// honoured by <i>classifying</i> the category and not by omitting it: <c>ReachesALockedPhone</c> is a total switch
    /// that <b>throws</b> on an unclassified member, so leaving it out would break every notification write in the
    /// product rather than only this one (the R-9 split-point guard, one feature over).
    /// </summary>
    [Fact]
    public void The_Category_Is_Classified_As_Never_Reaching_A_Locked_Phone()
    {
        Assert.False(StaffNotificationRules.ReachesALockedPhone(NotificationCategory.MessagingAllowanceLow));

        // And every other category still answers, so this feature cannot have been made to pass by omission.
        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            StaffNotificationRules.ReachesALockedPhone(category);
        }
    }

    /// <summary>
    /// AC-3.6 — a grant that puts the cabinet back below a crossed threshold <b>withdraws</b> the rows it no longer
    /// meets, and <b>keeps</b> the ones it still does. The bell must never assert two states of one month; and the kept
    /// row keeps its id, because a rewritten row is a new row whose read markers do not survive.
    /// </summary>
    [Fact]
    public async Task A_Grant_Withdraws_The_Thresholds_No_Longer_Met_And_Keeps_The_Rest()
    {
        var harness = new Harness(consumed: 200, allowance: 200);
        await harness.RunOn(August);
        var eightyPercentRowId = harness.Warnings.Single(n => n.MessagingThresholdPercent == 80).Id;

        // The vendor grants +300 for this month: 200/500 is 40 %, so none of the three holds.
        harness.SetAllowance(500);
        await harness.RunOn(August);
        Assert.Empty(harness.Warnings);

        // And a smaller grant leaves the one still met standing, with its identity intact.
        var second = new Harness(consumed: 200, allowance: 200);
        await second.RunOn(August);
        eightyPercentRowId = second.Warnings.Single(n => n.MessagingThresholdPercent == 80).Id;
        second.SetAllowance(240); // 200/240 = 83 % — 80 holds, 95 and 100 do not
        await second.RunOn(August);

        Assert.Equal(new[] { 80 }, second.Thresholds);
        Assert.Equal(eightyPercentRowId, second.Warnings.Single().Id);
    }

    /// <summary>
    /// AC-3.7 — at a new month the previous month's rows are withdrawn and all three thresholds are <b>re-armed</b>: a
    /// cabinet busy in August and busy again in September is warned both times.
    /// </summary>
    [Fact]
    public async Task A_New_Month_Withdraws_Last_Months_Rows_And_Re_Arms_The_Thresholds()
    {
        var harness = new Harness(consumed: 200, allowance: 200);
        await harness.RunOn(August);
        var augustIds = harness.Warnings.Select(n => n.Id).ToList();
        Assert.Equal(3, augustIds.Count);

        // September: a fresh counting row, and the cabinet crosses 80 % again.
        harness.RollOverTo(SeptemberKey, consumed: 160, allowance: 200);
        await harness.RunOn(September);

        var warnings = harness.Warnings;
        Assert.Equal(new[] { 80 }, warnings.Select(n => n.MessagingThresholdPercent!.Value).ToList());
        Assert.Equal(SeptemberKey, warnings.Single().MessagingAllowanceMonth);
        // Genuinely new, so it badges a bell that had been read clear in August.
        Assert.DoesNotContain(warnings.Single().Id, augustIds);
    }

    /// <summary>
    /// FR-6 — a cabinet whose subscription has lapsed is <b>not</b> warned: it is already refused for a reason this
    /// warning does not explain, and « 95 % de votre forfait » would send it to buy messages it cannot send anyway.
    /// </summary>
    [Fact]
    public async Task An_Expired_Cabinet_Is_Not_Warned()
    {
        var harness = new Harness(consumed: 200, allowance: 200, subscriptionAllowsWrites: false);

        await harness.RunOn(August);

        Assert.Empty(harness.Warnings);
    }

    /// <summary>
    /// EC-16 — where the deployment does not sell vendor messaging the pass does <b>nothing at all</b>: no row is
    /// provisioned, no warning is written, and the clinic list is never even read.
    /// </summary>
    [Fact]
    public async Task The_Pass_Reads_Nothing_Where_The_Deployment_Does_Not_Sell_Vendor_Messaging()
    {
        var harness = new Harness(consumed: 200, allowance: 200, sellsVendorMessaging: false);

        await harness.RunOn(August);

        Assert.Empty(harness.Warnings);
        Assert.Equal(0, harness.ClinicReads);
    }

    // ---- Provisioning (FR-1a), the pass's first duty ------------------------------------------------

    /// <summary>
    /// FR-1a — a cabinet with no counting row for the month gets one, at the <b>folded</b> allowance rather than the
    /// configured default: a cabinet whose vendor has changed its standing figure must not have the setting written
    /// back over it.
    /// </summary>
    [Fact]
    public async Task A_Missing_Counting_Row_Is_Provisioned_From_The_Fold()
    {
        var harness = new Harness(month: null, standingAllowance: 350);

        await harness.RunOn(August);

        var row = Assert.Single(harness.Months);
        Assert.Equal(AugustKey, row.MonthKey);
        Assert.Equal(350, row.AllowanceMessages);
        Assert.Equal(0, row.ConsumedMessages);
    }

    /// <summary>
    /// AC-4.3 — a cabinet whose ledger reaches this month with <b>nothing at all</b> gets <b>no row</b>. A zeroed row
    /// would turn our own bookkeeping gap into the statement « the vendor allowed this practice nothing », and it would
    /// make « non mesuré » unreachable on the history screen for ever.
    /// </summary>
    [Fact]
    public async Task A_Cabinet_With_No_Allowance_Record_Gets_No_Counting_Row()
    {
        var harness = new Harness(month: null, standingAllowance: null);

        await harness.RunOn(August);

        Assert.Empty(harness.Months);
        Assert.Empty(harness.Warnings);
    }

    /// <summary>
    /// R-6 — the stored snapshot is rewritten from the fold when the two disagree. The refold is the primary writer;
    /// this is the reconciling backstop <c>verify-schema</c> would otherwise only be able to <i>report</i>.
    /// </summary>
    [Fact]
    public async Task A_Snapshot_That_Drifted_From_The_Ledger_Is_Rewritten()
    {
        var harness = new Harness(consumed: 10, allowance: 200, standingAllowance: 500);

        await harness.RunOn(August);

        Assert.Equal(500, harness.Months.Single().AllowanceMessages);
        Assert.Equal(10, harness.Months.Single().ConsumedMessages); // consumption is never touched
    }

    /// <summary>
    /// R-9 — provisioning runs <b>first</b>, so a failure in the warning reconciliation cannot cost the cabinet its
    /// counting row for the day. Asserted by making the feed throw: the row must still be there afterwards.
    /// </summary>
    [Fact]
    public async Task A_Failing_Warning_Pass_Does_Not_Cost_The_Cabinet_Its_Counting_Row()
    {
        var harness = new Harness(month: null, standingAllowance: 200);
        harness.Feed.FailOnRead = true;

        await harness.RunOn(August); // must not throw — the pass is per-cabinet, per-duty guarded

        Assert.Single(harness.Months);
    }

    // ---- The wording, held against MessagingRefusals ------------------------------------------------

    /// <summary>
    /// AC-4.2 — <b>no clinic-facing sentence may promise that the held reminders go out on the 1st.</b> A reminder is
    /// measured against the forfait when it comes due, so by the time the month turns its appointment has passed and it
    /// is refused as obsolete. Asserted over the strings rather than checked by eye.
    ///
    /// <para>The 100 % warning and <c>MessagingRefusals.Exhausted</c> both name the renewal date; what neither may do
    /// is attach it to the <i>reminders</i>. Both offer the top-up as the remedy instead.</para>
    /// </summary>
    [Fact]
    public async Task No_Sentence_Promises_That_Held_Reminders_Go_Out_At_The_Rollover()
    {
        var harness = new Harness(consumed: 200, allowance: 200);
        await harness.RunOn(August);

        var sentences = harness.Warnings.Select(n => n.Message)
            .Append(MessagingRefusals.Exhausted(new DateTime(2026, 9, 1)))
            .Append(MessagingRefusals.ParkedExhausted(new DateTime(2026, 9, 1)))
            .Append(MessagingRefusals.RecallExhausted)
            .ToList();

        foreach (var sentence in sentences)
        {
            // The two shapes that would make the false promise: naming the renewal as when the reminders leave, or
            // saying they go out « le 1er ». The remedy is always the top-up.
            Assert.DoesNotContain("partiront le", sentence);
            Assert.DoesNotContain("dès le renouvellement", sentence);
            Assert.DoesNotContain("au renouvellement", sentence);
        }

        var hundred = harness.Warnings.Single(n => n.MessagingThresholdPercent == 100).Message;
        Assert.Contains("dès que nous augmentons votre forfait", hundred);
        // AC-2.6 — SMS is named in every warning, because the first fear read chairside is « are my patients no
        // longer being reminded at all? ».
        Assert.All(harness.Warnings, n => Assert.Contains("SMS", n.Message));
    }

    // ---- Harness -----------------------------------------------------------------------------------

    /// <summary>
    /// The two members the forfait warning pair uses, over a list. Everything else throws, on
    /// <c>SubscriptionWarningTests</c>' rule: a fake that quietly answers a read this feature does not make would let a
    /// wrong implementation pass by taking another path.
    /// </summary>
    private sealed class FeedRepository : IStaffNotificationRepository
    {
        public List<StaffNotification> Rows { get; } = new();

        /// <summary>Makes the warning duty fail, so R-9's per-duty isolation can be asserted.</summary>
        public bool FailOnRead { get; set; }

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

        public Task<StaffNotification?> GetMessagingWarningAsync(
            Guid clinicId, string monthKey, int thresholdPercent, CancellationToken cancellationToken = default)
        {
            Fail();
            return Task.FromResult(Rows.FirstOrDefault(
                n => n.ClinicId == clinicId
                     && n.Category == NotificationCategory.MessagingAllowanceLow
                     && n.MessagingAllowanceMonth == monthKey
                     && n.MessagingThresholdPercent == thresholdPercent));
        }

        public Task<IReadOnlyList<StaffNotification>> GetMessagingWarningsAsync(
            Guid clinicId, string? monthKey = null, CancellationToken cancellationToken = default)
        {
            Fail();
            return Task.FromResult<IReadOnlyList<StaffNotification>>(Rows
                .Where(n => n.ClinicId == clinicId
                            && n.Category == NotificationCategory.MessagingAllowanceLow
                            && (monthKey == null || n.MessagingAllowanceMonth == monthKey))
                .ToList());
        }

        private void Fail()
        {
            if (FailOnRead)
            {
                throw new InvalidOperationException("Feed unavailable");
            }
        }

        public Task<StaffNotification?> GetSubscriptionWarningAsync(
            Guid clinicId, int thresholdDays, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<StaffNotification>> GetSubscriptionWarningsAsync(
            Guid clinicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<IReadOnlyList<StaffNotification>> GetPendingReviewsForUserAsync(
            Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public FeedRepository Feed { get; } = new();
        public List<ClinicMessagingMonth> Months { get; } = new();
        public int ClinicReads { get; private set; }

        private readonly MessagingAllowanceJob _job;
        private readonly List<MessagingAllowanceEntry> _entries = new();

        /// <summary>A cabinet whose counting row already exists.</summary>
        public Harness(
            int consumed,
            int allowance,
            int? standingAllowance = null,
            bool subscriptionAllowsWrites = true,
            bool sellsVendorMessaging = true)
            : this(
                BuildMonth(allowance, consumed),
                standingAllowance ?? allowance,
                subscriptionAllowsWrites,
                sellsVendorMessaging)
        {
        }

        /// <summary>A cabinet with no row yet (or none at all), and a ledger that may reach this month or not.</summary>
        public Harness(ClinicMessagingMonth? month, int? standingAllowance)
            : this(month, standingAllowance, subscriptionAllowsWrites: true, sellsVendorMessaging: true)
        {
        }

        private Harness(
            ClinicMessagingMonth? month,
            int? standingAllowance,
            bool subscriptionAllowsWrites,
            bool sellsVendorMessaging)
        {
            if (month is not null)
            {
                Months.Add(month);
            }

            if (standingAllowance is { } figure)
            {
                _entries.Add(MessagingAllowanceEntry.Create(
                    ClinicId, MessagingAllowanceKind.Standing, figure, AugustKey,
                    recordedAtUtc: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                    recordedBy: "job|test"));
            }

            var clinics = new Mock<IClinicRepository>();
            clinics.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    ClinicReads++;
                    return new[] { new Clinic(ClinicId, "Cabinet Test", city: "Tunis") };
                });

            var allowances = new Mock<IMessagingAllowanceRepository>();
            allowances.Setup(a => a.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _entries);
            allowances.Setup(a => a.GetMonthAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string key, CancellationToken _) =>
                    Months.FirstOrDefault(m => m.MonthKey == key));
            allowances.Setup(a => a.AddMonthAsync(It.IsAny<ClinicMessagingMonth>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicMessagingMonth, CancellationToken>((m, _) => Months.Add(m))
                .Returns(Task.CompletedTask);
            allowances.Setup(a => a.UpdateMonthAsync(It.IsAny<ClinicMessagingMonth>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var subscriptions = new Mock<IClinicSubscriptionRepository>();
            subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => subscriptionAllowsWrites ? OpenEnded() : Expired());

            var policy = new Mock<ISubscriptionPolicy>();
            policy.Setup(p => p.RequiresSubscription).Returns(true);

            var availability = new Mock<IVendorMessagingAvailability>();
            availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsVendorMessaging);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var generator = new NotificationGenerator(
                Feed, Mock.Of<IDoctorRepository>(), unitOfWork.Object, Mock.Of<IRealtimeNotifier>(),
                NullLogger<NotificationGenerator>.Instance);

            _job = new MessagingAllowanceJob(
                clinics.Object, allowances.Object, subscriptions.Object, generator, availability.Object,
                policy.Object, unitOfWork.Object, Mock.Of<IAuditActorProvider>(), Mock.Of<ITenantScope>(),
                NullLogger<MessagingAllowanceJob>.Instance);
        }

        public Task RunOn(DateTime clinicToday) => _job.ReviewMessagingAllowances(clinicToday);

        public IReadOnlyList<StaffNotification> Warnings => Feed.Rows
            .Where(n => n.Category == NotificationCategory.MessagingAllowanceLow)
            .OrderBy(n => n.MessagingThresholdPercent)
            .ToList();

        public IReadOnlyList<int> Thresholds =>
            Warnings.Select(n => n.MessagingThresholdPercent!.Value).ToList();

        /// <summary>More sends inside the same month, without moving the allowance.</summary>
        public void SetConsumed(int consumed)
        {
            var row = Months.Single();
            while (row.ConsumedMessages < consumed)
            {
                row.RecordSend(DateTime.UtcNow);
            }
        }

        /// <summary>The vendor grants more for this month — the ledger and the snapshot both move.</summary>
        public void SetAllowance(int allowance)
        {
            _entries.Clear();
            _entries.Add(MessagingAllowanceEntry.Create(
                ClinicId, MessagingAllowanceKind.Standing, allowance, AugustKey,
                recordedAtUtc: new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                recordedBy: "job|test"));
            Months.Single().SetAllowance(allowance, DateTime.UtcNow);
        }

        /// <summary>A new Tunisian month: a fresh counting row beside the old one, which stays as history.</summary>
        public void RollOverTo(string monthKey, int consumed, int allowance)
        {
            var row = ClinicMessagingMonth.For(ClinicId, monthKey, allowance, DateTime.UtcNow);
            for (var i = 0; i < consumed; i++)
            {
                row.RecordSend(DateTime.UtcNow);
            }

            Months.Add(row);
        }

        private static ClinicMessagingMonth BuildMonth(int allowance, int consumed)
        {
            var row = ClinicMessagingMonth.For(ClinicId, AugustKey, allowance, DateTime.UtcNow);
            for (var i = 0; i < consumed; i++)
            {
                row.RecordSend(DateTime.UtcNow);
            }

            return row;
        }

        private static ClinicSubscription OpenEnded()
        {
            var subscription = ClinicSubscription.For(ClinicId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            subscription.RecomputeFrom(new[]
            {
                SubscriptionPeriod.OpenEnded(
                    ClinicId, SubscriptionPeriodKind.Grandfathered,
                    new DateTime(2026, 1, 1), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            }, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            return subscription;
        }

        private static ClinicSubscription Expired()
        {
            var subscription = ClinicSubscription.For(ClinicId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            subscription.RecomputeFrom(new[]
            {
                SubscriptionPeriod.Create(
                    ClinicId, SubscriptionPeriodKind.Paid,
                    recordedOnClinicDay: new DateTime(2026, 1, 1),
                    recordedAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    explicitEndsOn: new DateTime(2026, 2, 1))
            }, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            return subscription;
        }
    }
}
