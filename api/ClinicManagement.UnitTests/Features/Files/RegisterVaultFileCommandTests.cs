using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Files;

/// <summary>
/// The coffre's registration door — <b>the one upload path where the server never sees the bytes</b>, and which
/// shipped with no tests at all (`features/clinic-file-vault/progress.md` deferred them; the pass never ran).
///
/// <para>⚠️ <b>Why that absence mattered more here than elsewhere.</b> Every other door can check what it was
/// given: it has the file. This one is handed a name, a length and a hash for bytes that stayed on the practice's
/// disk, so the only things standing between the record and a row describing a file nobody can produce are the
/// checks in this handler. A wrong verdict is not a failed upload — it is a patient file that exists in the
/// database and nowhere else, discovered years later.</para>
/// </summary>
public class RegisterVaultFileCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid FileId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>Comfortably past the 25 Mo point where the catalogue sends a study to the cabinet.</summary>
    private const long AStudy = 340L * 1024 * 1024;

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientFolderRepository> _folders = new();
    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<IFileResidencyPolicy> _residency = new();

    public RegisterVaultFileCommandTests()
    {
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        _residency.SetupGet(p => p.VaultAvailable).Returns(true);
        _residency
            .Setup(p => p.Decide(It.IsAny<FileTypeEntry>(), It.IsAny<long>()))
            .Returns((FileTypeEntry entry, long size) => entry.Residency.Decide(size));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        PatientBelongsToTheCaller();
    }

    private void PatientBelongsToTheCaller(Guid? clinicId = null) =>
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(
                PatientId, clinicId ?? ClinicId, "Amine", "Trabelsi", new DateTime(1990, 1, 1), "M",
                new Email("a@t.tn"), new PhoneNumber("+21620000000")));

    private RegisterVaultFileCommandHandler Handler() => new(
        _patients.Object, _folders.Object, _files.Object, _storage.Object, _uow.Object,
        _resolver.Object, _residency.Object, NullLogger<RegisterVaultFileCommandHandler>.Instance);

    private static RegisterVaultFileCommand ACommand(
        string fileName = "etude.dcm", long size = AStudy, string hash = ValidHash) => new()
    {
        PatientId = PatientId,
        FileId = FileId,
        FileName = fileName,
        FileSize = size,
        ContentHash = hash,
    };

    private Task<Result<PatientFileDto>> Register(RegisterVaultFileCommand? command = null) =>
        Handler().Handle(command ?? ACommand(), CancellationToken.None);

    private void AssertNothingWasRecorded()
    {
        _files.Verify(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The happy path, and what it must NOT do ───────────────────────────────────────────────────────────

    /// <summary>
    /// AC-4: <b>zero original bytes reach the server.</b> That is the whole feature, and it is asserted on the
    /// storage mock rather than inferred — a handler that stored something would still return a perfectly valid
    /// DTO.
    /// </summary>
    [Fact]
    public async Task A_Study_Is_Recorded_And_Not_One_Original_Byte_Is_Written()
    {
        PatientFile? recorded = null;
        _files.Setup(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFile, CancellationToken>((file, _) => recorded = file);

        var result = await Register();

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(recorded);
        Assert.Equal(FileResidency.Vault, recorded!.Residency);
        Assert.Null(recorded.StorageKey);
        Assert.Equal(ValidHash, recorded.ContentHash);
        Assert.Equal(AStudy, recorded.FileSize);

        _storage.Verify(
            s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>The DTO must never carry the key — the list is served to every device, and a coffre row has none.</summary>
    [Fact]
    public async Task The_Response_Reports_The_Residency_And_Carries_No_Storage_Key()
    {
        var result = await Register();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(nameof(FileResidency.Vault), result.Value!.Residency);
        Assert.False(result.Value.HasPreview);
    }

    // ── The refusals, each in its own words ───────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-7: on a deployment where the clinic already holds its own blobs there is no coffre, so this door is not
    /// published at all — and the handler refuses behind it rather than trusting the controller's routing.
    /// </summary>
    [Fact]
    public async Task There_Is_No_Coffre_Door_Where_The_Deployment_Has_No_Coffre()
    {
        _residency.SetupGet(p => p.VaultAvailable).Returns(false);

        var result = await Register();

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    /// <summary>
    /// A file the server would gladly hold is sent back to the door that holds it — otherwise the coffre would
    /// slowly acquire every small scan, and those would stop opening from a phone.
    /// </summary>
    [Fact]
    public async Task A_File_Small_Enough_To_Host_Is_Sent_Back_To_The_Ordinary_Door()
    {
        var result = await Register(ACommand(size: 10L * 1024 * 1024));

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    [Fact]
    public async Task A_File_Past_Even_The_Coffres_Ceiling_Is_Refused_With_Its_Own_Code()
    {
        var result = await Register(ACommand(size: FileTypeCatalog.VaultBytes + 1));

        Assert.True(result.IsFailure);
        Assert.Equal(FileResidencyRefusals.TooLargeCode, result.Code);
        AssertNothingWasRecorded();
    }

    /// <summary>
    /// A format that never goes to the coffre cannot be registered through it — a PDF is hosted at any size, so
    /// admitting one here would put a file in a folder the app would then never look in.
    /// </summary>
    [Fact]
    public async Task An_Always_Hosted_Format_Cannot_Be_Filed_At_The_Cabinet()
    {
        var result = await Register(ACommand(fileName: "note.pdf"));

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    [Fact]
    public async Task A_Deny_Listed_Extension_Is_Refused_Here_Too()
    {
        var result = await Register(ACommand(fileName: "outil.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.DeniedMessage, result.Error);
        AssertNothingWasRecorded();
    }

    /// <summary>
    /// ⚠️ The hash is the <b>only</b> integrity evidence a coffre file will ever have — the deployment never
    /// received the bytes and can never recompute it — so a malformed one is refused rather than stored as junk.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("0123456789abcdef")]                                                    // too short
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]  // right length, not hex
    public async Task A_Malformed_Empreinte_Is_Refused(string hash)
    {
        var result = await Register(ACommand(hash: hash));

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    [Fact]
    public async Task An_Empty_File_Is_Refused()
    {
        var result = await Register(ACommand(size: 0));

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    /// <summary>
    /// A repeated id would point a second row at one file on the disk, and deleting either would strand the other
    /// — the id is minted by the browser, so this is the server's only defence against it.
    /// </summary>
    [Fact]
    public async Task An_Id_Already_On_Record_Is_Refused()
    {
        _files.Setup(r => r.GetByIdAsync(FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientFile(
                FileId, PatientId, ClinicId, "deja.dcm", "clinics/x/y", "application/dicom", 1, FileType.Scan));

        var result = await Register();

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    [Fact]
    public async Task An_Empty_Id_Is_Refused_Before_Anything_Else()
    {
        var command = ACommand();
        command.FileId = Guid.Empty;

        var result = await Register(command);

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    // ── Tenancy ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_Clinics_Patient_Cannot_Have_A_File_Filed_Against_Them()
    {
        PatientBelongsToTheCaller(OtherClinicId);

        var result = await Register();

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    [Fact]
    public async Task A_Folder_Belonging_To_Another_Patient_Is_Refused()
    {
        var folderId = Guid.NewGuid();
        _folders.Setup(r => r.GetByIdAsync(folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientFolder(folderId, Guid.NewGuid(), ClinicId, "radios"));

        var command = ACommand();
        command.FolderId = folderId;

        var result = await Register(command);

        Assert.True(result.IsFailure);
        AssertNothingWasRecorded();
    }

    // ── The preview, which never fails a registration ─────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The spec's edge case, and it was a real defect: the door carried
    /// <c>[RequestSizeLimit(PreviewBytes + 64 KB)]</c>, and Kestrel enforces a body limit <b>before</b> model
    /// binding — so an oversized preview 413'd the whole request and lost the row, the exact opposite of « dropped,
    /// and the row is still registered ». The handler is what decides, and this is what says so.
    /// </summary>
    [Fact]
    public async Task An_Oversized_Preview_Is_Dropped_And_The_Row_Is_Still_Recorded()
    {
        PatientFile? recorded = null;
        _files.Setup(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFile, CancellationToken>((file, _) => recorded = file);

        var command = ACommand();
        command.PreviewStream = new MemoryStream(new byte[16]);
        command.PreviewFileName = "apercu.jpg";
        command.PreviewSize = FileTypeCatalog.PreviewBytes + 1;

        var result = await Register(command);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(recorded!.PreviewStorageKey);
        _storage.Verify(
            s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>An unreadable stand-in is dropped for the same reason: the row is the record, the picture a courtesy.</summary>
    [Fact]
    public async Task A_Preview_That_Is_Not_What_It_Claims_Is_Dropped_Rather_Than_Refusing_The_File()
    {
        var command = ACommand();
        command.PreviewStream = new MemoryStream(Encoding.ASCII.GetBytes("ceci n'est pas une image"));
        command.PreviewFileName = "apercu.jpg";
        command.PreviewSize = 24;

        var result = await Register(command);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// A usable stand-in is stored under the file's own id, clinic-prefixed by the storage — the one hosted blob a
    /// coffre file owns, and the one `PatientFileBlobs` has to remember to delete with it.
    /// </summary>
    [Fact]
    public async Task A_Usable_Preview_Is_Stored_Against_The_Files_Own_Id()
    {
        string? relativePath = null;
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, string, CancellationToken>((_, _, _, path, _) => relativePath = path)
            .ReturnsAsync("clinics/aaa/previews/ddd.jpg");

        PatientFile? recorded = null;
        _files.Setup(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFile, CancellationToken>((file, _) => recorded = file);

        var jpeg = new byte[64];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(jpeg, 0);

        var command = ACommand();
        command.PreviewStream = new MemoryStream(jpeg);
        command.PreviewFileName = "apercu.jpg";
        command.PreviewSize = jpeg.Length;

        var result = await Register(command);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal($"previews/{FileId:D}.jpg", relativePath);
        Assert.Equal("clinics/aaa/previews/ddd.jpg", recorded!.PreviewStorageKey);
        Assert.True(result.Value!.HasPreview);
    }

    /// <summary>
    /// The preview is written before the row, so a failed save must take it back with it — otherwise the object
    /// store keeps an image nothing points at, invisibly, for the life of the deployment.
    /// </summary>
    [Fact]
    public async Task A_Failed_Save_Takes_The_Stored_Preview_Back_With_It()
    {
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("clinics/aaa/previews/ddd.jpg");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("save failed"));

        var jpeg = new byte[64];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(jpeg, 0);

        var command = ACommand();
        command.PreviewStream = new MemoryStream(jpeg);
        command.PreviewFileName = "apercu.jpg";
        command.PreviewSize = jpeg.Length;

        var result = await Register(command);

        Assert.True(result.IsFailure);
        _storage.Verify(s => s.DeleteAsync("clinics/aaa/previews/ddd.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }
}
