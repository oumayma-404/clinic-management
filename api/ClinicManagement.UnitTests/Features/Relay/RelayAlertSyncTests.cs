using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The bell is made to carry exactly the rows <see cref="RelayAlertRules"/> asks for (<c>clinic-pc-copy</c> AC-2.2,
/// AC-2.3), over an in-memory feed so the assertions are about the rows themselves.
/// </summary>
public class RelayAlertSyncTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly List<StaffNotification> _feed = new();
    private int _saves;
    private readonly NotificationGenerator _generator;

    public RelayAlertSyncTests()
    {
        var repo = new Mock<IStaffNotificationRepository>();
        repo.Setup(r => r.GetRelayAlertsAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _feed.Where(n => n.Category == NotificationCategory.RelayAttention).ToList());
        repo.Setup(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
            .Callback<StaffNotification, CancellationToken>((n, _) => _feed.Add(n))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
            .Callback<StaffNotification, CancellationToken>((n, _) => _feed.Remove(n))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _saves++)
            .ReturnsAsync(1);

        _generator = new NotificationGenerator(
            repo.Object, Mock.Of<IDoctorRepository>(), unitOfWork.Object, Mock.Of<IRealtimeNotifier>(),
            NullLogger<NotificationGenerator>.Instance);
    }

    private static RelayAlertRow Off(string since = "09:00") =>
        new(RelayAlert.Off, "PC de secours éteint", $"PC-ACCUEIL est éteint depuis {since}.");

    private Task Sync(params RelayAlertRow[] wanted) => _generator.SyncRelayAlertsAsync(ClinicId, wanted);

    // AC-2.3: the row is for admins only, and it opens « Paramètres → PC de secours ».
    [Fact]
    public async Task A_New_Problem_Is_One_Admin_Only_Row()
    {
        await Sync(Off());

        var row = Assert.Single(_feed);
        Assert.Equal(NotificationCategory.RelayAttention, row.Category);
        Assert.Equal(User.RoleAdmin, row.TargetRole);
        Assert.Equal(RelayAlert.Off, row.RelayAlert);
        Assert.Equal(NotificationTargetKind.RelaySettings, row.TargetKind);
        Assert.Null(row.ActorUserId);
        Assert.Null(row.TargetUserId);
    }

    // A minutely pass must not write — or make every open browser refetch — when nothing changed.
    [Fact]
    public async Task An_Unchanged_Problem_Writes_Nothing()
    {
        await Sync(Off());
        var first = _feed.Single().Id;
        _saves = 0;

        await Sync(Off());

        Assert.Equal(first, _feed.Single().Id);
        Assert.Equal(0, _saves);
    }

    [Fact]
    public async Task A_Reworded_Problem_Is_Restated_In_Place()
    {
        await Sync(Off("09:00"));
        var first = _feed.Single().Id;

        await Sync(Off("09:30"));

        var row = Assert.Single(_feed);
        Assert.Equal(first, row.Id);
        Assert.Contains("09:30", row.Message);
    }

    // A different problem is a NEW row, so it badges the bell again for an admin who had read the first one.
    [Fact]
    public async Task A_Problem_That_Changes_Kind_Replaces_The_Row()
    {
        await Sync(Off());
        var first = _feed.Single().Id;

        await Sync(new RelayAlertRow(RelayAlert.DiskNearlyFull, "Disque", "Il reste 2 Go sur PC-ACCUEIL."));

        var row = Assert.Single(_feed);
        Assert.NotEqual(first, row.Id);
        Assert.Equal(RelayAlert.DiskNearlyFull, row.RelayAlert);
    }

    [Fact]
    public async Task A_Problem_That_Ended_Leaves_The_Bell()
    {
        await Sync(Off(), new RelayAlertRow(RelayAlert.ClockWrong, "Horloge", "L'horloge de PC-ACCUEIL est fausse."));
        Assert.Equal(2, _feed.Count);

        await Sync();

        Assert.Empty(_feed);
    }
}
