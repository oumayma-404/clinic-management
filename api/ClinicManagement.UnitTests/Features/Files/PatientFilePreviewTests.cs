using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
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
/// The stand-in image a hosted file carries, and what the drawer paints when it has none.
///
/// <para>⚠️ <b>Hosted uploads carried no preview at all until this pass, and that made a whole feature
/// invisible.</b> <c>PreviewStorageKey</c> was written by the coffre registration alone, so on a hosted
/// deployment — the one every clinic uses — <c>HasPreview</c> was false for every row and a patient's drawer was
/// a column of grey icons whatever was in it. The upload half is now here; the read half is the fallback below,
/// which is what covers every file already stored.</para>
///
/// <para>⚠️ <b>The two must agree, and nothing but a test can hold them together.</b> The DTO's
/// <c>HasPreview</c> decides whether the browser calls the preview route at all, and the route decides what to
/// serve. A row the route would serve but whose flag says « none » is never requested; the reverse draws a tile
/// against a 404. <see cref="A_Row_The_Route_Will_Serve_Is_A_Row_The_Browser_Is_Told_To_Ask_For"/> is the pair.</para>
/// </summary>
public class PatientFilePreviewTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private const string StoredKey = "clinics/ddd/abc-123";
    private const string PreviewKey = "clinics/ddd/previews/xyz.jpg";

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientFolderRepository> _folders = new();
    private readonly Mock<IPatientFileRepository> _files = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<IFileResidencyPolicy> _residency = new();

    public PatientFilePreviewTests()
    {
        _residency.Setup(p => p.Decide(It.IsAny<FileTypeEntry>(), It.IsAny<long>())).Returns(FileResidency.Hosted);
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(APatient());
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static Patient APatient() => new(
        PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1990, 1, 1), "M",
        new Email("a@t.tn"), new PhoneNumber("+21620000000"));

    /// <summary>A JPEG only needs its marker: the validator reads the signature, not the whole image.</summary>
    private static byte[] AJpeg(int length = 64)
    {
        var jpeg = new byte[length];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(jpeg, 0);
        return jpeg;
    }

    private static UploadPatientFileCommand ACommand() => new()
    {
        PatientId = PatientId,
        FileName = "radio.png",
        FileSize = 8,
        FileStream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
    };

    private static UploadPatientFileCommand WithPreview(byte[] bytes, string name = "preview.jpg", long? size = null)
    {
        var command = ACommand();
        command.PreviewStream = new MemoryStream(bytes);
        command.PreviewFileName = name;
        command.PreviewSize = size ?? bytes.Length;
        return command;
    }

    private UploadPatientFileCommandHandler Handler() => new(
        _patients.Object, _folders.Object, _files.Object, _storage.Object, _uow.Object,
        _resolver.Object, _residency.Object, UnboundedStorage(),
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


    /// <summary>The original goes to a generated key; a preview goes to one this test can recognise.</summary>
    private (Func<PatientFile?> recorded, Func<string?> previewPath) StorageAccepts()
    {
        PatientFile? recorded = null;
        string? previewPath = null;

        _storage
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredKey);
        _storage
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, string, CancellationToken>((_, _, _, path, _) => previewPath = path)
            .ReturnsAsync(PreviewKey);
        _files
            .Setup(r => r.AddAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFile, CancellationToken>((f, _) => recorded = f);

        return (() => recorded, () => previewPath);
    }

    // ── The upload half ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Usable_Preview_Is_Stored_Against_The_Files_Own_Id()
    {
        var (recorded, previewPath) = StorageAccepts();

        var result = await Handler().Handle(WithPreview(AJpeg()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PreviewKey, recorded()!.PreviewStorageKey);
        Assert.Equal($"previews/{recorded()!.Id:D}.jpg", previewPath());
        Assert.True(result.Value!.HasPreview);
    }

    /// <summary>Nothing is uploaded twice and nothing is refused when the browser could not build one.</summary>
    [Fact]
    public async Task No_Preview_Is_An_Ordinary_Upload()
    {
        var (recorded, previewPath) = StorageAccepts();

        var result = await Handler().Handle(ACommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(recorded()!.PreviewStorageKey);
        Assert.Null(previewPath());
    }

    /// <summary>
    /// ⚠️ <b>A preview never fails an upload.</b> The row is the record and the picture is a courtesy — so an
    /// oversized one, or one that is not the format it claims, is dropped and the file is stored regardless.
    /// </summary>
    [Fact]
    public async Task An_Oversized_Preview_Is_Dropped_And_The_File_Is_Still_Stored()
    {
        var (recorded, _) = StorageAccepts();

        var result = await Handler().Handle(
            WithPreview(AJpeg(), size: FileTypeCatalog.PreviewBytes + 1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(recorded()!.PreviewStorageKey);

        // ⚠️ `HasPreview` is deliberately NOT asserted false here, and the reason is worth stating: this row is
        // a small PNG, so the *fallback* will serve its own bytes and the flag is legitimately true even with
        // the stand-in dropped. Asserting false would have pinned the wrong fact — that a dropped preview means
        // a blank tile — which is exactly what the fallback exists to stop being true.
        _storage.Verify(
            s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_Preview_That_Is_Not_What_It_Claims_Is_Dropped_And_The_File_Is_Still_Stored()
    {
        var (recorded, _) = StorageAccepts();

        var result = await Handler().Handle(
            WithPreview(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, size: 4), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(recorded()!.PreviewStorageKey);
    }

    /// <summary>
    /// ⚠️ <b>Both blobs, not one.</b> The preview is written before the row exists too, so cleaning up only the
    /// original leaves an orphan just as surely as cleaning up neither — and this one is in the object store
    /// with nothing referencing it, so nothing will ever find it again.
    /// </summary>
    [Fact]
    public async Task A_Failed_Save_Takes_The_Preview_Back_With_The_Original()
    {
        StorageAccepts();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db save failed"));

        var result = await Handler().Handle(WithPreview(AJpeg()), CancellationToken.None);

        Assert.True(result.IsFailure);
        _storage.Verify(s => s.DeleteAsync(StoredKey, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(PreviewKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The read half, and the fallback ────────────────────────────────────────────────────────────────────

    private DownloadPatientFilePreviewQueryHandler PreviewHandler() =>
        new(_files.Object, _patients.Object, _storage.Object, _resolver.Object);

    private Task<Result<FileDownloadDto>> ServePreview(PatientFile file)
    {
        _files.Setup(r => r.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        _storage
            .Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(AJpeg()));

        return PreviewHandler().Handle(
            new DownloadPatientFilePreviewQuery { PatientId = PatientId, FileId = file.Id },
            CancellationToken.None);
    }

    private static PatientFile AHostedFile(
        string fileName = "radio.png",
        string contentType = "image/png",
        long size = 400 * 1024,
        string? previewKey = null) =>
        new(
            Guid.NewGuid(), PatientId, ClinicId, fileName, StoredKey, contentType, size, FileType.Scan,
            folderId: null, description: null, uploadedBy: null, previewStorageKey: previewKey);

    [Fact]
    public async Task A_Stored_Stand_In_Is_Served_From_Its_Own_Key()
    {
        var result = await ServePreview(AHostedFile(previewKey: PreviewKey));

        Assert.True(result.IsSuccess, result.Error);
        _storage.Verify(s => s.DownloadAsync(PreviewKey, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("image/jpeg", result.Value!.ContentType);
    }

    /// <summary>
    /// The fallback. ⚠️ <b>Serving it here rather than from the browser is what keeps it out of the journal</b>:
    /// the *download* route records an access, so a client-side fallback wrote one « fichier téléchargé » row
    /// per tile scrolled past — which is why the frontend abandoned its own.
    /// </summary>
    [Fact]
    public async Task A_Small_Hosted_Raster_With_No_Stand_In_Serves_Its_Own_Bytes()
    {
        var result = await ServePreview(AHostedFile());

        Assert.True(result.IsSuccess, result.Error);
        _storage.Verify(s => s.DownloadAsync(StoredKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ⚠️ <b>The fallback's type comes from the row, not from the key.</b> A storage key carries no extension
    /// (<c>clinics/{id}/{guid}-{timestamp}</c>), so deriving it the way a stand-in's is derived would answer
    /// <c>image/jpeg</c> for every PNG — and with <c>nosniff</c> in force the browser paints nothing at all.
    /// </summary>
    [Fact]
    public async Task The_Fallbacks_Content_Type_Is_The_Rows_Validated_One()
    {
        var result = await ServePreview(AHostedFile("radio.png", "image/png"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("image/png", result.Value!.ContentType);
    }

    [Fact]
    public async Task A_File_With_Nothing_To_Show_Is_Refused_Rather_Than_Served()
    {
        var result = await ServePreview(AHostedFile("empreinte.stl", "model/stl"));

        Assert.True(result.IsFailure);
        _storage.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// <b>The pair.</b> For every shape of row, the DTO flag the browser reads and the route's own verdict have
    /// to say the same thing — a mismatch is silent in both directions and is invisible to every type.
    /// </summary>
    [Theory]
    [InlineData("radio.png", "image/png", 400 * 1024, null)]
    [InlineData("radio.png", "image/png", 400 * 1024, PreviewKey)]
    [InlineData("radio.png", "image/png", 40L * 1024 * 1024, null)]
    [InlineData("radio.png", "image/png", 40L * 1024 * 1024, PreviewKey)]
    [InlineData("compte-rendu.pdf", "application/pdf", 400 * 1024, null)]
    [InlineData("empreinte.stl", "model/stl", 400 * 1024, null)]
    [InlineData("empreinte.stl", "model/stl", 400 * 1024, PreviewKey)]
    [InlineData("photo.heic", "image/heic", 400 * 1024, null)]
    public async Task A_Row_The_Route_Will_Serve_Is_A_Row_The_Browser_Is_Told_To_Ask_For(
        string fileName, string contentType, long size, string? previewKey)
    {
        var file = AHostedFile(fileName, contentType, size, previewKey);

        var served = await ServePreview(file);

        Assert.Equal(file.ToDto().HasPreview, served.IsSuccess);
    }
}
