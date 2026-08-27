using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Official-documents production-readiness, Part C (FR-3.2 / FR-6.1). Exercises the QuestPDF generic-document
/// renderer (<see cref="PdfGenerationService"/>): the practitioner cachet is drawn when the snapshotted blob
/// resolves and falls back to the plain signature line otherwise (never failing the render), and the cabinet
/// city / TND localization replaces the old hardcoded "Paris"/euro path. The renderer emits opaque PDF bytes
/// with no text-extraction seam, so REND-5/REND-6 assert the render succeeds for the localized inputs (the
/// "Paris" literal and the euro honoraires case were removed from the render path in Parts C and A).
/// </summary>
public class GenericDocumentRenderTests
{
    // A minimal valid 1x1 PNG so QuestPDF's image decoder accepts the cachet blob.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static PdfGenerationService Service(IFileStorage storage) =>
        new(NullLogger<PdfGenerationService>.Instance, storage);

    private static MedicalDocumentPdfData BaseData() => new()
    {
        DocumentType = "certificat",
        DocumentDate = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
        PatientName = "Jean Dupont",
        ClinicName = "Cabinet Dentaire",
        ClinicAddress = "Avenue Habib Bourguiba",
        ClinicPhone = "+216 71 000 000",
        DoctorName = "Dr Alice Martin",
        DoctorSpecialty = "Médecin dentiste",
        ClinicCity = "Tunis"
    };

    // [REND-3] a snapshot cachet key resolving to a blob → the signature area uses the image.
    [Fact]
    public async Task Cachet_Image_Rendered_When_Present()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DownloadAsync("cachet-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(OnePixelPng));

        var data = BaseData();
        data.DoctorCachetKey = "cachet-key";
        data.DoctorCachetContentType = "image/png";

        var pdf = await Service(storage.Object).GeneratePdfFromDocumentDataAsync(data);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 0);
        storage.Verify(s => s.DownloadAsync("cachet-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    // [REND-4] no cachet snapshotted → plain signature line, storage never touched, no exception.
    [Fact]
    public async Task No_Cachet_Falls_Back_To_Signature_Line_Without_Touching_Storage()
    {
        var storage = new Mock<IFileStorage>();

        var pdf = await Service(storage.Object).GeneratePdfFromDocumentDataAsync(BaseData());

        Assert.True(pdf.Length > 0);
        storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [REND-4 / edge: deleted blob] a cachet key whose blob is missing at render time → no throw, still renders.
    [Fact]
    public async Task Missing_Cachet_Blob_Does_Not_Fail_Render()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("blob gone"));

        var data = BaseData();
        data.DoctorCachetKey = "missing-key";

        var pdf = await Service(storage.Object).GeneratePdfFromDocumentDataAsync(data);

        Assert.True(pdf.Length > 0);
    }

    // [REND-5] renders with the cabinet city and, when no city is snapshotted, without any place prefix —
    // never the old hardcoded "Paris", and never throwing.
    [Theory]
    [InlineData("Tunis")]
    [InlineData("Sfax")]
    [InlineData(null)]
    public async Task Generic_Doc_Renders_With_City_Not_Paris(string? city)
    {
        var data = BaseData();
        data.ClinicCity = city;

        var pdf = await Service(new Mock<IFileStorage>().Object).GeneratePdfFromDocumentDataAsync(data);

        Assert.True(pdf.Length > 0);
    }

    // [REND-6] every supported generic type renders (the euro-denominated honoraires case was removed — Part A).
    [Theory]
    [InlineData("prescription")]
    [InlineData("certificat")]
    [InlineData("liaison")]
    public async Task Supported_Generic_Types_Render_In_Tnd_Locale(string type)
    {
        var data = BaseData();
        data.DocumentType = type;
        if (type == "prescription") data.Content["medications"] = "[]";
        if (type == "liaison")
        {
            data.RecipientDoctorName = "Dr Externe";
            data.Content["content"] = "Bonjour confrère";
        }

        var pdf = await Service(new Mock<IFileStorage>().Object).GeneratePdfFromDocumentDataAsync(data);

        Assert.True(pdf.Length > 0);
    }
}
