using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Notifications.Commands;
using ClinicManagement.Application.Features.Notifications.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Notifications;

/// <summary>
/// Read-side handler semantics: per-viewer read annotation incl. the late-joiner baseline (US-6), the
/// unread-count delegation (badge independent of the 50-row window), and mark-all clearing every unread.
/// (The SQL-level due-gating / 50-cap / actor-exclusion filters live in the repository and are exercised
/// by integration, not these mock-based unit tests.)
/// </summary>
public class NotificationQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (Mock<IStaffNotificationRepository> repo, Mock<IUserRepository> users, Mock<IClinicContext> ctx, User user) Setup()
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var ctx = new Mock<IClinicContext>();
        ctx.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return (new Mock<IStaffNotificationRepository>(), users, ctx, user);
    }

    /// <summary>
    /// The handler gained the two reads behind « Contacter {fournisseur} » on a « Stock faible » row. Every
    /// notification in this fixture is <c>AppointmentCreated</c>, so the resolution short-circuits before it
    /// touches either mock — which is why bare mocks preserve each case's original assertion exactly.
    /// </summary>
    private static GetNotificationsQueryHandler HandlerFor(
        Mock<IStaffNotificationRepository> repo, Mock<IUserRepository> users, Mock<IClinicContext> ctx) =>
        new(repo.Object, new Mock<IStockItemRepository>().Object, new Mock<ISupplierRepository>().Object,
            users.Object, ctx.Object);

    private static StaffNotification Notification(DateTime effectiveFeedTime) =>
        new(Guid.NewGuid(), ClinicId, NotificationCategory.AppointmentCreated, "T", "M",
            effectiveFeedTime, NotificationTargetKind.Appointment, appointmentId: Guid.NewGuid());

    // [US-6] isRead = has read marker OR effective before the viewer's join baseline.
    [Fact]
    public async Task GetNotifications_Annotates_IsRead_By_Marker_And_Baseline()
    {
        var (repo, users, ctx, user) = Setup();
        var beforeJoin = Notification(user.CreatedAt.AddHours(-1)); // pre-join → read
        var afterJoinUnread = Notification(user.CreatedAt.AddHours(1)); // post-join, no marker → unread
        var afterJoinRead = Notification(user.CreatedAt.AddHours(2)); // post-join, has marker → read

        repo.Setup(r => r.GetRecentForUserAsync(ClinicId, user.Id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { beforeJoin, afterJoinUnread, afterJoinRead });
        repo.Setup(r => r.GetReadNotificationIdsAsync(user.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { afterJoinRead.Id });

        var handler = HandlerFor(repo, users, ctx);
        var result = await handler.Handle(new GetNotificationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dtos = result.Value!.ToDictionary(d => d.Id, d => d.IsRead);
        Assert.True(dtos[beforeJoin.Id]);        // baseline
        Assert.False(dtos[afterJoinUnread.Id]);  // genuinely unread
        Assert.True(dtos[afterJoinRead.Id]);     // read marker
    }

    [Fact]
    public async Task GetNotifications_Fails_When_Unauthenticated()
    {
        var (repo, users, _, _) = Setup();
        var ctx = new Mock<IClinicContext>();
        ctx.Setup(c => c.GetUserId()).Returns((string?)null);

        var handler = HandlerFor(repo, users, ctx);
        var result = await handler.Handle(new GetNotificationsQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        repo.Verify(r => r.GetRecentForUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [US-6] The badge count comes straight from the repo aggregate (not capped at the 50-row window).
    [Fact]
    public async Task GetUnreadCount_Returns_Repository_Count()
    {
        var (repo, users, ctx, user) = Setup();
        repo.Setup(r => r.CountUnreadAsync(ClinicId, user.Id, user.CreatedAt, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(137);

        var handler = new GetUnreadCountQueryHandler(repo.Object, users.Object, ctx.Object);
        var result = await handler.Handle(new GetUnreadCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(137, result.Value);
    }

    // [US-6] Mark-all inserts a read marker for every currently-unread notification, then commits once.
    [Fact]
    public async Task MarkAll_Marks_Every_Unread_And_Saves_Once()
    {
        var (repo, users, ctx, user) = Setup();
        var unreadIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        repo.Setup(r => r.GetUnreadIdsForUserAsync(ClinicId, user.Id, user.CreatedAt, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unreadIds);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new MarkAllNotificationsReadCommandHandler(repo.Object, users.Object, ctx.Object, uow.Object);
        var result = await handler.Handle(new MarkAllNotificationsReadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddReadMarkerAsync(It.IsAny<NotificationRead>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkAll_With_No_Unread_Does_Not_Save()
    {
        var (repo, users, ctx, user) = Setup();
        repo.Setup(r => r.GetUnreadIdsForUserAsync(ClinicId, user.Id, user.CreatedAt, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        var uow = new Mock<IUnitOfWork>();

        var handler = new MarkAllNotificationsReadCommandHandler(repo.Object, users.Object, ctx.Object, uow.Object);
        var result = await handler.Handle(new MarkAllNotificationsReadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddReadMarkerAsync(It.IsAny<NotificationRead>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
