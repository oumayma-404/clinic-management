using System.IO.Compression;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Application.Features.Backup.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// The cabinet's own two doors: downloading its archive and putting one back
/// (<c>clinic-data-archive-and-restore</c> AC-1, AC-6, AC-7).
///
/// <para><b>The download and the restore are run against each other</b> — the file a test restores is the one the
/// query actually built. That is what makes « a cabinet can put back what it took out » an assertion rather than
/// two independent hopes, and it is the property a backup is worthless without.</para>
///
/// <para><b>Most of the file is about refusals that must write NOTHING.</b> An archive from the wrong cabinet, from
/// a schema this build cannot read, or from a file that is not an archive at all are all caught before a row is
/// staged — so each case asserts the store was never asked and the unit of work never committed, not merely that a
/// failure came back.</para>
/// </summary>
public class ClinicArchiveEndpointTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly FakeBlobStore _blobs = new();

    private readonly FakeArchiveStore _store = new FakeArchiveStore()
        .Table("Patient", """[{"Id":"11111111-1111-1111-1111-111111111111"}]""", rows: 1,
            outcome: new ClinicArchiveTableOutcome(1, 0, 0));

    private readonly CountingUnitOfWork _unitOfWork = new();

    public ClinicArchiveEndpointTests()
    {
        _clinics.Setup(c => c.GetByIdAsync(ClinicA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicA, "Cabinet Ben Ali", city: "Tunis"));
    }

    private static User Local(string role, Guid clinicId) =>
        User.CreateLocalUser(clinicId, role, $"{role}@cabinet.tn", "HASH", $"{role} name");

    private void AsCaller(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private BuildClinicArchiveQueryHandler Download() => new(
        _users.Object, _clinics.Object, _context.Object, _store, _blobs,
        NullLogger<BuildClinicArchiveQueryHandler>.Instance);

    private readonly FakeAuditEntryRepository _auditEntries = new();

    private RestoreClinicArchiveCommandHandler Restore() => new(
        _users.Object, _context.Object, _store, _blobs, _unitOfWork, new ProcessAuditActorProvider(),
        _auditEntries, NullLogger<RestoreClinicArchiveCommandHandler>.Instance);

    private async Task<byte[]> ArchiveOfAsync(User admin)
    {
        AsCaller(admin);

        var result = await Download().Handle(new BuildClinicArchiveQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        // The handler hands back a readable stream over a self-deleting temp file rather than a `byte[]`: a
        // cabinet with twenty years of radiographs would otherwise be held twice on the large-object heap before
        // a byte reached the client.
        await using var content = result.Value!.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);

        return buffer.ToArray();
    }

    // ------------------------------------------------------------------ the download

    // [AC-1] The archive is built for the CALLER's own cabinet — the id comes from the DB user record, as every
    // other read in the product resolves it, and this is the one whose miss would put another cabinet's patients
    // in a file the practice keeps on a laptop.
    [Fact]
    public async Task An_Admin_Downloads_An_Archive_Of_Their_Own_Cabinet()
    {
        AsCaller(Local("admin", ClinicA));

        var result = await Download().Handle(new BuildClinicArchiveQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"export:{ClinicA}", _store.Calls);
        Assert.Equal(
            ClinicArchiveFormat.FileName("Cabinet Ben Ali", ClinicClock.ClinicToday()),
            result.Value!.FileName);
        Assert.Equal(ClinicA, result.Value.Manifest.ClinicId);
    }

    // An archive is every record the cabinet holds, in one unencrypted file. Defence in depth behind the
    // controller's AdminOnly policy — and the store is never even asked.
    [Theory]
    [InlineData("secretary")]
    [InlineData("doctor")]
    public async Task Only_An_Admin_Can_Download_An_Archive(string role)
    {
        AsCaller(Local(role, ClinicA));

        var result = await Download().Handle(new BuildClinicArchiveQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public async Task An_Unknown_Caller_Downloads_Nothing()
    {
        _context.Setup(c => c.GetUserId()).Returns("local|missing");
        _users.Setup(r => r.GetByAuth0SubAsync("local|missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await Download().Handle(new BuildClinicArchiveQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_store.Calls);
    }

    // ------------------------------------------------------------------ the round trip

    // [AC-2][AC-3] The property the feature exists for: what the cabinet took out is what it can put back. The
    // file under test is the one the download produced, so the writer and the reader cannot drift apart into an
    // archive this build makes and cannot open.
    [Fact]
    public async Task A_Cabinet_Can_Restore_The_Archive_It_Downloaded()
    {
        var admin = Local("admin", ClinicA);
        var archive = await ArchiveOfAsync(admin);

        var result = await Restore().Handle(
            new RestoreClinicArchiveCommand { Archive = new MemoryStream(archive) }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.TotalRestored);
        Assert.Equal(1, result.Value.Restored["Patient"]);
        Assert.Equal(ClinicA, Assert.Single(_store.RestoredIntoClinics));
    }

    // ------------------------------------------------------------------ AC-6 / AC-7, the refusals

    // [AC-6] An archive belonging to another cabinet is refused by CODE, names the cabinet it does belong to, and
    // changes nothing. It is not a theoretical mix-up: a practice with two installations, or an owner helping a
    // colleague, has two files in one Downloads folder whose names differ by a date.
    [Fact]
    public async Task An_Archive_From_Another_Cabinet_Is_Refused_And_Nothing_Is_Written()
    {
        var archive = await ArchiveOfAsync(Local("admin", ClinicA));

        // Same file, a different practice's admin.
        _store.Calls.Clear();
        AsCaller(Local("admin", ClinicB));

        var result = await Restore().Handle(
            new RestoreClinicArchiveCommand { Archive = new MemoryStream(archive) }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.ClinicMismatchCode, result.Code);
        Assert.Contains("Cabinet Ben Ali", result.Error!, StringComparison.Ordinal);
        AssertNothingWasWritten();
    }

    // [AC-7] A schema this build does not read is refused before anything is written, naming both versions.
    [Fact]
    public async Task An_Archive_From_Another_Schema_Version_Is_Refused_And_Nothing_Is_Written()
    {
        AsCaller(Local("admin", ClinicA));

        var result = await Restore().Handle(
            new RestoreClinicArchiveCommand { Archive = ArchiveWithSchemaVersion(ClinicArchiveFormat.SchemaVersion + 1) },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.SchemaUnsupportedCode, result.Code);
        AssertNothingWasWritten();
    }

    // A truncated download or the wrong file picked — named as such, because « échec de la restauration » would
    // send an owner looking for a fault in their data.
    [Fact]
    public async Task A_File_That_Is_Not_A_Zip_Is_Refused_As_An_Unreadable_Archive()
    {
        AsCaller(Local("admin", ClinicA));

        var result = await Restore().Handle(
            new RestoreClinicArchiveCommand { Archive = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }) },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, result.Code);
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task No_File_At_All_Is_Refused_With_The_Same_Code_The_Client_Branches_On()
    {
        AsCaller(Local("admin", ClinicA));

        var result = await Restore().Handle(new RestoreClinicArchiveCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, result.Code);
        AssertNothingWasWritten();
    }

    // Restoring rewrites a practice's records wholesale; the read behind it is admin-only for the same reason.
    [Theory]
    [InlineData("secretary")]
    [InlineData("doctor")]
    public async Task Only_An_Admin_Can_Restore_An_Archive(string role)
    {
        var archive = await ArchiveOfAsync(Local("admin", ClinicA));

        _store.Calls.Clear();
        AsCaller(Local(role, ClinicA));

        var result = await Restore().Handle(
            new RestoreClinicArchiveCommand { Archive = new MemoryStream(archive) }, CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasWritten();
    }

    private void AssertNothingWasWritten()
    {
        Assert.DoesNotContain(_store.Calls, c => c.StartsWith("restore:", StringComparison.Ordinal));
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_blobs.RestoredKeys);
    }

    /// <summary>An otherwise valid archive of this cabinet, written under a schema version this build cannot read.</summary>
    private static MemoryStream ArchiveWithSchemaVersion(int schemaVersion)
    {
        var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry(ClinicArchiveFormat.ManifestEntry).Open();
            using var writer = new StreamWriter(stream);
            writer.Write(System.Text.Json.JsonSerializer.Serialize(
                new ClinicArchiveManifest
                {
                    SchemaVersion = schemaVersion,
                    ClinicId = ClinicA,
                    ClinicName = "Cabinet Ben Ali",
                    CreatedAtUtc = new DateTime(2026, 7, 4, 21, 15, 0, DateTimeKind.Utc),
                },
                ClinicArchiveFormat.Json));
        }

        buffer.Position = 0;
        return buffer;
    }
}
