using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// A camera photo goes through the ordinary upload door (<c>features/camera-photo-import</c>) and carries its
/// capture facts onto the record. The load-bearing case is the duplicate: re-inserting a card must refuse the same
/// shot <b>before a byte is stored</b> — asserted on the storage mock, since a handler that stored the photo and
/// then refused would still return the refusal.
/// </summary>
public class CameraUploadTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly string Key = new('a', 64);

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientFolderRepository> _folders = new();
    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<IFileResidencyPolicy> _residency = new();

    public CameraUploadTests()
    {
        _residency.Setup(p => p.Decide(It.IsAny<FileTypeEntry>(), It.IsAny<long>())).Returns(FileResidency.Hosted);
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Amira", "Ben Salah", new DateTime(1990, 1, 1), "F",
                new Email("a@b.tn"), new PhoneNumber("+21620000000")));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("clinics/d/k");
    }

    private UploadPatientFileCommandHandler Handler()
    {
        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(false);
        return new UploadPatientFileCommandHandler(
            _patients.Object, _folders.Object, _files.Object, _storage.Object, _uow.Object,
            _resolver.Object, _residency.Object,
            new ClinicStorageAllowance(new Mock<IPatientFileRepository>().Object, policy.Object),
            NullLogger<UploadPatientFileCommandHandler>.Instance);
    }

    private static UploadPatientFileCommand APhoto(CameraCaptureInput capture)
    {
        var jpeg = new byte[64];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }.CopyTo(jpeg, 0);
        return new UploadPatientFileCommand
        {
            PatientId = PatientId,
            FileName = "IMG_4521.JPG",
            FileSize = jpeg.Length,
            FileStream = new MemoryStream(jpeg),
            Capture = capture,
        };
    }

    [Fact]
    public async Task A_Camera_Photo_Is_Recorded_With_Its_Capture_Facts()
    {
        PatientFile? recorded = null;
        _files.Setup(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFile, CancellationToken>((f, _) => recorded = f);

        var result = await Handler().Handle(
            APhoto(new CameraCaptureInput(Key, new DateTime(2026, 11, 12, 9, 14, 3), null, "Canon EOS 250D")),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(recorded);
        Assert.Equal(FileOrigin.Camera, recorded!.Origin);
        Assert.Equal(Key, recorded.CaptureKey);
        Assert.Equal("Canon EOS 250D", recorded.CameraModel);
        // 09:14:03 in Tunis (UTC+1) is 08:14:03 UTC.
        Assert.Equal(new DateTime(2026, 11, 12, 8, 14, 3, DateTimeKind.Utc), recorded.CapturedAt);
        Assert.Equal("Camera", result.Value!.Origin);
    }

    [Fact]
    public async Task The_Same_Shot_Is_Refused_Before_Anything_Is_Stored()
    {
        _files.Setup(r => r.CaptureKeyExistsAsync(ClinicId, Key, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(
            APhoto(new CameraCaptureInput(Key, null, null, null)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CameraImportRefusals.AlreadyImportedCode, result.Code);
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _files.Verify(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Ordinary_Upload_Asks_Nothing_About_Shots()
    {
        var result = await Handler().Handle(APhoto(CameraCaptureInput.None), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Upload", result.Value!.Origin);
        _files.Verify(r => r.CaptureKeyExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_Malformed_Key_Is_Refused_With_Its_Code()
    {
        var result = await Handler().Handle(
            APhoto(new CameraCaptureInput("not-a-key", null, null, null)), CancellationToken.None);

        Assert.Equal(CameraImportRefusals.InvalidKeyCode, result.Code);
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
