using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// The two corrections a filed camera photo needs (<c>features/camera-photo-import</c>): « Changer de patient »
/// (a photo filed to the wrong person) and the clinic PC's report that it kept the full-size original — plus the
/// default folder both of them, and the import itself, put a photo in.
///
/// <para>The load-bearing move case is the folder: a folder belongs to ONE patient, so a file that kept its
/// <c>FolderId</c> would be listed inside somebody else's tree — under the wrong name, and invisible in the right
/// one. It is asserted on the folder the file ends up in, not on the result.</para>
///
/// <para>⚠️ The load-bearing folder case is the RENAMED default: « Renommer » keeps the id, a lookup by name missed
/// it, and the folder was created a second time under the same key — which EF refuses before any SQL runs, so every
/// photo filed to that patient failed. Asserted on <c>AddAsync</c> never being called, which is what a working
/// lookup looks like from outside.</para>
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
        _patients.Setup(r => r.GetByIdAsync(Source, It.IsAny<CancellationToken>())).ReturnsAsync(APatient(Source));
        _folders.Setup(r => r.GetRootFoldersByPatientIdAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientFolder>());
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static Patient APatient(Guid id) =>
        new(id, ClinicId, "Sami", "Trabelsi", new DateTime(1985, 3, 2), "M", new Email("s@t.tn"), new PhoneNumber("+21698000000"));

    /// <summary>A default folder as the initializer made it — its predictable id — under whatever name it has now.</summary>
    private static PatientFolder ADefaultFolder(Guid patientId, string defaultName, string? renamedTo = null) =>
        new(DefaultPatientFolders.IdFor(patientId, defaultName), patientId, ClinicId, renamedTo ?? defaultName);

    private void Roots(Guid patientId, params PatientFolder[] folders) =>
        _folders.Setup(r => r.GetRootFoldersByPatientIdAsync(patientId, It.IsAny<CancellationToken>())).ReturnsAsync(folders);

    private List<PatientFolder> CaptureAdds()
    {
        var added = new List<PatientFolder>();
        _folders.Setup(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFolder, CancellationToken>((f, _) => added.Add(f));
        return added;
    }

    private static DbUpdateException UniqueViolationOnSave() =>
        new("duplicate key", new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", "23505"));

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
        Roots(Target, targetsFolder);
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie"));

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Target, file.PatientId);
        Assert.Equal(targetsFolder.Id, file.FolderId);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The target's « Photographie » was renamed « Photos cliniques » — same id. The move used to look it up by name,
    /// miss it, and create it again under the id it already holds: refused by EF, « Changer de patient » failed.
    /// </summary>
    [Fact]
    public async Task A_Photo_Moved_To_A_Patient_Whose_Photographie_Was_Renamed_Lands_In_It()
    {
        var renamed = ADefaultFolder(Target, "Photographie", "Photos cliniques");
        Roots(Target, renamed, ADefaultFolder(Target, "Radiographie"));
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie"));

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(renamed.Id, file.FolderId);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The SOURCE side by id too: a photo in a renamed « Photographie » is still a photography photo.</summary>
    [Fact]
    public async Task A_Photo_In_A_Renamed_Photographie_Still_Lands_In_The_Targets_Photographie()
    {
        var targetsFolder = new PatientFolder(Guid.NewGuid(), Target, ClinicId, "Photographie");
        Roots(Target, targetsFolder);
        var file = AFile(ADefaultFolder(Source, "Photographie", "Photos"));

        await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.Equal(targetsFolder.Id, file.FolderId);
    }

    /// <summary>
    /// A target with no folder at all gets the « Fichiers » tab's whole default set, saved on its own before the file
    /// moves: the tab seeds only a patient with no folder, so « Photographie » alone would have stayed alone.
    /// </summary>
    [Fact]
    public async Task A_Target_With_No_Folder_Gets_The_Default_Set_And_The_Photo_Lands_In_Photographie()
    {
        var added = CaptureAdds();
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "photographie"));

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DefaultPatientFolders.All.Select(d => d.Name), added.Select(f => f.Name));
        Assert.All(added, f => Assert.Equal(DefaultPatientFolders.IdFor(Target, f.Name), f.Id));
        Assert.All(added, f => Assert.Equal(Target, f.PatientId));
        Assert.Equal(DefaultPatientFolders.IdFor(Target, "Photographie"), file.FolderId);
        // The folders' save, then the move's.
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>A target that has folders gets only the one the photo needs — the others may have been deleted on purpose.</summary>
    [Fact]
    public async Task A_Target_Missing_Only_Photographie_Gets_That_Folder_Alone()
    {
        Roots(Target, ADefaultFolder(Target, "Radiographie"));
        var added = CaptureAdds();
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie"));

        await MoveHandler().Handle(Move(file), CancellationToken.None);

        var created = Assert.Single(added);
        Assert.Equal("Photographie", created.Name);
        Assert.Equal(created.Id, file.FolderId);
    }

    /// <summary>
    /// The move used to have no answer to a lost race: another request created the target's « Photographie » between
    /// the read and the save, the save failed on the deterministic id, and the user got a generic error.
    /// </summary>
    [Fact]
    public async Task A_Folder_Another_Request_Created_Meanwhile_Is_Used_And_The_Move_Goes_Through()
    {
        var winner = ADefaultFolder(Target, "Photographie");
        var radiographie = ADefaultFolder(Target, "Radiographie");
        _folders.SetupSequence(r => r.GetRootFoldersByPatientIdAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { radiographie })
            .ReturnsAsync(new[] { radiographie, winner });
        var added = CaptureAdds();
        _uow.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(UniqueViolationOnSave())
            .ReturnsAsync(1);
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie"));

        var result = await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(winner.Id, file.FolderId);
        Assert.Equal(Target, file.PatientId);
        _uow.Verify(u => u.StopTracking(Assert.Single(added)), Times.Once);
    }

    [Fact]
    public async Task A_File_In_A_Folder_The_Cabinet_Made_Lands_At_The_Targets_Root()
    {
        var file = AFile(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Avant / après"));

        await MoveHandler().Handle(Move(file), CancellationToken.None);

        Assert.Null(file.FolderId);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
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

    private static RecordFileOriginalCommand Original(PatientFile file, Guid? patientId = null) => new()
    {
        PatientId = patientId ?? Source,
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

    /// <summary>
    /// The Windows app queued the report under the patient the photo was filed to; « Changer de patient » moved it
    /// since. The original belongs to the file — refusing left the report re-sent on every page load for ever.
    /// </summary>
    [Fact]
    public async Task An_Original_Reported_Under_The_Patient_The_Photo_Left_Is_Still_Recorded()
    {
        var file = AFile();
        file.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, DateTime.UtcNow));
        file.MoveToPatient(Target, null);

        var result = await OriginalHandler().Handle(Original(file, patientId: Source), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("PC ACCUEIL", file.OriginalHeldBy);
        Assert.Equal(Target, result.Value!.PatientId);
    }

    /// <summary>A file deleted since answers by code (→ 404): the one answer the page treats as « done ».</summary>
    [Fact]
    public async Task A_Deleted_File_Answers_Not_Found_By_Code()
    {
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((PatientFile?)null);

        var result = await OriginalHandler().Handle(
            new RecordFileOriginalCommand { PatientId = Source, FileId = id, Sha256 = new string('b', 64), SizeBytes = 1, HeldBy = "PC" },
            CancellationToken.None);

        Assert.Equal(CameraImportRefusals.FileNotFoundCode, result.Code);
    }

    [Fact]
    public async Task A_File_Of_Another_Cabinet_Answers_Not_Found_And_Records_Nothing()
    {
        var foreign = new PatientFile(Guid.NewGuid(), Source, Guid.NewGuid(), "IMG_1.jpg", "clinics/x/k", "image/jpeg",
            10, FileType.Scan);
        foreign.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, DateTime.UtcNow));
        _files.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await OriginalHandler().Handle(Original(foreign), CancellationToken.None);

        Assert.Equal(CameraImportRefusals.FileNotFoundCode, result.Code);
        Assert.Null(foreign.OriginalHeldBy);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(CameraImportRefusals.FileNotFoundCode, StatusCodes.Status404NotFound)]
    [InlineData(CameraImportRefusals.OriginalInvalidCode, StatusCodes.Status400BadRequest)]
    [InlineData(CameraImportRefusals.OriginalNotCameraCode, StatusCodes.Status400BadRequest)]
    public async Task Only_A_Missing_File_Is_A_404_On_The_Wire(string code, int status)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RecordFileOriginalCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PatientFileDto>.Failure("refusé", code));
        var controller = new PatientFilesController(
            mediator.Object, new Mock<IFileResidencyPolicy>().Object, NullLogger<PatientFilesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var response = await controller.RecordOriginal(
            Source, Guid.NewGuid(), new ClinicManagement.API.Models.RecordFileOriginalRequest(), CancellationToken.None);

        var body = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(status, body.StatusCode);
        Assert.Equal(code, body.Value!.GetType().GetProperty("code")!.GetValue(body.Value));
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

    private EnsureDefaultPatientFolderCommandHandler EnsureHandler() =>
        new(_patients.Object, _folders.Object, _uow.Object, _resolver.Object);

    private static EnsureDefaultPatientFolderCommand EnsurePhotographie() =>
        new() { PatientId = Source, Key = DefaultPatientFolders.PhotographyKey };

    /// <summary>
    /// A patient who has folders gets only « Photographie »: the others may have been deleted on purpose, and the
    /// initializer — which creates every missing one — would bring them back on every import.
    /// </summary>
    [Fact]
    public async Task Ensuring_Photographie_On_A_Patient_With_Folders_Creates_That_Folder_And_No_Other()
    {
        Roots(Source, ADefaultFolder(Source, "Radiographie"));
        var added = CaptureAdds();

        var result = await EnsureHandler().Handle(EnsurePhotographie(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var created = Assert.Single(added);
        Assert.Equal("Photographie", created.Name);
        Assert.Equal(DefaultPatientFolders.IdFor(Source, "Photographie"), created.Id);
    }

    /// <summary>
    /// A patient whose first file comes from the camera has no folder at all, and the « Fichiers » tab seeds the four
    /// defaults only for such a patient — so creating « Photographie » alone left the other three missing for good.
    /// </summary>
    [Fact]
    public async Task A_Patient_With_No_Folder_Gets_The_Whole_Default_Set_From_The_Camera()
    {
        Roots(Source);
        var added = CaptureAdds();

        var result = await EnsureHandler().Handle(EnsurePhotographie(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DefaultPatientFolders.All.Select(d => d.Name), added.Select(f => f.Name));
        Assert.Equal(DefaultPatientFolders.IdFor(Source, "Photographie"), result.Value!.Id);
        Assert.Equal("Photographie", result.Value.Name);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Existing_Default_Folder_Is_Returned_As_It_Is()
    {
        var existing = new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Photographie");
        Roots(Source, existing);

        var result = await EnsureHandler().Handle(EnsurePhotographie(), CancellationToken.None);

        Assert.Equal(existing.Id, result.Value!.Id);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Renamed_Photographie_Is_Still_Where_Photos_Go_And_Nothing_Is_Created()
    {
        var renamed = ADefaultFolder(Source, "Photographie", "Photos intra-orales");
        Roots(Source, renamed, ADefaultFolder(Source, "Radiographie"));

        var result = await EnsureHandler().Handle(EnsurePhotographie(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(renamed.Id, result.Value!.Id);
        Assert.Equal("Photos intra-orales", result.Value.Name);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Two_Imports_Creating_The_Folder_At_Once_Both_Get_The_Winners()
    {
        var radiographie = ADefaultFolder(Source, "Radiographie");
        var winner = ADefaultFolder(Source, "Photographie");
        _folders.SetupSequence(r => r.GetRootFoldersByPatientIdAsync(Source, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { radiographie })
            .ReturnsAsync(new[] { radiographie, winner });
        var added = CaptureAdds();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(UniqueViolationOnSave());

        var result = await EnsureHandler().Handle(EnsurePhotographie(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(winner.Id, result.Value!.Id);
        _uow.Verify(u => u.StopTracking(Assert.Single(added)), Times.Once);
    }

    [Fact]
    public async Task An_Unknown_Folder_Key_Is_Refused_Before_Anything_Is_Read()
    {
        var result = await EnsureHandler().Handle(
            new EnsureDefaultPatientFolderCommand { PatientId = Source, Key = "vacances" }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.FolderKeyUnknownCode, result.Code);
        _patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The « Fichiers » tab's seed ─────────────────────────────────────────────────────────────────────

    private InitializeDefaultFoldersCommandHandler InitializeHandler() =>
        new(_patients.Object, _folders.Object, _uow.Object, _resolver.Object);

    [Fact]
    public async Task The_Seed_Creates_The_Four_Defaults_For_A_New_Patient()
    {
        Roots(Source);
        var added = CaptureAdds();

        var result = await InitializeHandler().Handle(
            new InitializeDefaultFoldersCommand { PatientId = Source }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DefaultPatientFolders.All.Select(d => d.Name), added.Select(f => f.Name));
    }

    /// <summary>
    /// The seed asked « missing? » by name too, so a renamed default was « missing » and re-created under the id it
    /// already holds — the same refusal as the camera's, one command over.
    /// </summary>
    [Fact]
    public async Task The_Seed_Does_Not_Recreate_A_Renamed_Default_Folder()
    {
        Roots(Source,
            ADefaultFolder(Source, "Radiographie"),
            ADefaultFolder(Source, "Photographie", "Photos"),
            ADefaultFolder(Source, "Analyses Biologiques"),
            ADefaultFolder(Source, "Lettres de Liaisons", "Courriers"));

        var result = await InitializeHandler().Handle(
            new InitializeDefaultFoldersCommand { PatientId = Source }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        _folders.Verify(r => r.AddAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_Default_Folder_Is_Recognised_By_Its_Id_Before_Its_Name()
    {
        Assert.Equal("Photographie", DefaultPatientFolders.DefaultNameOf(ADefaultFolder(Source, "Photographie", "Photos")));
        Assert.Equal("Radiographie", DefaultPatientFolders.DefaultNameOf(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "radiographie")));
        Assert.Null(DefaultPatientFolders.DefaultNameOf(new PatientFolder(Guid.NewGuid(), Source, ClinicId, "Avant / après")));
        Assert.Null(DefaultPatientFolders.DefaultNameOf(null));
    }
}
