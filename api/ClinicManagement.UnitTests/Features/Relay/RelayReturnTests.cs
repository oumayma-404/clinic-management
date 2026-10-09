using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The return (<c>clinic-pc-copy</c> D18, US-5, FR-6, FR-11, EC-6, EC-7) on the cloud: the two phases that keep the
/// cabinet writable on one side only, the decisions about each returned row, and the alarms when it does not land.
/// </summary>
public class RelayReturnTests
{
    private const string Build = "20261008_Return+1.0.0+aaaaaaaaaaaa";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static ClinicRelay HoldingRelay(DateTime now, DateTime? since = null)
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", now.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, Build, now.AddDays(-1));
        relay.RecordHeartbeat(Beat(holding: true, since: since ?? now.AddMinutes(-30)), 40, now);
        return relay;
    }

    private static RelayHeartbeat Beat(bool holding = false, DateTime? since = null, DateTime? stuck = null) =>
        new(40, 100, true, 0, 0, null, false, Build, null, null, null, null,
            Holding: holding, HoldingSinceUtc: since, ReturnStuckSinceUtc: stuck);

    // ---- the two phases --------------------------------------------------------------------------------------------

    // [D18] Phase 1 keeps the cloud fenced — the PC may still be taking saves until it hears the answer — and a save
    // there is told « Retour au cloud en cours » (AC-5.2).
    [Fact]
    public void An_Applied_Handback_Keeps_The_Cloud_Fenced_And_Says_The_Return_Is_Under_Way()
    {
        var now = DateTime.UtcNow;
        var relay = HoldingRelay(now);
        var id = Guid.NewGuid();

        relay.RecordHandbackApplied(id, now.AddMinutes(-30), now);

        Assert.True(ClinicWriteLease.IsCloudFenced(relay, now));
        Assert.True(relay.IsReturning);
        var (error, code) = RelayRefusals.ForFencedCloud(relay.PcHoldingSinceUtc, now, relay.IsReturning);
        Assert.Equal(RelayRefusals.HandingBackCode, code);
        Assert.Equal("Retour au cloud en cours — réessayez dans quelques secondes.", error);
        Assert.False(relay.HasReleasedReturn(id));
    }

    // [D18] Phase 2: only the handback the cloud applied releases it, once.
    [Fact]
    public void Only_The_Applied_Handback_Releases_The_Cloud_And_Only_Once()
    {
        var now = DateTime.UtcNow;
        var relay = HoldingRelay(now);
        var id = Guid.NewGuid();
        relay.RecordHandbackApplied(id, now.AddMinutes(-30), now);

        Assert.False(relay.ConfirmReturn(Guid.NewGuid(), now));
        Assert.NotNull(relay.PcHoldingSinceUtc);

        Assert.True(relay.ConfirmReturn(id, now));
        Assert.Null(relay.PcHoldingSinceUtc);
        Assert.False(relay.IsReturning);
        Assert.Equal(now, relay.ReturnedAtUtc);
        Assert.True(relay.HasReleasedReturn(id));
        Assert.False(relay.ConfirmReturn(id, now));
    }

    // [D18] A « holding » heartbeat of the cut already handed back, arriving late, is no new takeover — a newer one is.
    [Fact]
    public void A_Late_Heartbeat_Of_The_Returned_Cut_Does_Not_Fence_The_Cloud_Again()
    {
        var now = DateTime.UtcNow;
        var cut = now.AddMinutes(-30);
        var relay = HoldingRelay(now, cut);
        var id = Guid.NewGuid();
        relay.RecordHandbackApplied(id, cut, now);
        relay.ConfirmReturn(id, now);

        relay.RecordHeartbeat(Beat(holding: true, since: cut), 40, now.AddSeconds(5));
        Assert.Null(relay.PcHoldingSinceUtc);

        relay.RecordHeartbeat(Beat(holding: true, since: now.AddSeconds(8)), 40, now.AddSeconds(10));
        Assert.NotNull(relay.PcHoldingSinceUtc);
    }

    // [AC-5.9] The stuck moment is the PC's, never in the cloud's future, and it means nothing once the PC holds nothing.
    [Fact]
    public void A_Stuck_Return_Is_Recorded_Only_While_The_Pc_Holds()
    {
        var now = DateTime.UtcNow;
        var relay = HoldingRelay(now);

        relay.RecordHeartbeat(Beat(holding: true, since: now.AddMinutes(-30), stuck: now.AddHours(2)), 40, now);
        Assert.Equal(now, relay.ReturnStuckSinceUtc);

        relay.RecordHeartbeat(Beat(holding: false, stuck: now.AddMinutes(-20)), 40, now);
        Assert.Null(relay.ReturnStuckSinceUtc);
    }

    [Fact]
    public void Reprendre_La_Main_Ends_A_Return_Under_Way()
    {
        var now = DateTime.UtcNow;
        var relay = HoldingRelay(now);
        relay.RecordHandbackApplied(Guid.NewGuid(), now.AddMinutes(-30), now);

        relay.Reclaim("local|admin", now);

        Assert.False(relay.IsReturning);
    }

    // [AC-5.2] The PC's own refusal during the return names it, not « copie » nor « retiré ».
    [Fact]
    public void A_Pc_Handing_Back_Refuses_With_The_Return_Sentence()
    {
        Assert.Equal(RelayRefusals.HandingBackCode, RelayRefusals.ForPcNotHolding(retired: false, handingBack: true).Code);
        Assert.Equal(RelayRefusals.RetiredCode, RelayRefusals.ForPcNotHolding(retired: true, handingBack: true).Code);
        Assert.Equal(RelayRefusals.StandbyCode, RelayRefusals.ForPcNotHolding(retired: false).Code);
    }

    // ---- the heartbeat: phase 2 --------------------------------------------------------------------------------------

    private static async Task<(RelayHeartbeatAck Ack, List<AuditEntry> Journal)> BeatAsync(
        ClinicRelay relay, bool holding, Guid? returned, DateTime? since = null)
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(relay.Subject);
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetByIdAcrossClinicsAsync(relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(relay);
        var rows = new Mock<IClinicRelayRowStore>();
        rows.Setup(r => r.HighWaterAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(40);
        rows.Setup(r => r.FeedEpochAsync(It.IsAny<CancellationToken>())).ReturnsAsync("e1");
        var build = new Mock<IRelayBuildInfo>();
        build.Setup(b => b.Current).Returns(Build);
        var journal = new List<AuditEntry>();
        var audit = new Mock<IAuditEntryRepository>();
        audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => journal.AddRange(e))
            .Returns(Task.CompletedTask);

        var handler = new RelayHeartbeatCommandHandler(context.Object, relays.Object, new TenantScope(NullLogger<TenantScope>.Instance),
            rows.Object, build.Object, audit.Object, new Mock<IUnitOfWork>().Object, NullLogger<RelayHeartbeatCommandHandler>.Instance);
        var result = await handler.Handle(new RelayHeartbeatCommand(new RelayHeartbeatRequest(
            40, 100, true, 0, 0, null, false, Build, DateTime.UtcNow, null, null, null, null,
            Holding: holding, HoldingSinceUtc: since, ReturnedHandbackId: returned)), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return (result.Value!, journal);
    }

    // [D18, FR-8] The PC no longer holding, naming the handback the cloud applied: the cloud takes the saves back, says
    // so in the journal, and answers « released » so the PC can forget it.
    [Fact]
    public async Task The_Pcs_Word_That_It_Stopped_Holding_Releases_The_Cloud()
    {
        var now = DateTime.UtcNow;
        var relay = HoldingRelay(now);
        var id = Guid.NewGuid();
        relay.RecordHandbackApplied(id, now.AddMinutes(-30), now);

        var (ack, journal) = await BeatAsync(relay, holding: false, returned: id);

        Assert.True(ack.ReturnReleased);
        Assert.Null(relay.PcHoldingSinceUtc);
        Assert.Contains(journal, e => e.ChangedFields!.StartsWith(RelayJournal.Returned, StringComparison.Ordinal));
    }

    // [D18] A heartbeat that still holds — a NEW cut — never releases the cloud, whatever handback it names.
    [Fact]
    public async Task A_Pc_Still_Holding_Never_Releases_The_Cloud()
    {
        var now = DateTime.UtcNow;
        var relay = HoldingRelay(now);
        var id = Guid.NewGuid();
        relay.RecordHandbackApplied(id, now.AddMinutes(-30), now);

        var (ack, journal) = await BeatAsync(relay, holding: true, returned: id, since: now.AddMinutes(-30));

        Assert.False(ack.ReturnReleased);
        Assert.NotNull(relay.PcHoldingSinceUtc);
        Assert.Empty(journal);
    }

    // ---- the decisions -----------------------------------------------------------------------------------------------

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RelayHandbackRequest Request(
        IEnumerable<RelayHandbackChange> changes, IEnumerable<RelayRow> rows) =>
        new(Guid.NewGuid(), 40, DateTime.UtcNow, changes.ToList(), rows.ToList(),
            Array.Empty<RelayHandbackJournalEntry>(), Array.Empty<RelaySignInTrace>(), Array.Empty<RelayRecoveryCodeUse>());

    private static RelayHandbackChange Change(string table, string key, string? save = null, long seq = 1, bool deleted = false) =>
        new(seq, table, key, deleted, save, DateTime.UtcNow);

    private static RelayCloudChange Cloud(string table, string key, string? save = null, long seq = 41, bool fromRelay = false) =>
        new(seq, table, key, false, fromRelay, save, DateTime.UtcNow);

    private static readonly Func<string, JsonElement, IEnumerable<RelayRowKey>> NoReferences =
        (_, _) => Enumerable.Empty<RelayRowKey>();

    // The desk moved a visit on the PC, a doctor at home moved it in the cloud 10 s before the cut: the CLOUD's version
    // stays (owner, 2026-10-09: cloud work is never destroyed), the desk's is listed to apply, and the PC takes the cloud's.
    [Fact]
    public void The_Clouds_Version_Is_Never_Replaced_And_The_Cabinets_Is_Listed()
    {
        var request = Request(new[] { Change("Appointment", "a1") }, new[] { new RelayRow("Appointment", "a1", Json("{\"Id\":\"a1\"}")) });

        var plan = RelayHandbackPlanner.Plan(request, new[] { Cloud("Appointment", "a1") }, NoReferences);

        Assert.Empty(plan.Apply);
        Assert.Equal(new RelayRowKey("Appointment", "a1"), Assert.Single(plan.Kept!));
        var line = Assert.Single(plan.Review);
        Assert.Equal(RelayReviewKind.BothChanged, line.Kind);
    }

    // A deletion on the PC never removes a row the cloud changed meanwhile.
    [Fact]
    public void A_Deletion_On_The_Pc_Never_Removes_A_Row_The_Cloud_Changed()
    {
        var request = Request(new[] { Change("Appointment", "a1", deleted: true) }, new[] { new RelayRow("Appointment", "a1", null) });

        var plan = RelayHandbackPlanner.Plan(request, new[] { Cloud("Appointment", "a1") }, NoReferences);

        Assert.Empty(plan.Apply);
        Assert.Single(plan.Kept!);
    }

    // What only the cabinet changed still comes back as before.
    [Fact]
    public void A_Row_Only_The_Cabinet_Changed_Is_Applied()
    {
        var request = Request(new[] { Change("Appointment", "a1") }, new[] { new RelayRow("Appointment", "a1", Json("{\"Id\":\"a1\"}")) });

        var plan = RelayHandbackPlanner.Plan(request, Array.Empty<RelayCloudChange>(), NoReferences);

        Assert.Equal("a1", Assert.Single(plan.Apply).Key);
        Assert.Empty(plan.Kept!);
    }

    // [FR-11] Accounts and the subscription are never taken from the PC; a practitioner record the cloud changed during
    // the cut stays the cloud's, and is listed.
    [Fact]
    public void Accounts_Are_Never_Returned_And_A_Practitioner_The_Cloud_Changed_Stays_The_Clouds()
    {
        var request = Request(
            new[] { Change("User", "local|u1"), Change("Doctor", "d1"), Change("Doctor", "d2") },
            new[]
            {
                new RelayRow("User", "local|u1", Json("{}")),
                new RelayRow("Doctor", "d1", Json("{}")),
                new RelayRow("Doctor", "d2", Json("{}")),
            });

        var plan = RelayHandbackPlanner.Plan(request, new[] { Cloud("Doctor", "d1"), Cloud("User", "local|u1") }, NoReferences);

        Assert.Equal(new[] { "d2" }, plan.Apply.Select(r => r.Key));
        Assert.Equal(("Doctor", "d1"), (Assert.Single(plan.Review).Key.Table, plan.Review[0].Key.Key));
    }

    // [EC-6, D17] A payment that reached the cloud at the cut and was pressed again on the PC — nothing points to the PC's
    // copy: it is dropped, so there is one payment after the return.
    [Fact]
    public void A_Save_Recorded_On_Both_Sides_Is_Dropped_When_Nothing_Points_To_It()
    {
        var request = Request(new[] { Change("Expense", "pc-e", save: "k1") }, new[] { new RelayRow("Expense", "pc-e", Json("{}")) });

        var plan = RelayHandbackPlanner.Plan(request, new[] { Cloud("Expense", "cloud-e", save: "k1") }, NoReferences);

        Assert.Empty(plan.Apply);
        Assert.Equal("pc-e", Assert.Single(plan.Dropped).Key);
        Assert.Empty(plan.Review);
    }

    // [D17] A note reprinted on the PC and a payment taken on it afterwards: never dropped — both kept, listed once as a
    // probable duplicate beside the cloud's own note.
    [Fact]
    public void A_Duplicate_Something_Points_To_Is_Kept_And_Listed_Once()
    {
        var request = Request(
            new[] { Change("Invoice", "pc-n", "k1"), Change("InvoiceLine", "pc-l", "k1"), Change("Payment", "pc-p", "k2", seq: 2) },
            new[]
            {
                new RelayRow("Invoice", "pc-n", Json("{}")),
                new RelayRow("InvoiceLine", "pc-l", Json("{}")),
                new RelayRow("Payment", "pc-p", Json("{}")),
            });
        IEnumerable<RelayRowKey> Refs(string table, JsonElement _) => table switch
        {
            "InvoiceLine" or "Payment" => new[] { new RelayRowKey("Invoice", "pc-n") },
            _ => Array.Empty<RelayRowKey>(),
        };

        var plan = RelayHandbackPlanner.Plan(request,
            new[] { Cloud("Invoice", "cloud-n", "k1"), Cloud("InvoiceLine", "cloud-l", "k1") }, Refs);

        Assert.Empty(plan.Dropped);
        Assert.Equal(3, plan.Apply.Count);
        var line = Assert.Single(plan.Review);
        Assert.Equal(RelayReviewKind.ProbableDuplicate, line.Kind);
        Assert.Equal(new RelayRowKey("Invoice", "pc-n"), line.Key);
        Assert.Equal(new RelayRowKey("Invoice", "cloud-n"), line.CloudKey);
    }

    // [AC-5.6] What the cloud changed that the PC never received is kept and listed — but not the cloud's own FR-11
    // writes during the cut, nor delivery state.
    [Fact]
    public void Changes_The_Pc_Never_Received_Are_Listed_Except_The_Clouds_Own_And_Delivery_State()
    {
        var request = Request(Array.Empty<RelayHandbackChange>(), Array.Empty<RelayRow>());

        var plan = RelayHandbackPlanner.Plan(request, new[]
        {
            Cloud("Patient", "p1"), Cloud("User", "local|u1"), Cloud("StaffNotification", "s1"), Cloud("Notification", "n1"),
        }, NoReferences);

        var line = Assert.Single(plan.Review);
        Assert.Equal((RelayReviewKind.CloudOnly, "p1"), (line.Kind, line.Key.Key));
    }

    // [D18] A second attempt after a lost answer: the rows an earlier return wrote come back as the cabinet's own —
    // never a conflict, never a duplicate.
    [Fact]
    public void Rows_An_Earlier_Return_Wrote_Are_Never_A_Conflict()
    {
        var request = Request(new[] { Change("Expense", "e1", "k1") }, new[] { new RelayRow("Expense", "e1", Json("{}")) });

        var plan = RelayHandbackPlanner.Plan(request, new[] { Cloud("Expense", "e1", "k1", fromRelay: true) }, NoReferences);

        Assert.Single(plan.Apply);
        Assert.Empty(plan.Dropped);
        Assert.Empty(plan.Review);
    }

    // Derived guard: the return's two lists are exactly the tables the fenced cloud keeps writing (FR-11).
    [Fact]
    public void The_Returns_Rules_Cover_Exactly_The_Fenced_Clouds_Tables()
    {
        var mine = RelayHandbackRules.NeverReturned.Concat(RelayHandbackRules.CloudKeepsIfChanged).OrderBy(t => t).ToList();

        Assert.Equal(RelayFence.AllowedOnFencedCloud.Keys.OrderBy(t => t), mine);
        Assert.Empty(RelayHandbackRules.NeverReturned.Intersect(RelayHandbackRules.CloudKeepsIfChanged));
    }

    // ---- files ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("clinics/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/patients/scan.png", true)]
    [InlineData("clinics/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/patients/scan.png", false)]
    [InlineData("clinics/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/../bbbbbbbb/scan.png", false)]
    [InlineData("flat-legacy-key.png", false)]
    [InlineData("", false)]
    public void A_Return_Brings_Only_Files_Under_The_Cabinets_Own_Prefix(string key, bool accepted) =>
        Assert.Equal(accepted, RelayHandbackFiles.IsClinicKey(ClinicId, key));

    // ---- the handler -------------------------------------------------------------------------------------------------

    private sealed class Harness
    {
        public ClinicRelay Relay { get; }
        public Mock<IRelayHandbackStore> Store { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<RelayReviewItem> Existing { get; } = new();
        public List<RelayReviewItem> Added { get; } = new();
        public List<AuditEntry> Journal { get; } = new();
        public List<string> Steps { get; } = new();

        public Harness(ClinicRelay relay)
        {
            Relay = relay;
            Store.Setup(s => s.CloudChangesAfterAsync(ClinicId, 40, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new RelayCloudChange(41, "Appointment", "a1", false, false, null, DateTime.UtcNow) });
            Store.Setup(s => s.CurrentRowsAsync(ClinicId, It.IsAny<IReadOnlyCollection<RelayRowKey>>(), It.IsAny<CancellationToken>()))
                .Callback(() => Steps.Add("read cloud versions"))
                .ReturnsAsync(new Dictionary<RelayRowKey, string> { [new RelayRowKey("Appointment", "a1")] = "{\"cloud\":1}" });
            Store.Setup(s => s.AuthorsAsync(ClinicId, It.IsAny<IReadOnlyCollection<RelayRowKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<RelayRowKey, string> { [new RelayRowKey("Appointment", "a1")] = "dr@cabinet.tn" });
            Store.Setup(s => s.References(It.IsAny<string>(), It.IsAny<JsonElement>())).Returns(Array.Empty<RelayRowKey>());
            Store.Setup(s => s.ApplyReturnAsync(ClinicId, It.IsAny<RelayHandbackRequest>(), It.IsAny<RelayHandbackPlan>(), It.IsAny<CancellationToken>()))
                .Callback(() => Steps.Add("apply"))
                .Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => Steps.Add("begin")).Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => Steps.Add("save")).ReturnsAsync(1);
            UnitOfWork.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => Steps.Add("commit")).Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => Steps.Add("rollback")).Returns(Task.CompletedTask);
        }

        public async Task<ClinicManagement.Application.Common.Models.Result<RelayHandbackResultDto>> SendAsync(
            RelayHandbackRequest request, string build = Build)
        {
            var context = new Mock<IClinicContext>();
            context.Setup(c => c.GetUserId()).Returns(Relay.Subject);
            var relays = new Mock<IClinicRelayRepository>();
            relays.Setup(r => r.GetByIdAcrossClinicsAsync(Relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Relay);
            var buildInfo = new Mock<IRelayBuildInfo>();
            buildInfo.Setup(b => b.Current).Returns(Build);
            var reviews = new Mock<IRelayReviewItemRepository>();
            reviews.Setup(r => r.GetForCutAsync(Relay.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Existing);
            reviews.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<RelayReviewItem>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<RelayReviewItem>, CancellationToken>((i, _) => Added.AddRange(i))
                .Returns(Task.CompletedTask);
            var audit = new Mock<IAuditEntryRepository>();
            audit.Setup(a => a.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((e, _) => Journal.AddRange(e))
                .Returns(Task.CompletedTask);

            var handler = new HandBackRelayCommandHandler(context.Object, relays.Object, new TenantScope(NullLogger<TenantScope>.Instance),
                buildInfo.Object, Store.Object, reviews.Object, audit.Object, UnitOfWork.Object,
                NullLogger<HandBackRelayCommandHandler>.Instance);
            return await handler.Handle(new HandBackRelayCommand(request, build), CancellationToken.None);
        }
    }

    private static RelayHandbackRequest CutRequest(Guid? id = null, DateTime? since = null) =>
        new(id ?? Guid.NewGuid(), 40, since ?? DateTime.UtcNow.AddMinutes(-30),
            new[] { Change("Appointment", "a1") },
            new[] { new RelayRow("Appointment", "a1", Json("{\"desk\":1}")) },
            new[] { new RelayHandbackJournalEntry("local|desk", "desk@cabinet.tn", "Appointment", "a1", (int)AuditAction.Update, "StartsAt", DateTime.UtcNow.AddMinutes(-5)) },
            Array.Empty<RelaySignInTrace>(), Array.Empty<RelayRecoveryCodeUse>());

    // [D18, AC-5.5, AC-5.6] One transaction: the cloud's versions are read BEFORE the cabinet's are applied, the journal
    // arrives under its author marked « via PC de secours », the pair is listed with both versions, phase 1 recorded.
    [Fact]
    public async Task The_Cut_Is_Applied_Once_With_Its_Journal_And_Its_Review_List()
    {
        var harness = new Harness(HoldingRelay(DateTime.UtcNow));
        var request = CutRequest();

        var result = await harness.SendAsync(request);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "begin", "read cloud versions", "apply", "save", "commit" }, harness.Steps);
        var entry = Assert.Single(harness.Journal);
        Assert.Equal("local|desk", entry.UserId);
        Assert.Equal("Via PC de secours · StartsAt", entry.ChangedFields);
        var item = Assert.Single(harness.Added);
        Assert.Equal(RelayReviewKind.BothChanged, item.Kind);
        Assert.Equal("{\"cloud\":1}", item.CloudVersion);
        Assert.Equal("{\"desk\":1}", item.CabinetVersion);
        Assert.Equal("dr@cabinet.tn", item.CloudChangedBy);
        Assert.Equal(request.HandbackId, harness.Relay.HandbackAppliedId);
        Assert.True(harness.Relay.IsReturning);
    }

    // [D18] The same handback again — its answer was lost — applies nothing and answers « already applied ».
    [Fact]
    public async Task A_Repeated_Handback_Applies_Nothing()
    {
        var harness = new Harness(HoldingRelay(DateTime.UtcNow));
        var request = CutRequest();
        await harness.SendAsync(request);
        harness.Steps.Clear();

        var again = await harness.SendAsync(request);

        Assert.True(again.Value!.AlreadyApplied);
        Assert.Empty(harness.Steps);
    }

    // [AC-5.6] A second attempt for the same cut lists a record once — the first versions are the ones worth reading.
    [Fact]
    public async Task A_Record_Already_Listed_For_This_Cut_Is_Not_Listed_Again()
    {
        var since = DateTime.UtcNow.AddMinutes(-30);
        var harness = new Harness(HoldingRelay(DateTime.UtcNow, since));
        harness.Existing.Add(new RelayReviewItem(ClinicId, harness.Relay.Id, since, RelayReviewKind.BothChanged, "Appointment", "a1",
            "{\"first\":1}", null, null, null, null, DateTime.UtcNow));

        await harness.SendAsync(CutRequest(since: since));

        Assert.Empty(harness.Added);
    }

    [Fact]
    public async Task A_Failure_Rolls_Everything_Back_And_Says_The_Pc_Keeps_The_Work()
    {
        var harness = new Harness(HoldingRelay(DateTime.UtcNow));
        harness.Store.Setup(s => s.ApplyReturnAsync(ClinicId, It.IsAny<RelayHandbackRequest>(), It.IsAny<RelayHandbackPlan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("23P01"));

        var result = await harness.SendAsync(CutRequest());

        Assert.Equal(RelayRefusals.HandbackFailedCode, result.Code);
        Assert.Contains("rollback", harness.Steps);
        Assert.DoesNotContain("commit", harness.Steps);
        Assert.Null(harness.Relay.HandbackAppliedId);
    }

    [Fact]
    public async Task A_Pc_On_Another_Build_Or_Holding_Nothing_Is_Refused_Before_Anything_Moves()
    {
        var harness = new Harness(HoldingRelay(DateTime.UtcNow));
        Assert.Equal(RelayRefusals.VersionMismatchCode, (await harness.SendAsync(CutRequest(), build: "old")).Code);

        var (idle, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", DateTime.UtcNow);
        idle.Pair("PC", "key", null, null, Build, DateTime.UtcNow);
        var notHolding = new Harness(idle);
        Assert.Equal(RelayRefusals.NotHoldingCode, (await notHolding.SendAsync(CutRequest())).Code);

        Assert.Empty(harness.Steps);
        Assert.Empty(notHolding.Steps);
    }

    // ---- the alarms (AC-5.9, AC-9.2) ---------------------------------------------------------------------------------

    [Fact]
    public void A_Return_Stuck_For_Fifteen_Minutes_Reaches_The_Admins_And_The_Vendor_At_Any_Hour()
    {
        var now = new DateTime(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc); // 03:00 in Tunis — closed
        var relay = HoldingRelay(now);
        relay.RecordHeartbeat(Beat(holding: true, since: now.AddMinutes(-60), stuck: now.AddMinutes(-14)), 40, now);

        Assert.DoesNotContain(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), now), r => r.Alert == RelayAlert.ReturnStuck);
        Assert.DoesNotContain(RelayIncidentKind.ReturnStuck, RelayVendorAlertRules.Due(relay, null, Array.Empty<RelayIncidentKind>(), now));

        var later = now.AddMinutes(2);
        Assert.Contains(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), later), r => r.Alert == RelayAlert.ReturnStuck);
        Assert.Contains(RelayIncidentKind.ReturnStuck, RelayVendorAlertRules.Due(relay, null, Array.Empty<RelayIncidentKind>(), later));
        Assert.Contains("retour au cloud bloqué", RelayVendorAlertEmail.Subject("Cabinet", RelayIncidentKind.ReturnStuck));
    }
}
