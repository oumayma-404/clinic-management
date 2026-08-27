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
/// Tenant isolation + idempotency for marking a notification read: a notification from another clinic
/// reads as "not found" (never confirming its existence), and re-marking an already-read notification is a
/// no-op. Mirrors <c>AppointmentTenantIsolationTests</c>.
/// </summary>
public class NotificationTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static (MarkNotificationReadCommandHandler handler, Mock<IStaffNotificationRepository> repo, Mock<IUnitOfWork> uow, User user)
        Build()
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var ctx = new Mock<IClinicContext>();
        ctx.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var repo = new Mock<IStaffNotificationRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new MarkNotificationReadCommandHandler(repo.Object, users.Object, ctx.Object, uow.Object, NullLogger<MarkNotificationReadCommandHandler>.Instance), repo, uow, user);
    }

    private static StaffNotification NotificationInClinic(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, NotificationCategory.LowStock, "T", "M",
            DateTime.UtcNow, NotificationTargetKind.StockItem, stockItemId: Guid.NewGuid());

    [Fact]
    public async Task MarkRead_Returns_NotFound_For_Other_Clinic()
    {
        var (handler, repo, uow, _) = Build();
        var foreign = NotificationInClinic(OtherClinicId);
        repo.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await handler.Handle(new MarkNotificationReadCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        repo.Verify(r => r.AddReadMarkerAsync(It.IsAny<NotificationRead>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_Returns_NotFound_When_Missing()
    {
        var (handler, repo, uow, _) = Build();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((StaffNotification?)null);

        var result = await handler.Handle(new MarkNotificationReadCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_Adds_Marker_For_Own_Clinic_When_Not_Already_Read()
    {
        var (handler, repo, uow, user) = Build();
        var own = NotificationInClinic(ClinicId);
        repo.Setup(r => r.GetByIdAsync(own.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        repo.Setup(r => r.ReadMarkerExistsAsync(own.Id, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await handler.Handle(new MarkNotificationReadCommand { Id = own.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddReadMarkerAsync(It.Is<NotificationRead>(nr => nr.NotificationId == own.Id && nr.UserId == user.Id), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkRead_Is_Idempotent_When_Already_Read()
    {
        var (handler, repo, uow, user) = Build();
        var own = NotificationInClinic(ClinicId);
        repo.Setup(r => r.GetByIdAsync(own.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        repo.Setup(r => r.ReadMarkerExistsAsync(own.Id, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await handler.Handle(new MarkNotificationReadCommand { Id = own.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddReadMarkerAsync(It.IsAny<NotificationRead>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
