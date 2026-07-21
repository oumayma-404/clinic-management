using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Files.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Files;

/// <summary>
/// fix-patient-file-tenant-isolation (#1, #18). Patient files/folders carry no ClinicId and are excluded
/// from the global query filter, so each read/delete handler must resolve the caller's clinic and verify the
/// owning patient (and scoped folder) belongs to it — otherwise a known cross-clinic GUID leaks or deletes
/// PHI (AC-1/AC-2). Also covers folder-delete integrity: DB rows committed before blobs, blob errors logged
/// not swallowed, never an orphan (AC-3).
/// </summary>
public class FilesTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Patient Patient(Guid clinicId) => new(
        Guid.NewGuid(), clinicId, "Jean", "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    private static Mock<ICurrentClinicResolver> Resolver(Guid clinicId)
    {
        var r = new Mock<ICurrentClinicResolver>();
        r.Setup(x => x.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(clinicId));
        return r;
    }

    private static PatientFile File(Guid patientId, Guid? folderId = null) =>
        new(Guid.NewGuid(), patientId, "scan.pdf", "files/scan.pdf", "application/pdf", 1024,
            FileType.MedicalRecord, folderId);

    private static PatientFolder Folder(Guid patientId) =>
        new(Guid.NewGuid(), patientId, "documents");

    // ---- GetPatientFilesQuery (AC-1/AC-2) -----------------------------------

    [Fact]
    public async Task GetFiles_Should_Fail_For_Other_Clinic_Patient()
    {
        var foreign = Patient(OtherClinicId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var files = new Mock<IPatientFileRepository>();
        var folders = new Mock<IPatientFolderRepository>();

        var handler = new GetPatientFilesQueryHandler(files.Object, folders.Object, patients.Object, Resolver(ClinicId).Object);
        var result = await handler.Handle(new GetPatientFilesQuery { PatientId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        files.Verify(r => r.GetRootFilesByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        files.Verify(r => r.GetByFolderIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetFiles_Should_Fail_When_Scoped_Folder_Belongs_To_Another_Patient()
    {
        var patient = Patient(ClinicId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var folders = new Mock<IPatientFolderRepository>();
        var foreignFolder = Folder(Guid.NewGuid()); // belongs to a different patient
        folders.Setup(r => r.GetByIdAsync(foreignFolder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignFolder);
        var files = new Mock<IPatientFileRepository>();

        var handler = new GetPatientFilesQueryHandler(files.Object, folders.Object, patients.Object, Resolver(ClinicId).Object);
        var result = await handler.Handle(
            new GetPatientFilesQuery { PatientId = patient.Id, FolderId = foreignFolder.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        files.Verify(r => r.GetByFolderIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetFiles_Should_Succeed_For_Own_Clinic_Patient()
    {
        var patient = Patient(ClinicId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var files = new Mock<IPatientFileRepository>();
        files.Setup(r => r.GetRootFilesByPatientIdAsync(patient.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { File(patient.Id) });
        var folders = new Mock<IPatientFolderRepository>();

        var handler = new GetPatientFilesQueryHandler(files.Object, folders.Object, patients.Object, Resolver(ClinicId).Object);
        var result = await handler.Handle(new GetPatientFilesQuery { PatientId = patient.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    // ---- GetPatientFoldersQuery (AC-1/AC-2) ---------------------------------

    [Fact]
    public async Task GetFolders_Should_Fail_For_Other_Clinic_Patient()
    {
        var foreign = Patient(OtherClinicId);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var folders = new Mock<IPatientFolderRepository>();

        var handler = new GetPatientFoldersQueryHandler(folders.Object, patients.Object, Resolver(ClinicId).Object);
        var result = await handler.Handle(new GetPatientFoldersQuery { PatientId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        folders.Verify(r => r.GetRootFoldersByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- DownloadPatientFileQuery (AC-1) ------------------------------------

    [Fact]
    public async Task Download_Should_Fail_For_Other_Clinic_And_Not_Stream_Bytes()
    {
        var foreign = Patient(OtherClinicId);
        var file = File(foreign.Id);
        var files = new Mock<IPatientFileRepository>();
        files.Setup(r => r.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var storage = new Mock<IFileStorage>();

        var handler = new DownloadPatientFileQueryHandler(files.Object, patients.Object, storage.Object, Resolver(ClinicId).Object);
        var result = await handler.Handle(
            new DownloadPatientFileQuery { PatientId = foreign.Id, FileId = file.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- DeletePatientFileCommand (AC-1) ------------------------------------

    [Fact]
    public async Task DeleteFile_Should_Fail_For_Other_Clinic_And_Not_Delete()
    {
        var foreign = Patient(OtherClinicId);
        var file = File(foreign.Id);
        var files = new Mock<IPatientFileRepository>();
        files.Setup(r => r.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var storage = new Mock<IFileStorage>();
        var uow = new Mock<IUnitOfWork>();

        var handler = new DeletePatientFileCommandHandler(files.Object, patients.Object, storage.Object, Resolver(ClinicId).Object, uow.Object,
            NullLogger<DeletePatientFileCommandHandler>.Instance);
        var result = await handler.Handle(
            new DeletePatientFileCommand { PatientId = foreign.Id, FileId = file.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        files.Verify(r => r.DeleteAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- DeletePatientFolderCommand (AC-1) ----------------------------------

    [Fact]
    public async Task DeleteFolder_Should_Fail_For_Other_Clinic_And_Not_Delete()
    {
        var foreign = Patient(OtherClinicId);
        var folder = Folder(foreign.Id);
        var folders = new Mock<IPatientFolderRepository>();
        folders.Setup(r => r.GetByIdAsync(folder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(folder);
        var files = new Mock<IPatientFileRepository>();
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var storage = new Mock<IFileStorage>();
        var uow = new Mock<IUnitOfWork>();

        var handler = new DeletePatientFolderCommandHandler(
            folders.Object, files.Object, patients.Object, storage.Object, Resolver(ClinicId).Object, uow.Object,
            NullLogger<DeletePatientFolderCommandHandler>.Instance);
        var result = await handler.Handle(
            new DeletePatientFolderCommand { PatientId = foreign.Id, FolderId = folder.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        folders.Verify(r => r.DeleteAsync(It.IsAny<PatientFolder>(), It.IsAny<CancellationToken>()), Times.Never);
        files.Verify(r => r.DeleteAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- DeletePatientFolderCommand integrity (AC-3, #18) -------------------

    // A blob-storage failure must NOT abort the DB deletion or leave an orphaned file row: the DB rows are
    // committed first, then blobs are deleted best-effort (a failure is logged, not swallowed, and never
    // propagates to roll back the commit).
    [Fact]
    public async Task DeleteFolder_Commits_Db_Then_Deletes_Blobs_And_Survives_Blob_Failure()
    {
        var patient = Patient(ClinicId);
        var folder = Folder(patient.Id);
        var file1 = File(patient.Id, folder.Id);
        var file2 = File(patient.Id, folder.Id);

        var folders = new Mock<IPatientFolderRepository>();
        folders.Setup(r => r.GetByIdAsync(folder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(folder);
        var files = new Mock<IPatientFileRepository>();
        files.Setup(r => r.GetByFolderIdAsync(folder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { file1, file2 });
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("blob store unavailable")); // blob delete fails for every file
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeletePatientFolderCommandHandler(
            folders.Object, files.Object, patients.Object, storage.Object, Resolver(ClinicId).Object, uow.Object,
            NullLogger<DeletePatientFolderCommandHandler>.Instance);
        var result = await handler.Handle(
            new DeletePatientFolderCommand { PatientId = patient.Id, FolderId = folder.Id }, CancellationToken.None);

        // The blob failure is logged, not surfaced — the delete still succeeds.
        Assert.True(result.IsSuccess);
        // Every file row + the folder row were removed and committed BEFORE any blob delete was attempted.
        files.Verify(r => r.DeleteAsync(file1, It.IsAny<CancellationToken>()), Times.Once);
        files.Verify(r => r.DeleteAsync(file2, It.IsAny<CancellationToken>()), Times.Once);
        folders.Verify(r => r.DeleteAsync(folder, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
