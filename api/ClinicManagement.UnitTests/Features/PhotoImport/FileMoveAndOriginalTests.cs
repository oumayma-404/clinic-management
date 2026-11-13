using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// The two corrections a filed camera photo needs (<c>features/camera-photo-import</c>): « Changer de patient »
/// (a photo filed to the wrong person) and the clinic PC's report that it kept the full-size original.
///
/// <para>The load-bearing move case is the folder: a folder belongs to ONE patient, so a file that kept its
/// <c>FolderId</c> would be listed inside somebody else's tree — under the wrong name, and invisible in the right
/// one. It is asserted on the folder the file ends up in, not on the result.</para>
/// </summary>
public class FileMoveAndOriginalTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Source = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Target = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly string Key = new('a', 64);

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly Mock<IPatientFolderRepository> _folders = new();
    private readonly Mock<IMedicalDocumentRepository> _documents = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();

    public FileMoveAndOriginalTests()
    {
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(APatient(Target));
        _folders.Setup(r => r.GetRootFoldersByPatientIdAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientFolder>());
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static Patient APatient(Guid id) =>
        new(id, ClinicId, "Sami", "Trabelsi", new DateTime(1985, 3, 2), "M", new Email("s@t.tn"), new PhoneNumber("+21698000000"));

    private PatientFile AFile(PatientFolder? folder = null)
    {
        var file = new PatientFile(Guid.NewGuid(), Source, ClinicId, "IMG_4521.jpg", "clinics/d/k", "image/jpeg",
            1_400_000, FileType.Scan, folder?.Id);
        if (folder is not null)
        {
            // EF fills the navigation on read; the handler reads the folder's NAME through it.
            typeof(PatientFile).GetProperty(nameof(PatientFile.Folder))!.SetValue(file, folder);
        }

        _files.Setup(r => r.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        return file;
    }

    private MovePatientFileToPatientCommandHandler MoveHandler() =>
        new(_patients.Object, _files.Object, _folders.Object, _documents.Object, _uow.Object, _resolver.Object);

    private static MovePatientFileToPatientCommand Move(PatientFile file, Guid? target = null, uint version = 7) => new()
    {
        PatientId = Source,
        FileId = file.Id,
        TargetPatientId = target ?? Target,
        Version = version,
    };

    [Fact]
    public async Task A_Photo_In_Photographie_Lands_In_The_Targets_Photographie()
    {
        var targetsFolder = new PatientFolder(Guid.NewGuid(), Target, ClinicId, "Photographie");
        _folders.Setup(r => r.GetRootFoldersByPatientIdAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { targetsFolder });
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie"));

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Target, file.PatientId);
        Assert.Equal(targetsFolder.Id, file.FolderId);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Missing_Default_Folder_Is_Created_For_The_Target_In_The_Same_Save()
    {
        PatientFolder? created = null;
        _folders.Setup(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFolder, CancellationToken>((f, _) => created = f);
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "photographie"));

        await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal("Photographie", created!.Name);
        Assert.Equal(Target, created.PatientId);
        Assert.Equal(DefaultPatientFolders.IdFor(Target, "Photographie"), created.Id);
        Assert.Equal(created.Id, file.FolderId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_File_In_A_Folder_The_Cabinet_Made_Lands_At_The_Targets_Root()
    {
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Avant / après"));

        await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.Null(file.FolderId);
    }

    [Fact]
    public async Task The_Version_The_Form_Read_Is_Checked()
    {
        var file = AFile();

        await MoveHandler().Handle(Move(file, version: 42), CancellationToken.None);

        _uow.Verify(u => u.SetExpectedVersion(file, 42u), Times.Once);
    }

    [Fact]
    public async Task Moving_To_The_Same_Patient_Is_Refused_By_Code()
    {
        var file = AFile();

        var result = await MoveHandler().Handle(Move(file, target: Source), CancellationToken.None);

        Assert.Equal(FileMoveRefusals.SamePatientCode, result.Code);
    }

    [Fact]
    public async Task An_Archived_Target_Is_Refused()
    {
        var archived = APatient(Target);
        archived.Archive();
        _patients.Setup(r => r.GetByIdAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(archived);

        var result = await MoveHandler().Handle(Move(AFile()), CancellationToken.None);

        Assert.Equal(FileMoveRefusals.TargetArchivedCode, result.Code);
    }

    [Fact]
    public async Task A_Patient_Of_Another_Cabinet_Reads_As_Not_Found()
    {
        var foreign = new Patient(Target, Guid.NewGuid(), "X", "Y", new DateTime(1990, 1, 1), "F",
            new Email("x@y.tn"), new PhoneNumber("+21620000001"));
        _patients.Setup(r => r.GetByIdAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var file = AFile();

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Source, file.PatientId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_File_A_Document_Relies_On_Stays_Where_It_Is()
    {
        var file = AFile();
        _documents.Setup(d => d.AnyForFileAsync(ClinicId, file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.Equal(FileMoveRefusals.LinkedDocumentCode, result.Code);
        Assert.Equal(Source, file.PatientId);
    }

    [Fact]
    public async Task A_Coffre_File_Cannot_Change_Patient()
    {
        var file = PatientFile.RegisterInVault(Guid.NewGuid(), Source, ClinicId, "scan.dcm", "application/dicom",
            10, FileType.Scan, Key);
        _files.Setup(r => r.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.Equal(FileMoveRefusals.VaultCode, result.Code);
    }

    // ── The original kept at the cabinet ────────────────────────────────────────────────────────────────

    private RecordFileOriginalCommandHandler OriginalHandler() => new(_files.Object, _uow.Object, _resolver.Object);

    private static RecordFileOriginalCommand Original(PatientFile file) => new()
    {
        PatientId = Source,
        FileId = file.Id,
        Sha256 = new string('b', 64),
        SizeBytes = 8_400_000,
        HeldBy = "PC ACCUEIL",
    };

    [Fact]
    public async Task A_Camera_Photos_Original_Is_Recorded_Once()
    {
        var file = AFile();
        file.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, DateTime.UtcNow));

        var first = await OriginalHandler().Handle(Original(file), CancellationToken.None);
        var again = await OriginalHandler().Handle(Original(file), CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal("PC ACCUEIL", again.Value!.OriginalHeldBy);
        Assert.Equal(8_400_000, again.Value.OriginalSizeBytes);
        // The Windows app re-reports after every restart: only the first report writes.
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Ordinary_Upload_Has_No_Original_At_The_Cabinet()
    {
        var result = await OriginalHandler().Handle(Original(AFile()), CancellationToken.None);

        Assert.Equal(CameraImportRefusals.OriginalNotCameraCode, result.Code);
    }

    [Fact]
    public async Task A_Report_Without_A_Holder_Is_Refused_By_Code()
    {
        var file = AFile();
        file.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, DateTime.UtcNow));
        var request = Original(file);
        request.HeldBy = "  ";

        var result = await OriginalHandler().Handle(request, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.OriginalInvalidCode, result.Code);
    }

    // ── The default folder a photo lands in ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ensuring_Photographie_Creates_That_Folder_And_No_Other()
    {
        _patients.Setup(r => r.GetByIdAsync(Source, It.IsAny<CancellationToken>())).ReturnsAsync(APatient(Source));
        _folders.Setup(r => r.GetRootFoldersByPatientIdAsync(Source, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientFolder>());
        var added = new List<PatientFolder>();
        _folders.Setup(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFolder, CancellationToken>((f, _) => added.Add(f));

        var handler = new EnsureDefaultPatientFolderCommandHandler(
            _patients.Object, _folders.Object, _uow.Object, _resolver.Object);
        var result = await handler.Handle(
            new EnsureDefaultPatientFolderCommand { PatientId = Source, Key = "photographie" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Photographie", Assert.Single(added).Name);
    }

    [Fact]
    public async Task An_Existing_Default_Folder_Is_Returned_As_It_Is()
    {
        var existing = new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie");
        _patients.Setup(r => r.GetByIdAsync(Source, It.IsAny<CancellationToken>())).ReturnsAsync(APatient(Source));
        _folders.Setup(r => r.GetRootFoldersByPatientIdAsync(Source, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existing });

        var handler = new EnsureDefaultPatientFolderCommandHandler(
            _patients.Object, _folders.Object, _uow.Object, _resolver.Object);
        var result = await handler.Handle(
            new EnsureDefaultPatientFolderCommand { PatientId = Source, Key = "photographie" }, CancellationToken.None);

        Assert.Equal(existing.Id, result.Value!.Id);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_Folder_Key_Is_Refused_Before_Anything_Is_Read()
    {
        var handler = new EnsureDefaultPatientFolderCommandHandler(
            _patients.Object, _folders.Object, _uow.Object, _resolver.Object);
        var result = await handler.Handle(
            new EnsureDefaultPatientFolderCommand { PatientId = Source, Key = "vacances" }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.FolderKeyUnknownCode, result.Code);
        _patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
