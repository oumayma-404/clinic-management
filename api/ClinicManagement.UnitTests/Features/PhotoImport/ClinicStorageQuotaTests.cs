using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// Storage you can see (<c>features/camera-photo-import</c>, phase 3): a cabinet's own ceiling set from the console,
/// the ceiling every upload is measured against, and the 80 / 95 % bell.
///
/// <para>The load-bearing cases are the ones a reader would get wrong: the 95 % warning must arrive as a NEW row
/// (restating the 80 % one keeps whoever already dismissed it), while going back DOWN must never announce anything —
/// deleting files to 85 % used to post a fresh unread « 80 % » row, i.e. the bell rewarded the remedy with an alarm.
/// The bell runs over a fake that behaves like the table: a duplicate primary key fails the save, as PostgreSQL's
/// does, which is what lets the race between two uploads be asserted at all.</para>
/// </summary>
public class ClinicStorageQuotaTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const long Gigabyte = 1024L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024;

    // ── The levels ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(70, -1)]   // under the re-arm level: the alert goes
    [InlineData(77, 0)]    // inside the band: nothing is announced
    [InlineData(80, 80)]
    [InlineData(94, 80)]
    [InlineData(95, 95)]
    [InlineData(120, 95)]  // past the ceiling after a lowering: still the urgent one
    public void The_Level_Follows_The_Usage(int percent, int expected) =>
        Assert.Equal(expected, ClinicStorageAlerts.LevelFor(percent * Gigabyte / 10, 10 * Gigabyte));

    [Fact]
    public void Where_Nothing_Is_Enforced_There_Is_No_Alert() =>
        Assert.Equal(-1, ClinicStorageAlerts.LevelFor(5 * Gigabyte, 0));

    [Fact]
    public void An_Alert_Id_Names_Its_Level_And_Its_Cabinet_And_Nothing_Else()
    {
        var other = Guid.NewGuid();

        Assert.Equal(ClinicStorageAlerts.AlertIdFor(ClinicId, 80), ClinicStorageAlerts.AlertIdFor(ClinicId, 80));
        Assert.NotEqual(ClinicStorageAlerts.AlertIdFor(ClinicId, 80), ClinicStorageAlerts.AlertIdFor(ClinicId, 95));
        Assert.NotEqual(ClinicStorageAlerts.AlertIdFor(ClinicId, 80), ClinicStorageAlerts.AlertIdFor(other, 80));
        Assert.Equal(95, ClinicStorageAlerts.AnnouncedLevelOf(ClinicId, ClinicStorageAlerts.AlertIdFor(ClinicId, 95)));
        Assert.Equal(0, ClinicStorageAlerts.AnnouncedLevelOf(ClinicId, ClinicStorageAlerts.AlertIdFor(other, 95)));
        Assert.Equal(0, ClinicStorageAlerts.AnnouncedLevelOf(ClinicId, Guid.NewGuid()));
    }

    // ── The allowance ───────────────────────────────────────────────────────────────────────────────────

    private static ClinicStorageAllowance Allowance(
        long usedBytes,
        long? overrideBytes,
        Mock<INotificationGenerator>? notifications = null,
        ILogger<ClinicStorageAllowance>? logger = null)
    {
        var files = new Mock<IPatientFileRepository>();
        files.Setup(f => f.GetHostedBytesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(usedBytes);
        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(true);
        policy.SetupGet(p => p.QuotaBytes).Returns(10 * Gigabyte);
        var quotas = new Mock<IClinicStorageQuotaReader>();
        quotas.Setup(q => q.GetOverrideAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(overrideBytes);
        return new ClinicStorageAllowance(files.Object, policy.Object, quotas.Object, notifications?.Object, logger);
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

    /// <summary>
    /// Under 75 % the alert goes; inside the band it is only ever LOWERED — never raised, never created. A deletion can
    /// take a cabinet from 96 % to 77 % in one go, and the bell must not keep saying 95 % until something crosses a line.
    /// </summary>
    [Fact]
    public async Task Usage_Back_Under_75_Percent_Withdraws_It_And_The_Band_Only_Ever_Lowers_It()
    {
        var notifications = new Mock<INotificationGenerator>();

        await Allowance(7 * Gigabyte, null, notifications).AfterChangeAsync(ClinicId);
        await Allowance(77 * Gigabyte / 10, null, notifications).AfterChangeAsync(ClinicId);

        notifications.Verify(n => n.ClearStorageNearlyFullAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.LowerStorageNearlyFullAsync(
            ClinicId, ClinicStorageAlerts.WarnPercent, 77 * Gigabyte / 10, 10 * Gigabyte, It.IsAny<CancellationToken>()),
            Times.Once);
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

    /// <summary>
    /// A broken read used to be swallowed by a bare <c>catch { }</c>: the 80 / 95 % bell stopped for every cabinet and
    /// nothing anywhere said so. It still never reaches the caller — the change is committed — but it is logged.
    /// </summary>
    [Fact]
    public async Task A_Broken_Read_Is_Logged_And_Never_Thrown()
    {
        var files = new Mock<IPatientFileRepository>();
        files.Setup(f => f.GetHostedBytesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database gone"));
        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(true);
        policy.SetupGet(p => p.QuotaBytes).Returns(10 * Gigabyte);
        var log = new CapturingLogger<ClinicStorageAllowance>();
        var allowance = new ClinicStorageAllowance(
            files.Object, policy.Object, notifications: new Mock<INotificationGenerator>().Object, logger: log);

        await allowance.AfterChangeAsync(ClinicId);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<TimeoutException>(entry.Exception);
    }

    // ── Deleting gives the space back ───────────────────────────────────────────────────────────────────

    private static Patient APatient() =>
        new(Guid.NewGuid(), ClinicId, "Sami", "Trabelsi", new DateTime(1985, 3, 2), "M", new Email("s@t.tn"),
            new PhoneNumber("+21698000000"));

    private static Mock<ICurrentClinicResolver> Resolver()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        return resolver;
    }

    /// <summary>
    /// Deleting large files is one of the two ways out the bell names, and only uploads used to re-evaluate it — so a
    /// cabinet that freed 3 Go kept reading « rempli à 95 % » until its next upload.
    /// </summary>
    [Fact]
    public async Task Deleting_A_File_Re_Evaluates_The_Bell_After_The_Commit()
    {
        var patient = APatient();
        var file = new PatientFile(Guid.NewGuid(), patient.Id, ClinicId, "cbct.zip", "clinics/d/k", "application/zip",
            3 * Gigabyte, FileType.Scan);
        var files = new Mock<IPatientFileRepository>();
        files.Setup(f => f.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(p => p.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var bell = new Mock<INotificationGenerator>();
        var handler = new DeletePatientFileCommandHandler(
            files.Object, patients.Object, new Mock<IFileStorage>().Object, Resolver().Object, new Mock<IUnitOfWork>().Object,
            Allowance(7 * Gigabyte, null, bell), NullLogger<DeletePatientFileCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeletePatientFileCommand { PatientId = patient.Id, FileId = file.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        bell.Verify(n => n.ClearStorageNearlyFullAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_A_Folder_Re_Evaluates_The_Bell_After_The_Commit()
    {
        var patient = APatient();
        var folder = new PatientFolder(Guid.NewGuid(), patient.Id, ClinicId, "Anciens scanners");
        var folders = new Mock<IPatientFolderRepository>();
        folders.Setup(f => f.GetByIdAsync(folder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(folder);
        var files = new Mock<IPatientFileRepository>();
        files.Setup(f => f.GetByFolderIdAsync(folder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PatientFile>());
        var patients = new Mock<IPatientRepository>();
        patients.Setup(p => p.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var bell = new Mock<INotificationGenerator>();
        var handler = new DeletePatientFolderCommandHandler(
            folders.Object, files.Object, patients.Object, new Mock<IFileStorage>().Object, Resolver().Object,
            new Mock<IUnitOfWork>().Object, Allowance(85 * Gigabyte / 10, null, bell),
            NullLogger<DeletePatientFolderCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeletePatientFolderCommand { PatientId = patient.Id, FolderId = folder.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        bell.Verify(n => n.EnsureStorageNearlyFullAsync(
            ClinicId, 80, 85 * Gigabyte / 10, 10 * Gigabyte, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The bell row ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The feed as the table behaves: rows are written at the save, a duplicate primary key fails the whole save with
    /// 23505, and a « blind » read answers what a concurrent request saw before the other one saved.
    /// </summary>
    private sealed class Bell
    {
        private readonly List<StaffNotification> _adding = new();
        private readonly List<StaffNotification> _removing = new();

        public List<StaffNotification> Rows { get; } = new();
        public int BlindReads { get; set; }
        public int Adds { get; private set; }
        public Mock<IStaffNotificationRepository> Feed { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public CapturingLogger<NotificationGenerator> Log { get; } = new();
        public NotificationGenerator Generator { get; }

        public Bell()
        {
            Feed.Setup(r => r.GetStorageNearlyFullAlertsAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (BlindReads > 0)
                    {
                        BlindReads--;
                        return Array.Empty<StaffNotification>();
                    }

                    return Rows.ToList();
                });
            Feed.Setup(r => r.AddAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
                .Callback<StaffNotification, CancellationToken>((n, _) =>
                {
                    Adds++;
                    _adding.Add(n);
                });
            Feed.Setup(r => r.RemoveAsync(It.IsAny<StaffNotification>(), It.IsAny<CancellationToken>()))
                .Callback<StaffNotification, CancellationToken>((n, _) => _removing.Add(n));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                var next = Rows.Except(_removing).Concat(_adding).ToList();
                var count = _adding.Count + _removing.Count;
                _adding.Clear();
                _removing.Clear();
                if (next.GroupBy(row => row.Id).Any(g => g.Count() > 1))
                {
                    throw new DbUpdateException(
                        "duplicate key", new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", "23505"));
                }

                Rows.Clear();
                Rows.AddRange(next);
                return Task.FromResult(count);
            });

            Generator = new NotificationGenerator(
                Feed.Object, new Mock<IDoctorRepository>().Object, UnitOfWork.Object, new Mock<IRealtimeNotifier>().Object,
                Log);
        }
    }

    [Fact]
    public async Task The_95_Percent_Warning_Is_A_New_Row_So_It_Badges_Again()
    {
        var bell = new Bell();

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 8 * Gigabyte, 10 * Gigabyte);
        var first = Assert.Single(bell.Rows).Id;
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 85 * Gigabyte / 10, 10 * Gigabyte);
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 96 * Gigabyte / 10, 10 * Gigabyte);

        var row = Assert.Single(bell.Rows);
        Assert.NotEqual(first, row.Id);
        Assert.Equal(NotificationCategory.StorageNearlyFull, row.Category);
        Assert.Equal("Stockage du cabinet rempli à 95 %", row.Title);
        Assert.Contains("9,6 Go sur 10,0 Go", row.Message);
        Assert.Equal(2, bell.Adds);
    }

    /// <summary>
    /// Files deleted, 95 → 85 %: the row says 80 % from now on, but it is the SAME row — read by whoever read it — and
    /// no new unread one appears. Before, every level change was a new row, so freeing space badged the bell.
    /// </summary>
    [Fact]
    public async Task Deleting_Files_Lowers_The_Row_Silently_And_Back_Up_To_95_Is_Not_Announced_Twice()
    {
        var bell = new Bell();
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 8 * Gigabyte, 10 * Gigabyte);
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 96 * Gigabyte / 10, 10 * Gigabyte);
        var urgent = Assert.Single(bell.Rows);

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 85 * Gigabyte / 10, 10 * Gigabyte);

        var lowered = Assert.Single(bell.Rows);
        Assert.Same(urgent, lowered);
        Assert.Equal("Stockage du cabinet rempli à 80 %", lowered.Title);
        Assert.Contains("8,5 Go sur 10,0 Go", lowered.Message);

        // Back over 95 % in the same episode (never under 75 %): already said once, so restated, not re-announced.
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 97 * Gigabyte / 10, 10 * Gigabyte);

        Assert.Same(urgent, Assert.Single(bell.Rows));
        Assert.Equal("Stockage du cabinet rempli à 95 %", urgent.Title);
        Assert.Equal(2, bell.Adds);
    }

    [Fact]
    public async Task Under_75_Percent_It_Goes_And_Both_Levels_Are_Armed_Again()
    {
        var bell = new Bell();
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 96 * Gigabyte / 10, 10 * Gigabyte);

        await bell.Generator.ClearStorageNearlyFullAsync(ClinicId);
        Assert.Empty(bell.Rows);

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 8 * Gigabyte, 10 * Gigabyte);
        Assert.Equal(ClinicStorageAlerts.AlertIdFor(ClinicId, 80), Assert.Single(bell.Rows).Id);
    }

    [Fact]
    public async Task Inside_The_Band_A_95_Row_Is_Lowered_And_Nothing_Is_Ever_Created()
    {
        var bell = new Bell();

        await bell.Generator.LowerStorageNearlyFullAsync(ClinicId, 80, 77 * Gigabyte / 10, 10 * Gigabyte);
        Assert.Empty(bell.Rows);

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 96 * Gigabyte / 10, 10 * Gigabyte);
        await bell.Generator.LowerStorageNearlyFullAsync(ClinicId, 80, 77 * Gigabyte / 10, 10 * Gigabyte);

        var row = Assert.Single(bell.Rows);
        Assert.Equal("Stockage du cabinet rempli à 80 %", row.Title);
        Assert.Contains("7,7 Go sur 10,0 Go", row.Message);
        Assert.Equal(1, bell.Adds);
    }

    /// <summary>
    /// Two uploads crossing 80 % at once both read « no alert yet » and both insert. The row's id is deterministic, so
    /// the database refuses the second insert, and the loser treats that as the outcome it wanted — not as an error.
    /// </summary>
    [Fact]
    public async Task Two_Uploads_Crossing_80_Percent_At_Once_Leave_One_Row()
    {
        var bell = new Bell { BlindReads = 2 };

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 81 * Gigabyte / 10, 10 * Gigabyte);
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 82 * Gigabyte / 10, 10 * Gigabyte);

        var row = Assert.Single(bell.Rows);
        Assert.Equal(ClinicStorageAlerts.AlertIdFor(ClinicId, 80), row.Id);
        Assert.Equal(2, bell.Adds);
        bell.UnitOfWork.Verify(u => u.StopTracking(It.Is<StaffNotification>(n => !ReferenceEquals(n, row))), Times.Once);
        Assert.DoesNotContain(bell.Log.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Two_Rows_Left_By_An_Earlier_Race_Are_Both_Withdrawn()
    {
        var bell = new Bell();
        var a = StaffNotification.ForStorageNearlyFull(Guid.NewGuid(), ClinicId, "Stockage du cabinet rempli à 80 %", "…", DateTime.UtcNow);
        var b = StaffNotification.ForStorageNearlyFull(Guid.NewGuid(), ClinicId, "Stockage du cabinet rempli à 80 %", "…", DateTime.UtcNow);
        bell.Rows.AddRange(new[] { a, b });

        await bell.Generator.ClearStorageNearlyFullAsync(ClinicId);

        Assert.Empty(bell.Rows);
    }

    [Fact]
    public async Task A_Duplicate_Beside_The_Kept_Row_Goes_Without_A_New_Announcement()
    {
        var bell = new Bell();
        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 8 * Gigabyte, 10 * Gigabyte);
        var kept = Assert.Single(bell.Rows);
        bell.Rows.Add(StaffNotification.ForStorageNearlyFull(
            Guid.NewGuid(), ClinicId, kept.Title, kept.Message, DateTime.UtcNow));

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 85 * Gigabyte / 10, 10 * Gigabyte);

        Assert.Same(kept, Assert.Single(bell.Rows));
        Assert.Equal(1, bell.Adds);
    }

    /// <summary>
    /// The bell's figures printed « Go » unconditionally — « 0,0 Go sur 0,1 Go » on a small ceiling, the defect the
    /// refusal's own sentence had already fixed — and carried its own copy of the remedy. Both are the refusal's now.
    /// </summary>
    [Fact]
    public async Task The_Bell_Speaks_The_Refusals_Sizes_And_Remedy()
    {
        var bell = new Bell();

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 80, 46 * Megabyte, 60 * Megabyte);

        var message = Assert.Single(bell.Rows).Message;
        Assert.Contains("46,0 Mo sur 60,0 Mo", message);
        Assert.DoesNotContain("Go", message);
        Assert.EndsWith(ClinicStorageRefusals.Remedy, message);
        Assert.EndsWith(ClinicStorageRefusals.Remedy, ClinicStorageRefusals.Full(46 * Megabyte, 60 * Megabyte));
    }

    /// <summary>Both ways out are an admin's — a secretary was shown a warning she could act on in neither way.</summary>
    [Fact]
    public async Task Only_The_Cabinets_Admins_Are_Told()
    {
        var bell = new Bell();

        await bell.Generator.EnsureStorageNearlyFullAsync(ClinicId, 95, 96 * Gigabyte / 10, 10 * Gigabyte);

        var row = Assert.Single(bell.Rows);
        Assert.Equal(User.RoleAdmin, row.TargetRole);
        Assert.Null(row.TargetUserId);
        Assert.Null(row.ActorUserId);
    }

    // ── The console ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class ConsoleHarness
    {
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<IPatientFileRepository> Files { get; } = new();
        public Mock<IClinicStoragePolicy> Policy { get; } = new();
        public Mock<IClinicStorageQuotaReader> Quotas { get; } = new();
        public Mock<INotificationGenerator> Notifications { get; } = new();
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
            // What the cabinet's row says after the save — the bell is re-read after the commit.
            Quotas.Setup(q => q.GetOverrideAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Clinic.StorageQuotaBytes);
            Session.Setup(s => s.GetAccountId()).Returns(Guid.NewGuid());
            Session.Setup(s => s.GetEmail()).Returns("vendeur@apexa.tn");
            Scope.SetupGet(s => s.Kind).Returns(TenantScopeKind.SystemWide);
        }

        public SetClinicStorageQuotaFromConsoleCommandHandler Handler() => new(
            Clinics.Object, Files.Object, Policy.Object,
            new ClinicStorageAllowance(Files.Object, Policy.Object, Quotas.Object, Notifications.Object),
            Access.Object, Session.Object, UnitOfWork.Object, Scope.Object,
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

    /// <summary>Raising the ceiling is the remedy the bell names: 9 Go of 25 is 36 %, so the 90 % warning must go.</summary>
    [Fact]
    public async Task Raising_The_Ceiling_Withdraws_The_Bell_After_The_Commit()
    {
        var harness = new ConsoleHarness();

        await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25 * Gigabyte, Reason = "Demande du cabinet",
        }, CancellationToken.None);

        harness.Notifications.Verify(n => n.ClearStorageNearlyFullAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lowering_The_Ceiling_Under_The_Usage_Rings_The_Bell()
    {
        var harness = new ConsoleHarness();

        await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 9 * Gigabyte, Reason = "Retour au forfait de base",
        }, CancellationToken.None);

        harness.Notifications.Verify(n => n.EnsureStorageNearlyFullAsync(
            ClinicId, 95, 9 * Gigabyte, 9 * Gigabyte, It.IsAny<CancellationToken>()), Times.Once);
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

    /// <summary>
    /// A motif longer than the journal's column used to throw inside the ledger and come back as « Erreur lors de la
    /// modification… » — the vendor could not tell what to shorten. Refused first, by code, with nothing read.
    /// </summary>
    [Fact]
    public async Task A_Motif_Longer_Than_The_Journal_Holds_Is_Refused_By_Code_Before_Anything_Is_Read()
    {
        var harness = new ConsoleHarness();

        var result = await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25 * Gigabyte, Reason = new string('x', PlatformAccessEntry.MaxReasonLength + 1),
        }, CancellationToken.None);

        Assert.Equal(SetClinicStorageQuotaFromConsoleCommandHandler.ReasonTooLongCode, result.Code);
        Assert.Contains(PlatformAccessEntry.MaxReasonLength.ToString(), result.Error);
        Assert.Null(harness.Clinic.StorageQuotaBytes);
        harness.Clinics.Verify(c => c.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Access.Verify(a => a.AddAsync(It.IsAny<PlatformAccessEntry>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task A_Typo_Of_Three_Zeros_Is_Refused_By_Code_With_The_Domains_Own_Sentence()
    {
        var harness = new ConsoleHarness();

        var result = await harness.Handler().Handle(new SetClinicStorageQuotaFromConsoleCommand
        {
            ClinicId = ClinicId, QuotaBytes = 25_000L * 1024 * Gigabyte, Reason = "Demande du cabinet",
        }, CancellationToken.None);

        Assert.Equal(SetClinicStorageQuotaFromConsoleCommandHandler.QuotaOutOfRangeCode, result.Code);
        Assert.Equal(Clinic.StorageQuotaRangeError, result.Error);
        Assert.Contains("1 Go et 2 To", result.Error);
        Assert.Null(harness.Clinic.StorageQuotaBytes);
        harness.Clinics.Verify(c => c.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => harness.Clinic.SetStorageQuota(512 * 1024 * 1024));
        Assert.StartsWith(Clinic.StorageQuotaRangeError, thrown.Message);
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

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
