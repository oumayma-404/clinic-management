using System;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Files;

/// <summary>
/// Whether a file has a picture the drawer can paint.
///
/// <para>⚠️ <b>The pair this class exists to keep together is the point.</b> Two places ask this question:
/// <c>DownloadPatientFilePreviewQuery</c> decides what to serve, and <c>PatientFileDto.HasPreview</c> decides
/// whether the browser asks at all. Answer them differently and the failure is silent in both directions — a
/// file the route would happily serve is never requested, or a tile is drawn against a route that refuses. The
/// last test here is the one that holds them together.</para>
/// </summary>
public class PatientFilePreviewPolicyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static PatientFile AHostedFile(
        string fileName = "panoramique.png",
        string contentType = "image/png",
        long size = 400 * 1024,
        string? previewKey = null) =>
        new(
            Guid.NewGuid(), PatientId, ClinicId, fileName, "clinics/aaa/abc-123",
            contentType, size, FileType.Scan,
            folderId: null, description: null, uploadedBy: null, previewStorageKey: previewKey);

    private static PatientFile AVaultFile(string fileName = "etude.dcm", string? previewKey = null) =>
        PatientFile.RegisterInVault(
            Guid.NewGuid(), PatientId, ClinicId, fileName, "application/dicom",
            340L * 1024 * 1024, FileType.Scan, ValidHash, previewKey);

    // ── The stored stand-in ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Stored_Preview_Is_What_It_Says()
    {
        Assert.True(PatientFilePreviewPolicy.HasStoredPreview(AHostedFile(previewKey: "clinics/aaa/previews/x.jpg")));
        Assert.False(PatientFilePreviewPolicy.HasStoredPreview(AHostedFile()));
    }

    /// <summary>A coffre file has no original here, so its stand-in is the only picture that can ever exist.</summary>
    [Fact]
    public void A_Vault_File_With_A_Stored_Preview_Has_Something_To_Show()
    {
        Assert.True(PatientFilePreviewPolicy.HasSomethingToShow(AVaultFile(previewKey: "clinics/aaa/previews/x.jpg")));
    }

    // ── Standing in for its own preview ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The case the fallback exists for: an ordinary radiograph uploaded before previews were built at all.
    /// </summary>
    [Fact]
    public void A_Small_Hosted_Raster_With_No_Stand_In_Serves_Its_Own_Bytes()
    {
        var file = AHostedFile(size: 400 * 1024);

        Assert.True(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(file));
        Assert.True(PatientFilePreviewPolicy.HasSomethingToShow(file));
    }

    /// <summary>
    /// ⚠️ The ceiling is not « a reasonable file » — this route is called once per tile in a list, so a 20 Mo
    /// panoramique forty times over is the clinic's morning. Past it the row keeps its icon, as before.
    /// </summary>
    [Fact]
    public void A_Large_Hosted_Raster_Keeps_Its_Icon()
    {
        var file = AHostedFile(size: FileTypeCatalog.PreviewFallbackBytes + 1);

        Assert.False(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(file));
        Assert.False(PatientFilePreviewPolicy.HasSomethingToShow(file));
    }

    /// <summary>A zero-length row is not a picture, and asking the store for it answers nothing useful.</summary>
    [Fact]
    public void A_Zero_Length_File_Cannot_Stand_In()
    {
        Assert.False(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(AHostedFile(size: 0)));
    }

    /// <summary>
    /// ⚠️ <b>The tile is an <c>&lt;img&gt;</c>.</b> A PDF is browser-previewable in the catalog's sense — the
    /// viewer frames it — but painting one into a 40 px square is a broken image, so the fallback is narrower
    /// than the flag and checks the content type too.
    /// </summary>
    [Theory]
    [InlineData("compte-rendu.pdf", "application/pdf")]
    [InlineData("empreinte.stl", "model/stl")]
    [InlineData("photo.heic", "image/heic")]
    [InlineData("radio.tiff", "image/tiff")]
    [InlineData("bon-labo.zip", "application/zip")]
    public void Only_A_Format_A_Browser_Paints_Unaided_Can_Stand_In(string fileName, string contentType)
    {
        Assert.False(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(
            AHostedFile(fileName, contentType, size: 100 * 1024)));
    }

    /// <summary>Every raster the catalog calls previewable, so the fallback is not silently narrower than it.</summary>
    [Theory]
    [InlineData("radio.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("cliche.webp", "image/webp")]
    [InlineData("anime.gif", "image/gif")]
    [InlineData("scan.bmp", "image/bmp")]
    public void Every_Paintable_Raster_Can_Stand_In(string fileName, string contentType)
    {
        Assert.True(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(
            AHostedFile(fileName, contentType, size: 100 * 1024)));
    }

    /// <summary>
    /// ⚠️ A coffre original never reached this deployment. Its <c>StorageKey</c> is null by construction, so
    /// serving « its own bytes » would be a download against a key the object store never held.
    /// </summary>
    [Fact]
    public void A_Vault_File_Never_Stands_In_For_Itself()
    {
        Assert.False(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(AVaultFile("radio.png")));
        Assert.False(PatientFilePreviewPolicy.HasSomethingToShow(AVaultFile("radio.png")));
    }

    /// <summary>
    /// ⚠️ <b>Keyed on the stored NAME, not on the content type.</b> A row written before the catalog existed
    /// carries whatever the client claimed, so believing it would serve a `.exe` as an image on the strength of
    /// a spoofed header. The name is what the validated type was decided from for every row the catalog wrote.
    /// </summary>
    [Fact]
    public void The_Format_Is_Read_From_The_Name_Not_From_A_Claimed_Content_Type()
    {
        var lying = AHostedFile("charge-utile.exe", "image/png", size: 100 * 1024);

        Assert.False(PatientFilePreviewPolicy.CanStandInForItsOwnPreview(lying));
    }

    // ── The pair ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the browser is told, case by case. ⚠️ The expected value is **written down**, not re-derived from
    /// the two halves — an assertion shaped like the implementation passes whatever the implementation does.
    /// </summary>
    [Theory]
    // A small raster with no stand-in: servable from its own bytes. The whole point of the fallback.
    [InlineData("radio.png", "image/png", 400 * 1024, null, true)]
    // The same file once a stand-in exists.
    [InlineData("radio.png", "image/png", 400 * 1024, "clinics/aaa/previews/x.jpg", true)]
    // Too large to serve per tile, and no stand-in was ever built: the icon, as before.
    [InlineData("radio.png", "image/png", 40L * 1024 * 1024, null, false)]
    // …but a stand-in makes the original's size irrelevant, which is the defect the old thumbnail gate had.
    [InlineData("radio.png", "image/png", 40L * 1024 * 1024, "clinics/aaa/previews/x.jpg", true)]
    // Nothing paints a mesh, and nothing built a stand-in for one.
    [InlineData("etude.stl", "model/stl", 400 * 1024, null, false)]
    // A stand-in built for one is shown, whatever the original is.
    [InlineData("etude.stl", "model/stl", 400 * 1024, "clinics/aaa/previews/x.jpg", true)]
    public void The_Flag_The_Browser_Reads_Says_Whether_Asking_Is_Worth_It(
        string fileName, string contentType, long size, string? previewKey, bool expected)
    {
        Assert.Equal(expected, PatientFilePreviewPolicy.HasSomethingToShow(
            AHostedFile(fileName, contentType, size, previewKey)));
    }
}
