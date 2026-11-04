using System.Reflection;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// demo-cabinet AC-5.5: every PDF of the demo carries « SPÉCIMEN — DÉMONSTRATION » and the footer line, on every
/// page; no other deployment's PDF changes.
/// </summary>
public class SpecimenPdfTests
{
    private static IDemoPolicy Demo(DeploymentKind kind) =>
        new DemoPolicy(DeploymentProfile.For(kind), new ConfigurationBuilder().Build());

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Only_The_Demo_Stamps_Its_Pdfs(DeploymentKind kind)
    {
        var inner = new Mock<IPdfGenerationService>().Object;

        var service = SpecimenPdfGenerationService.For(Demo(kind), inner);

        if (kind == DeploymentKind.PublicDemo)
            Assert.IsType<SpecimenPdfGenerationService>(service);
        else
            Assert.Same(inner, service);
    }

    public static TheoryData<DeploymentKind> Kinds()
    {
        var data = new TheoryData<DeploymentKind>();
        foreach (var kind in Enum.GetValues<DeploymentKind>())
            data.Add(kind);
        return data;
    }

    // Derived from the interface: a seventh renderer the wrapper forgets to stamp fails here.
    [Theory]
    [MemberData(nameof(RendererMethods))]
    public async Task Every_Renderer_Is_Stamped_On_Every_Page(string methodName)
    {
        var blank = BlankPdf(pages: 2);
        var inner = DispatchProxy.Create<IPdfGenerationService, ReturnsPdf>();
        ((ReturnsPdf)(object)inner).Pdf = blank;
        var service = new SpecimenPdfGenerationService(inner);
        var method = typeof(IPdfGenerationService).GetMethod(methodName)!;

        var args = method.GetParameters().Select(p => p.ParameterType == typeof(CancellationToken) ? (object)CancellationToken.None : null).ToArray();
        var pdf = await (Task<byte[]>)method.Invoke(service, args)!;

        AssertStamped(pdf, before: blank);
    }

    public static TheoryData<string> RendererMethods()
    {
        var data = new TheoryData<string>();
        foreach (var method in typeof(IPdfGenerationService).GetMethods())
            data.Add(method.Name);
        return data;
    }

    // The real renderer, wrapped: a QuestPDF document and the CNAM P 061 overlay (which never goes through DocumentPage).
    [Theory]
    [InlineData("certificat")]
    [InlineData("arret-travail")]
    public async Task A_Real_Document_Is_Stamped(string documentType)
    {
        var plain = new PdfGenerationService(NullLogger<PdfGenerationService>.Instance, new Mock<IFileStorage>().Object);
        var data = Document(documentType);

        var unstamped = await plain.GeneratePdfFromDocumentDataAsync(data);
        var stamped = await SpecimenPdfGenerationService.For(Demo(DeploymentKind.PublicDemo), plain)
            .GeneratePdfFromDocumentDataAsync(data);

        AssertStamped(stamped, before: unstamped);
    }

    [Fact]
    public async Task A_Production_Document_Carries_No_Mark()
    {
        var plain = new PdfGenerationService(NullLogger<PdfGenerationService>.Instance, new Mock<IFileStorage>().Object);
        var service = SpecimenPdfGenerationService.For(Demo(DeploymentKind.HostedMultiTenant), plain);

        var pdf = await service.GeneratePdfFromDocumentDataAsync(Document("certificat"));

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.DoesNotContain("SPÉCIMEN", document.Info.Subject ?? "");
    }

    private static void AssertStamped(byte[] pdf, byte[] before)
    {
        using var original = PdfReader.Open(new MemoryStream(before), PdfDocumentOpenMode.Import);
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        Assert.Equal(SpecimenStamp.Mark, document.Info.Keywords);
        Assert.Contains(SpecimenStamp.FooterLine, document.Info.Subject);
        Assert.Equal(original.PageCount, document.PageCount);
        for (var i = 0; i < document.PageCount; i++)
        {
            // The stamp is one more content stream drawn over the page, with a translucent ink.
            Assert.Equal(original.Pages[i].Contents.Elements.Count + 1, document.Pages[i].Contents.Elements.Count);
            var states = document.Pages[i].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/ExtGState");
            Assert.NotNull(states);
            Assert.Contains(states!.Elements.Values, state =>
                state is PdfReference { Value: PdfDictionary gs } && gs.Elements.GetReal("/ca") is > 0 and < 1);
        }
    }

    private static MedicalDocumentPdfData Document(string type) => new()
    {
        DocumentType = type,
        DocumentDate = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
        PatientName = "Ross Geller",
        ClinicName = "Cabinet Dentaire Démo",
        ClinicCity = "Tunis",
        DoctorName = "Dr Monica Geller",
        DoctorSpecialty = "Médecin dentiste",
    };

    private static byte[] BlankPdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.LightGray, 10, 10, 20, 20);
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    public class ReturnsPdf : DispatchProxy
    {
        public byte[] Pdf { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Task.FromResult(Pdf);
    }
}
