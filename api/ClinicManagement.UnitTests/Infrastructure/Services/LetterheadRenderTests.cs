using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Infrastructure.Services;
using ClinicManagement.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The cabinet's letterhead on the printed page. What is asserted is the thing a dentist would notice: the bands are
/// on EVERY page (a letterhead on page 1 only is a photocopy look), a missing footer or an unreadable blob degrades
/// rather than failing the document, and with no letterhead the page carries no image at all — the text header,
/// unchanged. Images are counted per page through PdfSharp, since the renderer has no text-extraction seam.
/// </summary>
public class LetterheadRenderTests
{
    private const string HeaderKey = "clinics/c/letterhead/a/header";
    private const string FooterKey = "clinics/c/letterhead/a/footer";
    private const string BodyKey = "clinics/c/letterhead/a/body";

    private static readonly byte[] HeaderPng = TestPng.White(2480, 400);
    private static readonly byte[] FooterPng = TestPng.White(2480, 200);
    private static readonly byte[] BodyPng = TestPng.White(2480, 2900);

    private readonly Mock<IFileStorage> _storage = new();

    public LetterheadRenderTests()
    {
        _storage.Setup(s => s.DownloadAsync(HeaderKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(HeaderPng));
        _storage.Setup(s => s.DownloadAsync(FooterKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(FooterPng));
        _storage.Setup(s => s.DownloadAsync(BodyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(BodyPng));
    }

    private PdfGenerationService Service() => new(NullLogger<PdfGenerationService>.Instance, _storage.Object);

    private static MedicalDocumentPdfData Ordonnance(
        int lines, string? header = HeaderKey, string? footer = FooterKey, string? body = null)
    {
        var medications = string.Join(",", Enumerable.Range(1, lines).Select(i =>
            $"{{\"name\":\"Médicament {i}\",\"dosage\":\"1 g\",\"timesPerDay\":\"2\",\"duration\":\"7 jours\"}}"));
        return new MedicalDocumentPdfData
        {
            DocumentType = DocumentTypes.Prescription,
            DocumentDate = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            PatientName = "Amine Trabelsi",
            ClinicName = "Cabinet Dentaire",
            DoctorName = "Dr Salma Ben Youssef",
            ClinicCity = "Tunis",
            Content = new Dictionary<string, string> { ["medications"] = $"[{medications}]" },
            LetterheadHeaderKey = header,
            LetterheadFooterKey = footer,
            LetterheadBodyKey = body
        };
    }

    [Fact]
    public async Task Both_Bands_Are_On_Every_Page_Of_A_Long_Ordonnance()
    {
        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 30));

        var images = ImagesPerPage(pdf);
        Assert.True(images.Count >= 2, $"expected a second page, got {images.Count}");
        Assert.All(images, count => Assert.Equal(2, count));
    }

    [Fact]
    public async Task Page_Entiere_Draws_The_Body_Behind_Every_Page_Of_A_Long_Ordonnance()
    {
        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 30, body: BodyKey));

        var images = ImagesPerPage(pdf);
        Assert.True(images.Count >= 2, $"expected a second page, got {images.Count}");
        Assert.All(images, count => Assert.Equal(3, count));
    }

    [Fact]
    public async Task An_Unreadable_Body_Drops_Only_The_Body()
    {
        _storage.Setup(s => s.DownloadAsync(BodyKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("blob gone"));

        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 3, body: BodyKey));

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(2, count));
    }

    [Fact]
    public async Task Without_A_Letterhead_The_Page_Carries_No_Image()
    {
        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 3, header: null, footer: null));

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(0, count));
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Letterhead_Without_A_Footer_Prints_The_Header_Band_Alone()
    {
        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 3, footer: null));

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task An_Unreadable_Header_Falls_Back_To_The_Text_Header_Without_Failing()
    {
        _storage.Setup(s => s.DownloadAsync(HeaderKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("blob gone"));

        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 3));

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task An_Unreadable_Footer_Drops_Only_The_Footer()
    {
        _storage.Setup(s => s.DownloadAsync(FooterKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("blob gone"));

        var pdf = await Service().GeneratePdfFromDocumentDataAsync(Ordonnance(lines: 3));

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task A_Note_D_Honoraires_Carries_The_Letterhead_Too()
    {
        var data = new InvoicePdfData
        {
            ClinicName = "Cabinet Dentaire",
            MatriculeFiscal = "1234567/A/M/000",
            LetterheadHeaderKey = HeaderKey,
            LetterheadFooterKey = FooterKey,
            PatientName = "Amine Trabelsi",
            Number = "2026-0001",
            IssueDate = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            TotalHt = 80m,
            TotalTtc = 81m,
            StampDutyAmount = 1m,
            Lines = { new InvoicePdfLine { Designation = "Détartrage", Quantity = 1, UnitPriceHt = 80m, LineTotalHt = 80m } }
        };

        var pdf = await Service().GenerateInvoicePdfAsync(data);

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(2, count));
    }

    [Fact]
    public async Task The_Preview_Draws_The_Bands_It_Is_Handed_Without_Reading_Storage()
    {
        var pdf = await Service().GeneratePdfWithLetterheadAsync(
            Ordonnance(lines: 3, header: null, footer: null), new LetterheadImages(HeaderPng, FooterPng));

        Assert.All(ImagesPerPage(pdf), count => Assert.Equal(2, count));
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A page that sets its own size or margins silently ignores the letterhead, so every <c>.Page(</c> in the
    /// renderer must hand its descriptor to <c>DocumentPage.Compose</c> — and that is the only place a margin is set.
    /// </summary>
    [Fact]
    public void Every_Page_Of_The_Renderer_Goes_Through_DocumentPage()
    {
        var path = Path.Combine(SolutionSources.Root().FullName,
            "ClinicManagement.Infrastructure", "Services", "PdfGenerationService.cs");
        var source = SolutionSources.WithoutComments(File.ReadAllText(path));

        var pages = Regex.Matches(source, @"\.Page\(\s*page\s*=>\s*\{\s*DocumentPage\.Compose\(page,");
        var allPages = Regex.Matches(source, @"\.Page\(");

        Assert.True(allPages.Count >= 5, $"expected the five document families, found {allPages.Count} pages");
        Assert.Equal(allPages.Count, pages.Count);
        Assert.DoesNotMatch(@"page\.(Margin\w*|Size)\(", source);
    }

    private static List<int> ImagesPerPage(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>().Select(page => CountImages(page.Elements.GetDictionary("/Resources"), 0)).ToList();
    }

    // Images in a resource dictionary, following form XObjects, since a renderer may wrap an image in one.
    private static int CountImages(PdfDictionary? resources, int depth)
    {
        var xobjects = resources?.Elements.GetDictionary("/XObject");
        if (xobjects == null || depth > 4)
        {
            return 0;
        }

        var count = 0;
        foreach (var item in xobjects.Elements.Values)
        {
            var dictionary = item is PdfReference reference ? reference.Value as PdfDictionary : item as PdfDictionary;
            switch (dictionary?.Elements.GetName("/Subtype"))
            {
                case "/Image":
                    count++;
                    break;
                case "/Form":
                    count += CountImages(dictionary.Elements.GetDictionary("/Resources"), depth + 1);
                    break;
            }
        }

        return count;
    }
}
