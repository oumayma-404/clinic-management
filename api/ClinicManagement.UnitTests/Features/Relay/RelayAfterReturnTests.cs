using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// What the cloud does once a cut is back (<c>clinic-pc-copy</c> D18 phase 2, AC-5.4, AC-5.6, AC-5.8): the cut's visits
/// reach Google Agenda, every open screen refreshes, and « Modifications à vérifier » stays on the admins' bell until
/// every line is « Vu ».
/// </summary>
public class RelayAfterReturnTests
{
    private const string Build = "20261008_Return+1.0.0+aaaaaaaaaaaa";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ---- AC-5.8 / AC-5.4 -----------------------------------------------------------------------------------------

    // The cut's rows arrived in SQL, so no command broadcast them: every key the product listens for is sent once.
    [Fact]
    public void Every_Broadcast_Key_Is_Known_And_The_Relay_Area_Is_Not_One()
    {
        var keys = RealtimeResourceResolver.AllKeys();

        Assert.Contains("appointments", keys);
        Assert.Contains("patients", keys);
        Assert.Contains("invoices", keys);
        Assert.DoesNotContain("relay", keys);
        Assert.Equal(keys.Distinct().Count(), keys.Count);
    }

    [Fact]
    public async Task After_The_Return_Every_Visit_Of_The_Cut_Goes_To_Google_And_Every_Screen_Refreshes()
    {
        var since = DateTime.UtcNow.AddMinutes(-1);
        var visit = Guid.NewGuid();
        var store = new Mock<IRelayHandbackStore>();
        store.Setup(s => s.KeysWrittenByReturnAsync(ClinicId, nameof(Appointment), since, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit.ToString("D"), "not-a-guid" });
        var realtime = new Mock<IRealtimeNotifier>();
        var google = new Mock<IAppointmentGoogleSyncDispatcher>();

        await new RelayReturnAftermath(store.Object, realtime.Object, google.Object, NullLogger<RelayReturnAftermath>.Instance)
            .AfterReturnAsync(ClinicId, since, CancellationToken.None);

        google.Verify(g => g.Dispatch(visit, ClinicId), Times.Once);
        google.VerifyNoOtherCalls();
        foreach (var key in RealtimeResourceResolver.AllKeys())
        {
            realtime.Verify(r => r.NotifyEntityChangedAsync(ClinicId, key, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // Best-effort: a store that fails costs the Google push, never the screens' refresh.
    [Fact]
    public async Task A_Failing_Read_Of_The_Cuts_Visits_Still_Refreshes_The_Screens()
    {
        var store = new Mock<IRelayHandbackStore>();
        store.Setup(s => s.KeysWrittenByReturnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));
        var realtime = new Mock<IRealtimeNotifier>();

        await new RelayReturnAftermath(store.Object, realtime.Object, new Mock<IAppointmentGoogleSyncDispatcher>().Object,
            NullLogger<RelayReturnAftermath>.Instance).AfterReturnAsync(ClinicId, DateTime.UtcNow, CancellationToken.None);

        realtime.Verify(r => r.NotifyEntityChangedAsync(ClinicId, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Phase 2 runs the aftermath once, from the moment phase 1 landed — and a heartbeat that releases nothing never does.
    [Fact]
    public async Task The_Heartbeat_That_Releases_The_Cloud_Runs_The_Aftermath_Once()
    {
        var now = DateTime.UtcNow;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", now.AddDays(-1));
        relay.Pair("PC", "key", null, null, Build, now.AddDays(-1));
        relay.RecordHeartbeat(new RelayHeartbeat(40, 100, true, 0, 0, null, false, Build, null, null, null, null,
            Holding: true, HoldingSinceUtc: now.AddMinutes(-30)), 40, now);
        var id = Guid.NewGuid();
        var appliedAt = now.AddSeconds(-2);
        relay.RecordHandbackApplied(id, now.AddMinutes(-30), appliedAt);
        var aftermath = new Mock<IRelayReturnAftermath>();

        await BeatAsync(relay, aftermath.Object, returned: id);
        await BeatAsync(relay, aftermath.Object, returned: id);

        aftermath.Verify(a => a.AfterReturnAsync(ClinicId, appliedAt, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task BeatAsync(ClinicRelay relay, IRelayReturnAftermath aftermath, Guid? returned)
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

        var handler = new RelayHeartbeatCommandHandler(context.Object, relays.Object, new TenantScope(NullLogger<TenantScope>.Instance),
            rows.Object, build.Object, new Mock<IAuditEntryRepository>().Object, new Mock<IUnitOfWork>().Object,
            NullLogger<RelayHeartbeatCommandHandler>.Instance, aftermath);
        var result = await handler.Handle(new RelayHeartbeatCommand(new RelayHeartbeatRequest(
            40, 100, true, 0, 0, null, false, Build, DateTime.UtcNow, null, null, null, null,
            ReturnedHandbackId: returned)), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
    }

    // ---- AC-5.6 — the bell and the list ------------------------------------------------------------------------------

    [Fact]
    public void Lines_Nobody_Has_Marked_Seen_Stay_On_The_Admins_Bell()
    {
        var now = DateTime.UtcNow;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC", "local|admin", now.AddDays(-1));

        var row = Assert.Single(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), now, pendingReviews: 3),
            r => r.Alert == RelayAlert.ToReview);
        Assert.Equal("3 modifications faites dans le cloud juste avant la coupure sont à vérifier.", row.Message);
        Assert.DoesNotContain(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), now), r => r.Alert == RelayAlert.ToReview);
        Assert.Equal("1 modification faite dans le cloud juste avant la coupure est à vérifier.", RelayReviewLabels.BellMessage(1));
    }

    private static (Mock<IClinicContext>, Mock<IUserRepository>) Admin(string role = User.RoleAdmin)
    {
        var user = User.CreateLocalUser(ClinicId, role, "admin@cabinet.tn", "hash", "Admin");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return (context, users);
    }

    private static Mock<IPatientRepository> Patients()
    {
        var patients = new Mock<IPatientRepository>();
        patients.Setup(p => p.GetByIdsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient>());
        return patients;
    }

    private static RelayReviewItem Item(RelayReviewKind kind = RelayReviewKind.BothChanged) =>
        new(ClinicId, Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), kind, "Expense", "e1", "{\"cloud\":1}", "{\"desk\":1}",
            null, DateTime.UtcNow.AddHours(-2), "dr@cabinet.tn", DateTime.UtcNow);

    [Fact]
    public async Task The_List_Names_Each_Line_In_French_With_Both_Versions()
    {
        var (context, users) = Admin();
        var items = new Mock<IRelayReviewItemRepository>();
        items.Setup(i => i.GetPageAsync(ClinicId, false, false, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<RelayReviewItem>(new[] { Item() }, 1, 50, 1));

        var result = await new GetRelayReviewItemsQueryHandler(context.Object, users.Object, items.Object, Patients().Object)
            .Handle(new GetRelayReviewItemsQuery(false, false, null, null), CancellationToken.None);

        var line = Assert.Single(result.Value!.Items);
        Assert.Equal("Modifié des deux côtés — la version du cloud est gardée, celle du cabinet est à reporter si elle est juste", line.KindLabel);
        Assert.Equal("{\"cloud\":1}", line.CloudVersion);
        Assert.Equal("{\"desk\":1}", line.CabinetVersion);
        Assert.Equal("dr@cabinet.tn", line.CloudChangedBy);
    }

    [Fact]
    public async Task Vu_Is_Recorded_Once_And_Only_By_An_Admin()
    {
        var item = Item();
        var (context, users) = Admin();
        var items = new Mock<IRelayReviewItemRepository>();
        items.Setup(i => i.GetByIdAsync(ClinicId, item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        var handler = new MarkRelayReviewItemSeenCommandHandler(context.Object, users.Object, items.Object, new Mock<IUnitOfWork>().Object,
            Patients().Object);

        var first = await handler.Handle(new MarkRelayReviewItemSeenCommand(item.Id), CancellationToken.None);
        var seenAt = item.ReviewedAtUtc;
        await handler.Handle(new MarkRelayReviewItemSeenCommand(item.Id), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.NotNull(seenAt);
        Assert.Equal(seenAt, item.ReviewedAtUtc);

        var missing = await handler.Handle(new MarkRelayReviewItemSeenCommand(Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(MarkRelayReviewItemSeenCommandHandler.NotFoundCode, missing.Code);

        var (secretaryContext, secretaryUsers) = Admin(User.RoleSecretary);
        var refused = await new MarkRelayReviewItemSeenCommandHandler(secretaryContext.Object, secretaryUsers.Object, items.Object,
            new Mock<IUnitOfWork>().Object, Patients().Object).Handle(new MarkRelayReviewItemSeenCommand(item.Id), CancellationToken.None);
        Assert.Equal(RelayRefusals.NotAdminCode, refused.Code);
    }

    [Fact]
    public void Every_Kind_Has_A_French_Name()
    {
        foreach (var kind in Enum.GetValues<RelayReviewKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(RelayReviewLabels.Kind(kind)));
        }
    }
}
