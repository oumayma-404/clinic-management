using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Backup;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// « Are the camera originals safe? » (<c>features/camera-photo-import</c>, phase 2) — a clinic PC keeps the full-size
/// photos, copies them to the archive disk and says so; the server checks the claim against what that PC reported
/// keeping and nags after <see cref="PhotoOriginalsCopy.StaleAfterDays"/> days.
///
/// <para>The load-bearing cases are the two the vault report already taught: a copy that fell short must NOT advance
/// the stamp (or the alert clears over a copy of three photos out of four hundred), and the check is per PC — one PC's
/// copy must not silence the alert about another's photos.</para>
/// </summary>
public class PhotoOriginalsCopyTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly InMemoryCopies _copies = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<INotificationGenerator> _notifications = new();

    public PhotoOriginalsCopyTests()
    {
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Holders(("PC ACCUEIL", 40, 320_000_000L));
    }

    private void Holders(params (string HeldBy, int Files, long Bytes)[] holders) =>
        _files.Setup(f => f.GetCameraOriginalTotalsAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(holders.Select(h => new CameraOriginalTotals(h.HeldBy, h.Files, h.Bytes)).ToList());

    private void Uncovered(string heldBy, int files, DateTime? oldest) =>
        _files.Setup(f => f.GetUncoveredOriginalsAsync(ClinicId, heldBy, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UncoveredOriginals(files, oldest));

    private ReportPhotoOriginalsCopyCommandHandler Handler() => new(
        _copies, _files.Object, _uow.Object, _resolver.Object, _notifications.Object,
        NullLogger<ReportPhotoOriginalsCopyCommandHandler>.Instance);

    // ── The report ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Copy_Covering_Everything_On_Record_Advances_The_Stamp()
    {
        Uncovered("PC ACCUEIL", 0, null);

        var result = await Handler().Handle(
            new ReportPhotoOriginalsCopyCommand { HeldBy = "PC ACCUEIL", FileCount = 41, TotalBytes = 330_000_000 },
            CancellationToken.None);

        Assert.True(result.Value);
        Assert.NotNull(_copies.Rows.Single().LastCompleteCopyAtUtc);
        // Nothing left uncovered on any PC: the alert, if there was one, goes.
        _notifications.Verify(n => n.ClearPhotoOriginalsCopyStaleAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Copy_That_Fell_Short_Is_Remembered_And_Does_Not_Advance_The_Stamp()
    {
        Uncovered("PC ACCUEIL", 40, Now.AddDays(-9));

        var result = await Handler().Handle(
            new ReportPhotoOriginalsCopyCommand { HeldBy = "PC ACCUEIL", FileCount = 3, TotalBytes = 24_000_000 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        var row = _copies.Rows.Single();
        Assert.Null(row.LastCompleteCopyAtUtc);
        Assert.Equal(3, row.LastReportedFiles);
        // The alert stands — and names the PC.
        _notifications.Verify(n => n.EnsurePhotoOriginalsCopyStaleAsync(
            ClinicId,
            It.Is<IReadOnlyList<StalePhotoOriginals>>(list => list.Single().HeldBy == "PC ACCUEIL" && list.Single().Files == 40),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Blank_Pc_Name_Is_Refused()
    {
        var result = await Handler().Handle(
            new ReportPhotoOriginalsCopyCommand { HeldBy = "  ", FileCount = 1, TotalBytes = 1 }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_copies.Rows);
    }

    // ── The 7-day check ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Originals_Never_Copied_For_A_Week_Are_Stale()
    {
        Uncovered("PC ACCUEIL", 40, Now.AddDays(-8));

        var stale = await PhotoOriginalsCopyCheck.FindStaleAsync(_files.Object, _copies, ClinicId, Now);

        Assert.Equal("PC ACCUEIL", Assert.Single(stale).HeldBy);
    }

    [Fact]
    public async Task Originals_Kept_A_Few_Days_Ago_Are_Not_Yet_Stale()
    {
        Uncovered("PC ACCUEIL", 40, Now.AddDays(-6));

        Assert.Empty(await PhotoOriginalsCopyCheck.FindStaleAsync(_files.Object, _copies, ClinicId, Now));
    }

    [Fact]
    public async Task The_Check_Measures_From_The_Last_Complete_Copy_Of_That_Pc()
    {
        var copy = PhotoOriginalsCopy.Start(Guid.NewGuid(), ClinicId, "PC ACCUEIL");
        copy.RecordReport(40, 320_000_000, complete: true, Now.AddDays(-20));
        _copies.Rows.Add(copy);
        DateTime? askedAfter = null;
        _files.Setup(f => f.GetUncoveredOriginalsAsync(ClinicId, "PC ACCUEIL", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTime?, CancellationToken>((_, _, after, _) => askedAfter = after)
            .ReturnsAsync(new UncoveredOriginals(0, null));

        var stale = await PhotoOriginalsCopyCheck.FindStaleAsync(_files.Object, _copies, ClinicId, Now);

        Assert.Empty(stale);
        Assert.Equal(copy.LastCompleteCopyAtUtc, askedAfter);
    }

    [Fact]
    public async Task One_Pcs_Copy_Does_Not_Silence_Another_Pcs_Photos()
    {
        Holders(("PC ACCUEIL", 40, 320_000_000L), ("PC FAUTEUIL 2", 12, 96_000_000L));
        var accueil = PhotoOriginalsCopy.Start(Guid.NewGuid(), ClinicId, "PC ACCUEIL");
        accueil.RecordReport(40, 320_000_000, complete: true, Now.AddHours(-1));
        _copies.Rows.Add(accueil);
        Uncovered("PC ACCUEIL", 0, null);
        Uncovered("PC FAUTEUIL 2", 12, Now.AddDays(-10));

        var stale = await PhotoOriginalsCopyCheck.FindStaleAsync(_files.Object, _copies, ClinicId, Now);

        Assert.Equal("PC FAUTEUIL 2", Assert.Single(stale).HeldBy);
    }

    [Fact]
    public async Task A_Cabinet_With_No_Originals_Anywhere_Is_Never_Nagged()
    {
        Holders();

        Assert.Empty(await PhotoOriginalsCopyCheck.FindStaleAsync(_files.Object, _copies, ClinicId, Now));
        _files.Verify(f => f.GetUncoveredOriginalsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The bell row ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Alert_Names_Each_Pc_And_Is_Not_Restated_While_Nothing_Changes()
    {
        var feed = new Mock<IStaffNotificationRepository>();
        StaffNotification? row = null;
        feed.Setup(r => r.GetPhotoOriginalsCopyStaleAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => row);
        feed.Setup(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
            .Callback<StaffNotification, CancellationToken>((n, _) => row = n);
        var generator = new NotificationGenerator(
            feed.Object, new Mock<IDoctorRepository>().Object, _uow.Object, new Mock<IRealtimeNotifier>().Object,
            NullLogger<NotificationGenerator>.Instance);
        var stale = new List<StalePhotoOriginals> { new("PC ACCUEIL", 37, new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc)) };

        await generator.EnsurePhotoOriginalsCopyStaleAsync(ClinicId, stale);
        await generator.EnsurePhotoOriginalsCopyStaleAsync(ClinicId, stale);

        Assert.NotNull(row);
        Assert.Equal(NotificationCategory.PhotoOriginalsCopyStale, row!.Category);
        Assert.Contains("37 photos originales gardées sur PC ACCUEIL", row.Message);
        Assert.Contains("03/11/2026", row.Message);
        feed.Verify(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void A_Complete_Report_Never_Moves_The_Stamp_Back()
    {
        var copy = PhotoOriginalsCopy.Start(Guid.NewGuid(), ClinicId, "PC ACCUEIL");
        copy.RecordReport(10, 100, complete: true, Now);
        copy.RecordReport(10, 100, complete: true, Now.AddDays(-1));
        copy.RecordReport(2, 10, complete: false, Now.AddDays(1));

        Assert.Equal(Now, copy.LastCompleteCopyAtUtc);
        Assert.Equal(2, copy.LastReportedFiles);
    }

    private sealed class InMemoryCopies : IPhotoOriginalsCopyRepository
    {
        public List<PhotoOriginalsCopy> Rows { get; } = new();

        public Task<PhotoOriginalsCopy?> GetAsync(Guid clinicId, string heldBy, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.ClinicId == clinicId && r.HeldBy == heldBy));

        public Task<IReadOnlyList<PhotoOriginalsCopy>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhotoOriginalsCopy>>(Rows.Where(r => r.ClinicId == clinicId).ToList());

        public Task AddAsync(PhotoOriginalsCopy copy, CancellationToken cancellationToken = default)
        {
            Rows.Add(copy);
            return Task.CompletedTask;
        }
    }
}
