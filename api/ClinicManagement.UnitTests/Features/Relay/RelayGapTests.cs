using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// AC-9.4 / EC-21 on the cloud: a cloud restored from a backup older than its PC de secours is read-only for that
/// cabinet until the PC has sent back what the restore lost — then it keeps whatever it changed itself since the
/// restore, takes everything else from the PC once, and the vendor is told.
/// </summary>
public class RelayGapTests
{
    private const string Build = "20261009_Gap+1.0.0+aaaaaaaaaaaa";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private static ClinicRelay Relay()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", Now.AddDays(-1));
        relay.Pair("PC", "key", null, null, Build, Now.AddDays(-1));
        return relay;
    }

    // ---- the fence -------------------------------------------------------------------------------------------------

    // [AC-9.4] A PC that copied another history holds what the cloud lost: the cloud is read-only for the cabinet —
    // for at most 15 minutes, so a PC unable to send it never keeps the cabinet out of its own cloud.
    [Fact]
    public void A_Pc_Following_Another_History_Fences_The_Cloud_For_At_Most_Fifteen_Minutes()
    {
        var relay = Relay();
        relay.NoteFollowedEpoch("old", "new", pcAppliedSeq: 40, Now);

        Assert.True(relay.IsRecoveringGap(Now));
        Assert.True(ClinicWriteLease.IsCloudFenced(relay, Now));
        Assert.False(relay.IsRecoveringGap(Now + ClinicRelay.GapFenceFor));
        Assert.False(ClinicWriteLease.IsCloudFenced(relay, Now + ClinicRelay.GapFenceFor));

        // A later heartbeat does not restart the clock.
        relay.NoteFollowedEpoch("old", "new", 40, Now.AddMinutes(10));
        Assert.Equal(Now, relay.GapPendingSinceUtc);
    }

    [Theory]
    [InlineData("new", 40)]   // follows this cloud
    [InlineData(null, 40)]    // follows nothing yet, or has just sent its gap
    [InlineData("old", 0)]    // has copied nothing
    public void A_Pc_That_Holds_Nothing_More_Fences_Nothing(string? pcEpoch, long appliedSeq)
    {
        var relay = Relay();
        relay.NoteFollowedEpoch("old", "new", 40, Now);

        relay.NoteFollowedEpoch(pcEpoch, "new", appliedSeq, Now.AddMinutes(1));

        Assert.Null(relay.GapPendingSinceUtc);
        Assert.False(ClinicWriteLease.IsCloudFenced(relay, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_Save_On_A_Recovering_Cloud_Is_Refused_In_Its_Own_Words()
    {
        var (error, code) = RelayRefusals.ForFencedCloud(null, Now, returning: false, restoring: true);

        Assert.Equal(RelayRefusals.RestoringCode, code);
        Assert.Equal(RelayRefusals.Restoring, error);
    }

    // ---- the vendor ------------------------------------------------------------------------------------------------

    // [AC-9.4] « the vendor is alerted » — once: one incident for the day the gap landed.
    [Fact]
    public void The_Vendor_Is_Told_Once_The_Restored_Cloud_Got_Its_Gap()
    {
        var relay = Relay();
        Assert.DoesNotContain(RelayIncidentKind.CloudRestored, RelayVendorAlertRules.Due(relay, null, Array.Empty<RelayIncidentKind>(), Now));

        relay.RecordGapReturned(Guid.NewGuid(), 3, Now);

        Assert.Equal(new[] { RelayIncidentKind.CloudRestored },
            RelayVendorAlertRules.Due(relay, null, Array.Empty<RelayIncidentKind>(), Now.AddHours(1)));
        Assert.DoesNotContain(RelayIncidentKind.CloudRestored,
            RelayVendorAlertRules.Due(relay, null, Array.Empty<RelayIncidentKind>(), Now + RelayVendorAlertRules.CloudRestoredFor));
        Assert.Contains("restauré", RelayVendorAlertEmail.Compose("Cabinet", ClinicId, RelayIncidentKind.CloudRestored, Now, Now).Intro[0]);
    }

    // ---- applying the gap ------------------------------------------------------------------------------------------

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class Harness
    {
        public ClinicRelay Relay { get; } = RelayGapTests.Relay();
        public Mock<IRelayHandbackStore> Store { get; } = new();
        public List<RelayHandbackPlan> Plans { get; } = new();
        public List<RelayReviewItem> Listed { get; } = new();
        public HashSet<RelayRowKey> ChangedSinceRestore { get; } = new();

        public async Task<Result<RelayHandbackResultDto>> SendAsync(RelayGapRequest request, string build = Build)
        {
            var context = new Mock<IClinicContext>();
            context.Setup(c => c.GetUserId()).Returns(Relay.Subject);
            var relays = new Mock<IClinicRelayRepository>();
            relays.Setup(r => r.GetByIdAcrossClinicsAsync(Relay.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Relay);
            var buildInfo = new Mock<IRelayBuildInfo>();
            buildInfo.Setup(b => b.Current).Returns(Build);
            Store.Setup(s => s.ChangedSinceRestoreAsync(ClinicId, It.IsAny<IReadOnlyCollection<RelayRowKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<RelayRowKey> keys, CancellationToken _) =>
                    keys.Where(ChangedSinceRestore.Contains).ToHashSet());
            Store.Setup(s => s.ApplyReturnAsync(ClinicId, It.IsAny<RelayHandbackRequest>(), It.IsAny<RelayHandbackPlan>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, RelayHandbackRequest, RelayHandbackPlan, CancellationToken>((_, _, plan, _) => Plans.Add(plan))
                .Returns(Task.CompletedTask);
            Store.Setup(s => s.CurrentRowsAsync(ClinicId, It.IsAny<IReadOnlyCollection<RelayRowKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<RelayRowKey, string> { [new RelayRowKey("Expense", "e2")] = "{\"Amount\":5}" });
            Store.Setup(s => s.AuthorsAsync(ClinicId, It.IsAny<IReadOnlyCollection<RelayRowKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<RelayRowKey, string> { [new RelayRowKey("Expense", "e2")] = "doc@cloud.tn" });
            var reviews = new Mock<IRelayReviewItemRepository>();
            reviews.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<RelayReviewItem>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<RelayReviewItem>, CancellationToken>((items, _) => Listed.AddRange(items))
                .Returns(Task.CompletedTask);

            var handler = new ReturnRelayGapCommandHandler(context.Object, relays.Object,
                new ClinicManagement.Application.Common.Services.TenantScope(NullLogger<ClinicManagement.Application.Common.Services.TenantScope>.Instance),
                buildInfo.Object, Store.Object, reviews.Object, new Mock<IAuditEntryRepository>().Object,
                new Mock<IUnitOfWork>().Object, NullLogger<ReturnRelayGapCommandHandler>.Instance);
            return await handler.Handle(new ReturnRelayGapCommand(request, build), CancellationToken.None);
        }
    }

    // [AC-9.4] Everything the restore lost comes back from the PC — except what the cloud changed again since its
    // restore, which keeps the cloud's version and is listed with the PC's beside it; accounts never travel (FR-11),
    // and the fence lifts once it has landed.
    [Fact]
    public async Task The_Gap_Is_Applied_Except_What_The_Cloud_Changed_Since_Its_Restore()
    {
        var harness = new Harness();
        harness.Relay.NoteFollowedEpoch("old", "new", 40, Now);
        harness.ChangedSinceRestore.Add(new RelayRowKey("Expense", "e2"));
        harness.ChangedSinceRestore.Add(new RelayRowKey("Notification", "n2"));
        var gapId = Guid.NewGuid();

        var result = await harness.SendAsync(new RelayGapRequest(gapId, new[]
        {
            new RelayRow("Expense", "e1", Json("{\"Amount\":1}")),
            new RelayRow("Expense", "e2", Json("{\"Amount\":2}")),
            new RelayRow("Notification", "n2", Json("{}")),
            new RelayRow("User", "local|u1", Json("{}")),
        }));

        Assert.True(result.IsSuccess, result.Error);
        var plan = Assert.Single(harness.Plans);
        Assert.Equal(new[] { ("Expense", "e1") }, plan.Apply.Select(r => (r.Table, r.Key)));
        var listed = Assert.Single(harness.Listed);
        Assert.Equal(RelayReviewKind.KeptAfterRestore, listed.Kind);
        Assert.Equal("e2", listed.EntityKey);
        Assert.Contains("5", listed.CloudVersion);
        Assert.Contains("2", listed.CabinetVersion);
        Assert.Equal("doc@cloud.tn", listed.CloudChangedBy);
        Assert.Equal(gapId, harness.Relay.LastGapId);
        Assert.Equal(1, harness.Relay.LastGapRows);
        Assert.Null(harness.Relay.GapPendingSinceUtc);
        Assert.Equal(1, result.Value!.Applied);
    }

    [Fact]
    public async Task The_Same_Gap_Twice_Is_Applied_Once_And_Another_Build_Is_Refused()
    {
        var harness = new Harness();
        var gap = new RelayGapRequest(Guid.NewGuid(), new[] { new RelayRow("Expense", "e1", Json("{}")) });

        Assert.True((await harness.SendAsync(gap)).IsSuccess);
        var again = await harness.SendAsync(gap);

        Assert.True(again.Value!.AlreadyApplied);
        Assert.Single(harness.Plans);
        Assert.Equal(RelayRefusals.VersionMismatchCode,
            (await harness.SendAsync(new RelayGapRequest(Guid.NewGuid(), Array.Empty<RelayRow>()), build: "old")).Code);
    }

    // A gap gives back; it never removes anything, and never sends an account.
    [Fact]
    public void A_Gap_Carries_No_Deletion_And_No_Account()
    {
        var rows = ReturnRelayGapCommandHandler.Rows(new RelayGapRequest(Guid.NewGuid(), new[]
        {
            new RelayRow("Expense", "e1", null),
            new RelayRow("UserRecoveryCode", "c1", Json("{}")),
            new RelayRow("Patient", "p1", Json("{\"v\":1}")),
            new RelayRow("Patient", "p1", Json("{\"v\":2}")),
        }));

        var row = Assert.Single(rows);
        Assert.Equal("p1", row.Key);
        Assert.Contains("2", row.Row!.Value.GetRawText());
    }
}
