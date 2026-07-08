using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Domain.Entities;
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

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientFolderRepository> _folders = new();
    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly Mock<IFileStorage> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private UploadPatientFileCommandHandler Handler() =>
        new(_patients.Object, _folders.Object, _files.Object, _fileStorage.Object, _uow.Object,
            NullLogger<UploadPatientFileCommandHandler>.Instance);

    private static Patient APatient() => new(
        PatientId,
        Guid.NewGuid(),
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
        ContentType = "application/pdf",
        FileSize = 4,
        FileStream = new MemoryStream(new byte[] { 1, 2, 3, 4 }),
    };

    private void PatientFound() =>
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(APatient());

    // [AC-3] DB save fails after a successful upload → failure returned AND the blob is removed.
    [Fact]
    public async Task Handle_Should_Delete_Blob_When_Save_Fails()
    {
        PatientFound();
        _fileStorage
            .Setup(f => f.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(f => f.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
