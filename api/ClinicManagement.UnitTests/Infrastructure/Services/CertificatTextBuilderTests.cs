using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Official-documents production-readiness, Part D (FR-2). The certificat body is composed by the pure
/// <see cref="CertificatTextBuilder"/> (shared shape with the frontend preview / Word export). Tested here
/// directly — the QuestPDF renderer emits opaque bytes with no text seam — covering the mandatory mention
/// (REND-1), the CNOMDT ordre label (REND-2) and the optional repos clause (CERT-2/CERT-3).
/// </summary>
public class CertificatTextBuilderTests
{
    private static CertificatText Build(string? objetMotif, string? duration, string? startDate, string? ordre = "CNOMDT-12345") =>
        CertificatTextBuilder.Build(
            doctorName: "Alice Martin",
            doctorSpecialty: "Médecin dentiste",
            ordreNumber: ordre,
            clinicAddress: "Avenue Habib Bourguiba, Tunis",
            patientName: "Jean Dupont",
            patientDobFormatted: "01/01/1990",
            objetMotif: objetMotif,
            duration: duration,
            startDateFormatted: startDate);

    // [REND-1] the mandatory deontological mention is present (above the signature block).
    [Fact]
    public void Certificat_Renders_Mandatory_Deontological_Mention()
    {
        var text = Build(objetMotif: "présence ce jour", duration: null, startDate: null);

        Assert.Equal("Certificat établi à la demande de l'intéressé(e) et remis en main propre.", text.Mention);
        Assert.Contains("remis en main propre", text.FullText);
    }

    // [REND-2] the ordre label reads "Ordre National des Médecins Dentistes (CNOMDT)", never "Ordre des Médecins".
    [Fact]
    public void Certificat_Ordre_Label_Is_CNOMDT()
    {
        var text = Build(objetMotif: "présence ce jour", duration: null, startDate: null);

        Assert.Contains("Ordre National des Médecins Dentistes (CNOMDT)", text.FullText);
        Assert.DoesNotContain("l'Ordre des Médecins sous", text.FullText);
    }

    // [CERT-2] objet/motif filled, repos empty → only the objet/motif renders; no repos sentence.
    [Fact]
    public void Certificat_With_Only_ObjetMotif_Omits_Repos_Block()
    {
        var text = Build(objetMotif: "atteste des soins dentaires en cours", duration: null, startDate: null);

        Assert.Contains(text.BodyParagraphs, p => p.Contains("atteste des soins dentaires en cours"));
        Assert.DoesNotContain(text.BodyParagraphs, p => p.Contains("repos médical"));
    }

    // [CERT-3] repos fields filled → the rest sentence is present (with the start date and plural handling).
    [Fact]
    public void Certificat_With_Repos_Fields_Renders_Rest_Sentence()
    {
        var text = Build(objetMotif: null, duration: "3", startDate: "21/07/2026");

        Assert.Contains(text.BodyParagraphs,
            p => p.Contains("repos médical d'une durée de 3 jours à compter du 21/07/2026"));
    }

    // [CERT-3 / boundary] a single day is rendered singular ("1 jour", not "1 jours").
    [Fact]
    public void Certificat_Repos_Single_Day_Is_Singular()
    {
        var text = Build(objetMotif: null, duration: "1", startDate: null);

        Assert.Contains(text.BodyParagraphs, p => p.Contains("1 jour") && !p.Contains("1 jours"));
    }

    // [FR-2.5] a missing ordre falls back to a bracketed placeholder rather than an empty label.
    [Fact]
    public void Certificat_Without_Ordre_Uses_Placeholder()
    {
        var text = Build(objetMotif: "présence", duration: null, startDate: null, ordre: null);

        Assert.Contains("sous le n° [Numéro]", text.FullText);
    }
}
