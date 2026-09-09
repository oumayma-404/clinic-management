using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The printed body of a « demande d'examens » — the sheet a médicament may not share.
/// </summary>
public class ExamenContentTests
{
    private static Dictionary<string, string> Content(string examens) =>
        new() { ["examens"] = examens };

    /// <summary>
    /// ⚠️ The whole contract. An examen is printed exactly as the practitioner wrote it — no dosage, no
    /// posologie, no reformatting — because that is what a clinical instruction to a laboratoire is. It is why
    /// none of the médicament line's three contractually-identical formatters was touched by the split.
    /// </summary>
    [Fact]
    public void An_Examen_Prints_Exactly_What_Was_Written()
    {
        var body = ExamenContent.Build(Content(
            "[{\"name\":\"Bilan sanguin : NFS, glycémie, TP/INR\"}]"));

        Assert.Equal("Bilan sanguin : NFS, glycémie, TP/INR", Assert.Single(body.Lines).Text);
    }

    [Fact]
    public void The_Opening_Formula_Agrees_With_How_Many_Examens_There_Are()
    {
        var one = ExamenContent.Build(Content("[{\"name\":\"Radiographie panoramique\"}]"));
        var two = ExamenContent.Build(Content(
            "[{\"name\":\"Radiographie panoramique\"},{\"name\":\"Téléradiographie de profil\"}]"));

        Assert.Equal(ExamenContent.IntroSingular, one.Intro);
        Assert.Equal(ExamenContent.IntroPlural, two.Intro);
        Assert.Equal(2, two.Lines.Count);
    }

    [Fact]
    public void The_Requested_Examens_Keep_The_Order_They_Were_Entered_In()
    {
        var body = ExamenContent.Build(Content(
            "[{\"name\":\"Panoramique\"},{\"name\":\"Cone beam secteur 4\"},{\"name\":\"Avis ORL\"}]"));

        Assert.Equal(
            new[] { "Panoramique", "Cone beam secteur 4", "Avis ORL" },
            body.Lines.Select(l => l.Text));
    }

    [Fact]
    public void A_Nameless_Entry_Prints_Nothing_Rather_Than_A_Blank_Bullet()
    {
        var body = ExamenContent.Build(Content(
            "[{\"name\":\"Panoramique\"},{\"name\":\"   \"},{\"name\":null}]"));

        Assert.Equal("Panoramique", Assert.Single(body.Lines).Text);
    }

    /// <summary>
    /// A demande whose content cannot be parsed still renders its raw text: a prescription that shows something
    /// is recoverable, one that throws is not. Same rule as <c>PrescriptionContent</c>.
    /// </summary>
    [Fact]
    public void Malformed_Or_Plain_Content_Degrades_To_One_Verbatim_Line()
    {
        Assert.Equal(
            "Radiographie panoramique",
            Assert.Single(ExamenContent.Build(Content("Radiographie panoramique")).Lines).Text);

        Assert.Equal(
            "[{\"name\": oops",
            Assert.Single(ExamenContent.Build(Content("[{\"name\": oops")).Lines).Text);
    }

    [Fact]
    public void No_Examens_At_All_Yields_No_Lines()
    {
        Assert.Empty(ExamenContent.Build(Content("[]")).Lines);
        Assert.Empty(ExamenContent.Build(Content("   ")).Lines);
        Assert.Empty(ExamenContent.Build(new Dictionary<string, string>()).Lines);
    }

    /// <summary>
    /// ⚠️ A renouvellement never reaches this sheet, and the body has nowhere to put one even if a caller
    /// wrote the key: an examen prescription is single-use by default. Asserted so that « add renewals for
    /// symmetry with the ordonnance » fails here rather than on a patient's panoramique.
    /// </summary>
    [Fact]
    public void A_Renewals_Key_Changes_Nothing_About_The_Body()
    {
        var content = Content("[{\"name\":\"Panoramique\"}]");
        content["renewals"] = "2";

        var body = ExamenContent.Build(content);

        Assert.Equal("Panoramique", Assert.Single(body.Lines).Text);
        Assert.Equal(ExamenContent.IntroSingular, body.Intro);
        // The record type carries lines and an intro, and nothing else — there is no renewal mention to read.
        Assert.Equal(2, typeof(ExamenBody).GetProperties().Length);
    }
}
