using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
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
/// FR-C3 atomicity: a successful blob upload followed by a failed DB save must not leave an
/// orphaned blob behind. Exercises the shared handler path (applies in both storage modes).
/// </summary>
public class UploadPatientFileAtomicityTests
{
    private const string StoredKey = "stored-key-123";
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientFolderRepository> _folders = new();
    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly Mock<IFileStorage> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    /// <summary>
    /// Where this deployment keeps a file of a given format and size.
    ///
    /// <para>⚠️ Defaulted to <see cref="FileResidency.Hosted"/> because that is what this class is about — the
    /// atomicity of writing a blob and its row together, which only exists on the hosted path. A coffre file
    /// writes no original at all, so it has no atomicity to test here; its own door is
    /// <c>RegisterVaultFileCommand</c>. The enum has no <c>0</c> member, so Moq's default would be an invalid
    /// value rather than a neutral one, which is why this is stated rather than left unset.</para>
    /// </summary>
    private readonly Mock<IFileResidencyPolicy> _residencyPolicy = new();

    public UploadPatientFileAtomicityTests()
    {
        _residencyPolicy
            .Setup(p => p.Decide(It.IsAny<FileTypeEntry>(), It.IsAny<long>()))
            .Returns(FileResidency.Hosted);
    }

    /// <summary>
    /// ⚠️ The other side of the same door, and the one case that is <b>not</b> about atomicity: a study the
    /// catalogue files at the cabinet must be refused <i>here</i>, before anything is written, and pointed at the
    /// coffre. Without it the 25 Mo threshold would be advice the picker follows and nothing enforces — a
    /// third-party caller could put a four-hundred-megabyte study on the hosted disk through the ordinary door.
    /// </summary>
    [Fact]
    public async Task A_File_That_Belongs_In_The_Coffre_Is_Refused_Here_And_Nothing_Is_Written()
    {
        PatientFound();
        _residencyPolicy
            .Setup(p => p.Decide(It.IsAny<FileTypeEntry>(), It.IsAny<long>()))
            .Returns(FileResidency.Vault);

        var result = await Handler().Handle(ACommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _fileStorage.Verify(
            s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _files.Verify(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private UploadPatientFileCommandHandler Handler() =>
        new(_patients.Object, _folders.Object, _files.Object, _fileStorage.Object, _uow.Object,
            _clinicResolver.Object, _residencyPolicy.Object, UnboundedStorage(),
            NullLogger<UploadPatientFileCommandHandler>.Instance);
    /// <summary>
    /// An allowance that never refuses — these tests are about the upload, not about Part 4's quota.
    ///
    /// ⚠️ Built with an <b>unenforced</b> policy rather than a huge ceiling, so it reads no repository at all:
    /// a mocked `GetHostedBytesAsync` returning Moq's default 0 would look identical here and would quietly
    /// stop exercising the real path the day the allowance learns to read something else.
    /// </summary>
    private static ClinicStorageAllowance UnboundedStorage()
    {
        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(false);
        return new ClinicStorageAllowance(new Mock<IPatientFileRepository>().Object, policy.Object);
    }


    private static Patient APatient() => new(
        PatientId,
        ClinicId,
        "John",
        "Doe",
        DateTime.UtcNow.AddYears(-30),
        "M",
        new Email("john@doe.com"),
        new PhoneNumber("+21620000000"));

    private static UploadPatientFileCommand ACommand() => new()
    {
        PatientId = PatientId,
        FileName = "scan.pdf",
        FileSize = 8,
        // Must start with the real %PDF- signature: the format is keyed on the extension and the bytes have to
        // agree with it, because a Content-Type header is trivially spoofable (US-11 / AC-11.2, AC-2.3).
        FileStream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }),
    };

    private void PatientFound()
    {
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(APatient());
        // The handler now resolves the caller's clinic and verifies the patient belongs to it (#6); return
        // the patient's own clinic so the atomicity path under test is reached.
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    // [AC-3] DB save fails after a successful upload → failure returned AND the blob is removed.
    [Fact]
    public async Task Handle_Should_Delete_Blob_When_Save_Fails()
    {
        PatientFound();
        _fileStorage
            .Setup(f => f.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredKey);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db save failed"));

        var result = await Handler().Handle(ACommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _fileStorage.Verify(f => f.DeleteAsync(StoredKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-3] On success the record persists and no cleanup delete is issued.
    [Fact]
    public async Task Handle_Should_Not_Delete_Blob_On_Success()
    {
        PatientFound();
        _fileStorage
            .Setup(f => f.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredKey);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        PatientFile? persisted = null;
        _files.Setup(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFile, CancellationToken>((f, _) => persisted = f);

        var result = await Handler().Handle(ACommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(persisted);
        Assert.Equal(StoredKey, persisted!.StorageKey); // stored key matches the persisted record
        _fileStorage.Verify(f => f.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
