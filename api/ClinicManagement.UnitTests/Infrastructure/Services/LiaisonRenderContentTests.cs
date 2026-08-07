using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Official-documents production-readiness, Part E (FR-4). The liaison body sections are composed by the pure
/// <see cref="LiaisonContent"/> builder (shared shape with the frontend preview / Word export): empty guided
/// fields are omitted (LIA-4) and a legacy free-text letter still renders as one unlabelled section (LIA-5).
/// A legacy internal-recipient letter also renders end-to-end through <see cref="PdfGenerationService"/>.
/// </summary>
public class LiaisonRenderContentTests
{
    // [LIA-4] only the filled guided fields become sections — no empty headings.
    [Fact]
    public void Empty_Optional_Fields_Are_Omitted_From_Render()
    {
        var content = new Dictionary<string, string>
        {
            ["motif"] = "avis spécialisé",
            ["examenClinique"] = "",
            ["examenRadiologique"] = "   ",
            ["actesRealises"] = "",
            ["prescriptions"] = "Amoxicilline 1g",
        };

        var sections = LiaisonContent.Build(content);

        Assert.Equal(2, sections.Count);
        Assert.Equal("Motif de la liaison", sections[0].Heading);
        Assert.Equal("avis spécialisé", sections[0].Body);
        Assert.Equal("Prescriptions", sections[1].Heading);
        Assert.DoesNotContain(sections, s => s.Heading == "Examen clinique" || s.Heading == "Examen radiologique");
    }

    // [LIA-4 / order] guided sections render in the fixed reading order regardless of dictionary order.
    [Fact]
    public void Guided_Sections_Render_In_Fixed_Order()
    {
        var content = new Dictionary<string, string>
        {
            ["prescriptions"] = "P",
            ["motif"] = "M",
            ["actesRealises"] = "A",
        };

        var headings = LiaisonContent.Build(content).Select(s => s.Heading).ToList();

        Assert.Equal(new[] { "Motif de la liaison", "Actes réalisés", "Prescriptions" }, headings);
    }

    // [LIA-5] a legacy letter carries only a free-text `content` body → one unlabelled section.
    [Fact]
    public void Legacy_Free_Text_Body_Renders_As_One_Unlabelled_Section()
    {
        var content = new Dictionary<string, string> { ["content"] = "Cher confrère, je vous adresse ce patient." };

        var sections = LiaisonContent.Build(content);

        var section = Assert.Single(sections);
        Assert.Null(section.Heading);
        Assert.Equal("Cher confrère, je vous adresse ce patient.", section.Body);
    }

    // [LIA-5] Free-text prose and the guided sections COEXIST, prose second in the reading order.
    //
    // ⚠️ This asserted the opposite until now ("guided fields take precedence, the legacy body is dropped"), which
    // was Part E's rule: `content` was a pre-Part-E fallback rendered only when no guided field was filled. A
    // later change promoted it to a first-class unlabelled section on BOTH sides — the frontend's
    // `liaisonSections()` says so in as many words, in the same order and with the same headings — so the old
    // assertion described behaviour the product had deliberately left behind.
    [Fact]
    public void Free_Text_Prose_Coexists_With_Guided_Sections()
    {
        var content = new Dictionary<string, string>
        {
            ["content"] = "prose body",
            ["motif"] = "avis",
        };

        var sections = LiaisonContent.Build(content);

        Assert.Equal(2, sections.Count);
        Assert.Equal("Motif de la liaison", sections[0].Heading);
        Assert.Null(sections[1].Heading);
        Assert.Equal("prose body", sections[1].Body);
    }

    // [LIA-5] a legacy internal-recipient liaison (recipient in the snapshot columns + free-text body, no
    // guided fields) still renders end-to-end without error.
    [Fact]
    public async Task Legacy_Internal_Recipient_Liaison_Renders_Without_Error()
    {
        var data = new MedicalDocumentPdfData
        {
            DocumentType = "liaison",
            DocumentDate = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
            PatientName = "Jean Dupont",
            ClinicName = "Cabinet Dentaire",
            ClinicAddress = "Avenue Habib Bourguiba",
            ClinicPhone = "+216 71 000 000",
            DoctorName = "Dr Alice Martin",
            DoctorSpecialty = "Médecin dentiste",
            ClinicCity = "Tunis",
            RecipientDoctorName = "Dr Interne Cabinet",
            RecipientDoctorSpecialty = "Orthodontiste",
            Content = { ["content"] = "Lettre de liaison historique." },
        };

        var service = new PdfGenerationService(NullLogger<PdfGenerationService>.Instance, new Mock<IFileStorage>().Object);
        var pdf = await service.GeneratePdfFromDocumentDataAsync(data);

        Assert.True(pdf.Length > 0);
    }
}
