using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The certificat body is composed by the pure <see cref="CertificatTextBuilder"/> (shared shape with the
/// frontend preview / Word export). Tested here directly — the QuestPDF renderer emits opaque bytes with no
/// text seam.
/// <para>
/// ⚠️ <b>The document is ONE sentence now</b>, and most of what these tests used to assert is what was
/// withdrawn from it: the spécialité, « inscrit(e) à l'Ordre National … », the patient's date de naissance, the
/// free objet/motif and the deontological « remis en main propre » mention. The cases below therefore pin the
/// absences as hard as the presences — a reader working from `ordonnance-certificat-norms`' spec will be
/// tempted to add every one of them back.
/// </para>
/// </summary>
public class CertificatTextBuilderTests
{
    private static CertificatText Build(
        string? duration,
        string? startDate,
        string? civility = "M.",
        string? objetMotif = null,
        string? durationUnit = null) =>
        CertificatTextBuilder.Build(
            doctorName: "Alice Martin",
            patientName: "Jean Dupont",
            patientCivility: civility,
            objetMotif: objetMotif,
            duration: duration,
            durationUnit: durationUnit,
            startDateFormatted: startDate);

    /// <summary>One paragraph as plain text, formatting dropped.</summary>
    private static string Flat(IReadOnlyList<CertificatSegment> paragraph) =>
        string.Concat(paragraph.Select(s => s.Text));

    /// <summary>Exactly the runs a reader must find — the four bold facts, in order.</summary>
    private static string[] Bold(CertificatText text) =>
        text.BodyParagraphs.SelectMany(p => p).Where(s => s.Bold).Select(s => s.Text).ToArray();

    [Fact]
    public void The_Certificat_Is_The_One_Dictated_Sentence()
    {
        var text = Build(duration: "5", startDate: "21/07/2026");

        Assert.Equal(
            "Je soussigné(e) Docteur Alice Martin, certifie avoir examiné ce jour M. Jean Dupont, " +
            "et atteste que son état de santé nécessite un repos de 5 jours à compter du 21/07/2026, " +
            "sauf complications.",
            Flat(Assert.Single(text.BodyParagraphs)));
    }

    /// <summary>
    /// The patient's name, the count with its unit, and the start date — the three runs a reader has to find,
    /// and nothing else. The certificat carries no identity block, so the bold name is the only place it appears.
    /// </summary>
    [Fact]
    public void The_Facts_A_Reader_Must_Find_Are_Bold()
    {
        Assert.Equal(
            new[] { "Jean Dupont", "5 jours", "21/07/2026" },
            Bold(Build(duration: "5", startDate: "21/07/2026")));
    }

    [Fact]
    public void A_Repos_Can_Be_Counted_In_Months()
    {
        var text = Build(duration: "6", startDate: null, durationUnit: "mois");

        Assert.Contains("un repos de 6 mois, sauf complications.", text.FullText);
        Assert.Equal(new[] { "Jean Dupont", "6 mois" }, Bold(text));
    }

    /// <summary>An absent unit reads as jours, which is what every certificat issued so far holds.</summary>
    [Fact]
    public void An_Absent_Unit_Reads_As_Jours()
    {
        Assert.Contains("un repos de 6 jours", Build(duration: "6", startDate: null).FullText);
    }

    /// <summary>
    /// Every clause a reader would put back on the strength of the old spec. Each one printed on this document
    /// until the practice owner dictated the sentence above.
    /// </summary>
    [Fact]
    public void The_Withdrawn_Clauses_Are_Gone()
    {
        var text = Build(duration: "5", startDate: "21/07/2026");

        Assert.DoesNotContain("Ordre National", text.FullText);
        Assert.DoesNotContain("inscrit(e)", text.FullText);
        Assert.DoesNotContain("né(e) le", text.FullText);
        Assert.DoesNotContain("remis en main propre", text.FullText);
        Assert.DoesNotContain("faire valoir ce que de droit", text.FullText);
        Assert.DoesNotContain("Médecin dentiste", text.FullText);
    }

    /// <summary>No duration ⇒ the sentence stops after the examination, rather than asserting a repos of zero.</summary>
    [Fact]
    public void Without_A_Duration_The_Repos_Clause_Is_Omitted()
    {
        var text = Build(duration: null, startDate: null);

        Assert.Equal(
            "Je soussigné(e) Docteur Alice Martin, certifie avoir examiné ce jour M. Jean Dupont.",
            Flat(Assert.Single(text.BodyParagraphs)));
        Assert.DoesNotContain("sauf complications", text.FullText);
    }

    [Fact]
    public void A_Duration_Without_A_Start_Date_Still_Ends_Sauf_Complications()
    {
        var text = Build(duration: "3", startDate: null);

        Assert.Contains("un repos de 3 jours, sauf complications.", text.FullText);
        Assert.DoesNotContain("à compter du", text.FullText);
    }

    [Fact]
    public void A_Single_Day_Is_Singular()
    {
        var text = Build(duration: "1", startDate: null);

        Assert.Contains("un repos de 1 jour,", text.FullText);
        Assert.DoesNotContain("1 jours", text.FullText);
    }

    /// <summary>
    /// An unknown civilité prints « M./Mme » so the practitioner strikes one out — never a guess, and never a
    /// name with nothing in front of it.
    /// </summary>
    [Theory]
    [InlineData(null, "M./Mme Jean Dupont")]
    [InlineData("", "M./Mme Jean Dupont")]
    [InlineData("Mme", "Mme Jean Dupont")]
    public void The_Civility_Falls_Back_To_Both(string? civility, string expected)
    {
        Assert.Contains(expected, Build(duration: null, startDate: null, civility: civility).FullText);
    }

    /// <summary>
    /// A legacy certificat's objet/motif still prints. The field is gone from the editor, so nothing new writes
    /// one — but re-rendering an issued certificate must not silently drop a paragraph a practitioner wrote.
    /// </summary>
    [Fact]
    public void A_Legacy_ObjetMotif_Still_Prints()
    {
        var text = Build(duration: null, startDate: null, objetMotif: "atteste des soins dentaires en cours");

        Assert.Equal(2, text.BodyParagraphs.Count);
        Assert.Equal("atteste des soins dentaires en cours", Flat(text.BodyParagraphs[1]));
    }
}
