using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Notifications.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Notifications;

/// <summary>
/// Clearing a notification from one reader's bell: tenant isolation, idempotency, and the property the whole
/// design rests on — <b>nothing is ever deleted</b>, because a <see cref="StaffNotification"/> row with no named
/// target is the same row in every colleague's feed.
///
/// <para>Mirrors <see cref="NotificationTenantIsolationTests"/>, which holds the same contract for the read
/// marker.</para>
/// </summary>
public class NotificationDismissalTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static (DismissNotificationCommandHandler handler, Mock<IStaffNotificationRepository> repo, Mock<IUnitOfWork> uow, User user)
        BuildOne()
    {
        var (repo, uow, users, ctx, user) = Common();
        return (new DismissNotificationCommandHandler(
            repo.Object, users.Object, ctx.Object, uow.Object,
            NullLogger<DismissNotificationCommandHandler>.Instance), repo, uow, user);
    }

    private static (DismissAllNotificationsCommandHandler handler, Mock<IStaffNotificationRepository> repo, Mock<IUnitOfWork> uow, User user)
        BuildAll()
    {
        var (repo, uow, users, ctx, user) = Common();
        return (new DismissAllNotificationsCommandHandler(repo.Object, users.Object, ctx.Object, uow.Object), repo, uow, user);
    }

    private static (Mock<IStaffNotificationRepository>, Mock<IUnitOfWork>, Mock<IUserRepository>, Mock<IClinicContext>, User) Common()
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var ctx = new Mock<IClinicContext>();
        ctx.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var repo = new Mock<IStaffNotificationRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (repo, uow, users, ctx, user);
    }

    private static StaffNotification NotificationInClinic(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, NotificationCategory.LowStock, "T", "M",
            DateTime.UtcNow, NotificationTargetKind.StockItem, stockItemId: Guid.NewGuid());

    [Fact]
    public async Task Dismiss_Returns_NotFound_For_Other_Clinic()
    {
        var (handler, repo, uow, _) = BuildOne();
        var foreign = NotificationInClinic(OtherClinicId);
        repo.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await handler.Handle(new DismissNotificationCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        repo.Verify(r => r.AddDismissalAsync(It.IsAny<NotificationDismissal>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dismiss_Returns_NotFound_When_Missing()
    {
        var (handler, repo, uow, _) = BuildOne();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((StaffNotification?)null);

        var result = await handler.Handle(new DismissNotificationCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dismiss_Adds_Marker_For_Own_Clinic()
    {
        var (handler, repo, uow, user) = BuildOne();
        var own = NotificationInClinic(ClinicId);
        repo.Setup(r => r.GetByIdAsync(own.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        repo.Setup(r => r.DismissalExistsAsync(own.Id, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await handler.Handle(new DismissNotificationCommand { Id = own.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddDismissalAsync(
            It.Is<NotificationDismissal>(d => d.NotificationId == own.Id && d.UserId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dismiss_Is_Idempotent()
    {
        var (handler, repo, uow, user) = BuildOne();
        var own = NotificationInClinic(ClinicId);
        repo.Setup(r => r.GetByIdAsync(own.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        repo.Setup(r => r.DismissalExistsAsync(own.Id, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await handler.Handle(new DismissNotificationCommand { Id = own.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddDismissalAsync(It.IsAny<NotificationDismissal>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The property the whole shape exists for: a colleague's row survives. Deleting would have been the
    /// obvious implementation and it would silently clear a low-stock alert out of every other bell.
    /// </summary>
    [Fact]
    public async Task Dismiss_Never_Removes_The_Shared_Row()
    {
        var (handler, repo, _, user) = BuildOne();
        var own = NotificationInClinic(ClinicId);
        repo.Setup(r => r.GetByIdAsync(own.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        repo.Setup(r => r.DismissalExistsAsync(own.Id, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await handler.Handle(new DismissNotificationCommand { Id = own.Id }, CancellationToken.None);

        repo.Verify(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// « Tout effacer » clears what the reader can SEE, which is why it reads the visible set and not the unread
    /// one — the panel lists read rows too, and clearing only the unread ones would leave the list standing.
    /// </summary>
    [Fact]
    public async Task DismissAll_Marks_Every_Visible_Row_And_Never_The_Unread_Set()
    {
        var (handler, repo, uow, user) = BuildAll();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        repo.Setup(r => r.GetVisibleIdsForUserAsync(ClinicId, user.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        var result = await handler.Handle(new DismissAllNotificationsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        foreach (var id in ids)
        {
            repo.Verify(r => r.AddDismissalAsync(
                It.Is<NotificationDismissal>(d => d.NotificationId == id && d.UserId == user.Id),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        repo.Verify(r => r.GetUnreadIdsForUserAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DismissAll_With_Nothing_Visible_Writes_Nothing()
    {
        var (handler, repo, uow, user) = BuildAll();
        repo.Setup(r => r.GetVisibleIdsForUserAsync(ClinicId, user.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var result = await handler.Handle(new DismissAllNotificationsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddDismissalAsync(It.IsAny<NotificationDismissal>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
