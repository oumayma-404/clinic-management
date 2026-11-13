using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// Storage you can see (<c>features/camera-photo-import</c>, phase 3): a cabinet's own ceiling set from the console,
/// the ceiling every upload is measured against, and the 80 / 95 % bell.
///
/// <para>The load-bearing case is the one a reader would get wrong: the 95 % warning must arrive as a NEW row, because
/// restating the 80 % one keeps whoever already dismissed it — the escalation would land on a cleared bell.</para>
/// </summary>
public class ClinicStorageQuotaTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const long Gigabyte = 1024L * 1024 * 1024;

    // ── The levels ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(70, -1)]   // under the re-arm level: the alert goes
    [InlineData(77, 0)]    // inside the band: leave whatever is up
    [InlineData(80, 80)]
    [InlineData(94, 80)]
    [InlineData(95, 95)]
    [InlineData(120, 95)]  // past the ceiling after a lowering: still the urgent one
    public void The_Level_Follows_The_Usage(int percent, int expected) =>
        Assert.Equal(expected, ClinicStorageAlerts.LevelFor(percent * Gigabyte / 10, 10 * Gigabyte));

    [Fact]
    public void Where_Nothing_Is_Enforced_There_Is_No_Alert() =>
        Assert.Equal(-1, ClinicStorageAlerts.LevelFor(5 * Gigabyte, 0));

    // ── The allowance ───────────────────────────────────────────────────────────────────────────────────

    private static ClinicStorageAllowance Allowance(
        long usedBytes, long? overrideBytes, Mock<INotificationGenerator>? notifications = null)
    {
        var files = new Mock<IPatientFileRepository>();
        files.Setup(f => f.GetHostedBytesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(usedBytes);
        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(true);
        policy.SetupGet(p => p.QuotaBytes).Returns(10 * Gigabyte);
        var quotas = new Mock<IClinicStorageQuotaReader>();
        quotas.Setup(q => q.GetOverrideAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(overrideBytes);
        return new ClinicStorageAllowance(files.Object, policy.Object, quotas.Object, notifications?.Object);
    }

    [Fact]
    public async Task A_Cabinets_Own_Ceiling_Replaces_The_Default()
    {
        var usage = await Allowance(12 * Gigabyte, 25 * Gigabyte).ReadAsync(ClinicId);

        Assert.Equal(25 * Gigabyte, usage.QuotaBytes);
        Assert.True((await Allowance(12 * Gigabyte, 25 * Gigabyte).EnsureRoomForAsync(ClinicId, Gigabyte)).IsSuccess);
        Assert.Equal(
            ClinicStorageRefusals.FullCode,
            (await Allowance(12 * Gigabyte, null).EnsureRoomForAsync(ClinicId, Gigabyte)).Code);
    }

    [Fact]
    public async Task An_Upload_Crossing_80_Percent_Raises_The_Alert()
    {
        var notifications = new Mock<INotificationGenerator>();

        await Allowance(8 * Gigabyte + 1, null, notifications).AfterChangeAsync(ClinicId);

        notifications.Verify(n => n.EnsureStorageNearlyFullAsync(
            ClinicId, 80, 8 * Gigabyte + 1, 10 * Gigabyte, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Usage_Back_Under_75_Percent_Withdraws_It_And_The_Band_Leaves_It_Alone()
    {
        var notifications = new Mock<INotificationGenerator>();

        await Allowance(7 * Gigabyte, null, notifications).AfterChangeAsync(ClinicId);
        await Allowance(77 * Gigabyte / 10, null, notifications).AfterChangeAsync(ClinicId);

        notifications.Verify(n => n.ClearStorageNearlyFullAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.EnsureStorageNearlyFullAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Failing_Bell_Never_Reaches_The_Uploader()
    {
        var notifications = new Mock<INotificationGenerator>();
        notifications.Setup(n => n.EnsureStorageNearlyFullAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("feed down"));

        await Allowance(9 * Gigabyte, null, notifications).AfterChangeAsync(ClinicId);
    }

    // ── The bell row ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_95_Percent_Warning_Is_A_New_Row_So_It_Badges_Again()
    {
        var feed = new Mock<IStaffNotificationRepository>();
        StaffNotification? row = null;
        feed.Setup(r => r.GetStorageNearlyFullAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => row);
        feed.Setup(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
            .Callback<StaffNotification, CancellationToken>((n, _) => row = n);
        feed.Setup(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
            .Callback<StaffNotification, CancellationToken>((_, _) => row = null);
        var uow = new Mock<IUnitOfWork>();
        var generator = new NotificationGenerator(
            feed.Object, new Mock<IDoctorRepository>().Object, uow.Object, new Mock<IRealtimeNotifier>().Object,
            NullLogger<NotificationGenerator>.Instance);

        await generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 8 * Gigabyte, 10 * Gigabyte);
        var first = row!.Id;
        await generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 85 * Gigabyte / 10, 10 * Gigabyte);
        await generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 96 * Gigabyte / 10, 10 * Gigabyte);

        Assert.NotEqual(first, row!.Id);
        Assert.Equal(NotificationCategory.StorageNearlyFull, row.Category);
        Assert.Equal("Stockage du cabinet rempli à 95 %", row.Title);
        Assert.Contains("9,6 Go sur 10,0 Go", row.Message);
        feed.Verify(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        feed.Verify(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The console ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class ConsoleHarness
    {
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<IPatientFileRepository> Files { get; } = new();
        public Mock<IClinicStoragePolicy> Policy { get; } = new();
        public Mock<IPlatformAccessEntryRepository> Access { get; } = new();
        public Mock<IPlatformSessionContext> Session { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ITenantScope> Scope { get; } = new();
        public Clinic Clinic { get; } = new(ClinicId, "Cabinet Amira", "CODE01");

        public ConsoleHarness()
        {
            Policy.SetupGet(p => p.Enforced).Returns(true);
            Policy.SetupGet(p => p.QuotaBytes).Returns(10 * Gigabyte);
            Clinics.Setup(c => c.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Clinic);
            Files.Setup(f => f.GetHostedBytesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(9 * Gigabyte);
            Session.Setup(s => s.GetAccountId()).Returns(Guid.NewGuid());
            Session.Setup(s => s.GetEmail()).Returns("vendeur@apexa.tn");
            Scope.SetupGet(s => s.Kind).Returns(TenantScopeKind.SystemWide);
        }

        public SetClinicStorageQuotaFromConsoleCommandHandler Handler() => new(
            Clinics.Object, Files.Object, Policy.Object, Access.Object, Session.Object, UnitOfWork.Object, Scope.Object,
            NullLogger<SetClinicStorageQuotaFromConsoleCommandHandler>.Instance);
    }

    [Fact]
    public async Task The_Vendor_Raises_A_Ceiling_With_A_Motif_And_The_Journal_Says_So()
    {
        var harness = new ConsoleHarness();

        var result = await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25 * Gigabyte, Reason = "Photos de chaque patient — demande du Dr Amira",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(25 * Gigabyte, harness.Clinic.StorageQuotaBytes);
        Assert.True(result.Value!.StorageQuotaIsCustom);
        Assert.Equal(9 * Gigabyte, result.Value.StorageUsedBytes);
        harness.Access.Verify(a => a.AddAsync(
            It.Is<PlatformAccessEntry>(e => e.Action == PlatformAccessAction.StorageQuotaChanged && e.Reason!.Contains("Dr Amira")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_Motif_No_Change()
    {
        var harness = new ConsoleHarness();

        var result = await harness.Handler().Handle(
            new SetClinicStorageQuotaFromConsoleCommand { ClinicId = ClinicId, QuotaBytes = 25 * Gigabyte, Reason = " " },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(harness.Clinic.StorageQuotaBytes);
    }

    [Fact]
    public async Task The_Same_Figure_Twice_Writes_One_Journal_Row()
    {
        var harness = new ConsoleHarness();
        var command = new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25 * Gigabyte, Reason = "Demande du cabinet",
        };

        await harness.Handler().Handle(command, CancellationToken.None);
        await harness.Handler().Handle(command, CancellationToken.None);

        harness.Access.Verify(a => a.AddAsync(It.IsAny<PlatformAccessEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Typo_Of_Three_Zeros_Is_Refused()
    {
        var harness = new ConsoleHarness();

        var result = await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25_000L * 1024 * Gigabyte, Reason = "Demande du cabinet",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("1 Go et 2 To", result.Error);
        Assert.Null(harness.Clinic.StorageQuotaBytes);
    }

    [Fact]
    public async Task Where_The_Cabinet_Owns_Its_Disk_There_Is_No_Ceiling_To_Set()
    {
        var harness = new ConsoleHarness();
        harness.Policy.SetupGet(p => p.Enforced).Returns(false);

        var result = await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25 * Gigabyte, Reason = "Demande du cabinet",
        }, CancellationToken.None);

        Assert.Equal(SetClinicStorageQuotaFromConsoleCommandHandler.NotEnforcedCode, result.Code);
    }

    [Fact]
    public void The_Portfolio_Row_States_The_Ceiling_In_Force()
    {
        var custom = PlatformClinicRowMapper.ToDto(
            new PlatformClinicRow(ClinicId, "A", null, DateTime.UtcNow, false, null, null, false, null, 1, 1, 1, 1, 1, 1,
                null, null, null, HostedBytes: 3 * Gigabyte, StorageQuotaBytes: 25 * Gigabyte),
            DateTime.UtcNow.Date, null, null, DateTime.UtcNow, defaultQuotaBytes: 10 * Gigabyte);
        var standard = PlatformClinicRowMapper.ToDto(
            new PlatformClinicRow(ClinicId, "B", null, DateTime.UtcNow, false, null, null, false, null, 1, 1, 1, 1, 1, 1,
                null, null, null, HostedBytes: 3 * Gigabyte),
            DateTime.UtcNow.Date, null, null, DateTime.UtcNow, defaultQuotaBytes: 10 * Gigabyte);

        Assert.Equal((25 * Gigabyte, true), (custom.StorageQuotaBytes, custom.StorageQuotaIsCustom));
        Assert.Equal((10 * Gigabyte, false), (standard.StorageQuotaBytes, standard.StorageQuotaIsCustom));
        Assert.Equal(3 * Gigabyte, standard.StorageUsedBytes);
    }
}
