using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// What an ordonnance actually prints, now that the fiche de soins writes one too.
///
/// <para>
/// The two properties worth pinning are both about what did <b>not</b> change: an examen line prints verbatim
/// with no branch in the formatter, and a line carrying the new <c>kind</c> key renders byte-identically to one
/// without it. Together they are why this feature touched none of the three line formatters.
/// </para>
/// </summary>
public class PrescriptionRenderContentTests
{
    private static PrescriptionBody Build(string medicationsJson, string? renewals = null)
    {
        var content = new Dictionary<string, string> { ["medications"] = medicationsJson };
        if (renewals != null)
        {
            content["renewals"] = renewals;
        }

        return PrescriptionContent.Build(content);
    }

    /// <summary>
    /// An examen carries only a name, so every optional clause is skipped and the text comes out exactly as the
    /// practitioner wrote it. ⚠️ This is the load-bearing one: it is what makes « un examen est une ligne de
    /// l'ordonnance » true without a second rendering path.
    /// </summary>
    [Fact]
    public void An_Examen_Line_Prints_Exactly_What_Was_Written()
    {
        var body = Build(
            "[{\"kind\":\"examen\",\"name\":\"Radiographie panoramique dentaire — bilan pré-implantaire\"," +
            "\"dosage\":\"\",\"timesPerDay\":\"\",\"duration\":\"\"}]");

        var line = Assert.Single(body.Lines);
        Assert.Equal("Radiographie panoramique dentaire — bilan pré-implantaire", line.Heading);
        Assert.Equal(string.Empty, line.Posology);
        Assert.Equal(string.Empty, line.Details);
    }

    /// <summary>
    /// The renderer does not know the <c>kind</c> key and must not learn it: System.Text.Json ignores an
    /// unmapped member, so the printed line is unchanged. A regression here would mean somebody added a branch.
    /// </summary>
    [Fact]
    public void The_Kind_Key_Changes_Nothing_About_A_Medicament_Line()
    {
        const string fields =
            "\"name\":\"Augmentin Comprimé\",\"dosage\":\"1 g\",\"dose\":\"1 comprimé\"," +
            "\"timesPerDay\":\"3\",\"route\":\"par voie orale\",\"quantity\":\"1 boîte\"," +
            "\"duration\":\"7\"";

        var withKind = Build("[{\"kind\":\"medicament\"," + fields + "}]");
        var without = Build("[{" + fields + "}]");

        Assert.Equal(without.Lines[0], withKind.Lines[0]);
        // Three parts, printed on three lines — and only the first two are underlined.
        Assert.Equal("Augmentin Comprimé (1 g)", withKind.Lines[0].Heading);
        Assert.Equal("1 comprimé * 3 / jour pendant 7 jours", withKind.Lines[0].Posology);
        Assert.Equal("par voie orale — quantité : 1 boîte", withKind.Lines[0].Details);
    }

    /// <summary>
    /// A durée counted in months, and the absence of the key reading as jours — which is what keeps every line
    /// written before <c>durationUnit</c> existed printing the words it always did.
    /// </summary>
    [Fact]
    public void The_Duration_Unit_Is_Jours_Unless_The_Line_Says_Mois()
    {
        var months = Build("[{\"name\":\"Fluor\",\"dose\":\"1 comprimé\",\"timesPerDay\":\"1\"," +
                           "\"duration\":\"6\",\"durationUnit\":\"mois\"}]");
        var legacy = Build("[{\"name\":\"Fluor\",\"dose\":\"1 comprimé\",\"timesPerDay\":\"1\"," +
                           "\"duration\":\"6\"}]");

        Assert.Equal("1 comprimé * 1 / jour pendant 6 mois", months.Lines[0].Posology);
        Assert.Equal("1 comprimé * 1 / jour pendant 6 jours", legacy.Lines[0].Posology);
    }

    /// <summary>
    /// The DCI stays on the line — a catalogue entry going inactive must not rewrite an issued ordonnance — and
    /// stays off the paper: after the brand name the dentist chose, it duplicates it on every catalogue
    /// médicament, on the one part of the sheet that has to be read at a glance.
    /// </summary>
    [Fact]
    public void The_Dci_Is_Snapshotted_But_Never_Printed()
    {
        var body = Build("[{\"name\":\"Augmentin\",\"dosage\":\"1 g\",\"dose\":\"1 comprimé\"," +
                         "\"timesPerDay\":\"3\",\"duration\":\"7\",\"dci\":[\"Amoxicilline\"]}]");

        var line = Assert.Single(body.Lines);
        Assert.DoesNotContain("DCI", line.Heading + line.Posology + line.Details);
    }

    [Fact]
    public void A_Legacy_String_Blob_Still_Prints_As_One_Line()
    {
        var body = Build("Amoxicilline 1g, 2 fois par jour pendant 6 jours");

        var line = Assert.Single(body.Lines);
        Assert.Equal("Amoxicilline 1g, 2 fois par jour pendant 6 jours", line.Heading);
        Assert.Equal(string.Empty, line.Posology);
        Assert.Equal(string.Empty, line.Details);
    }

    /// <summary>
    /// ⚠️ The renouvellement is withdrawn, and <b>a legacy document still holds the key</b> — there was no
    /// migration. So the property under test is that a stored value is now inert: the body is the lines and
    /// nothing else, and re-rendering an old ordonnance simply prints one line fewer.
    /// </summary>
    [Fact]
    public void A_Stored_Renouvellement_No_Longer_Renders()
    {
        var withMention = Build("[{\"name\":\"Augmentin\"}]", "non");
        var withCount = Build("[{\"name\":\"Augmentin\"}]", "2");
        var without = Build("[{\"name\":\"Augmentin\"}]");

        Assert.Equal(withoutRendering(without), withoutRendering(withMention));
        Assert.Equal(withoutRendering(without), withoutRendering(withCount));

        static string withoutRendering(PrescriptionBody body) =>
            string.Join(" | ", body.Lines.Select(l => $"{l.Heading}/{l.Posology}/{l.Details}"));
    }

    // ── The withdrawn identity lines ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// « Sexe » and « Poids » were withdrawn from the identity block on the practice owner's decision: a
    /// Tunisian dental ordonnance does not carry them, and « Sexe » was prefilled from the patient record and so
    /// printed on every one ever issued.
    ///
    /// <para>
    /// ⚠️ Nothing was migrated, so a document issued before the change still holds both keys in its
    /// <c>ContentJson</c> — they simply stop being read. This pins that: a legacy document prints neither line
    /// and does not throw on the keys' presence.
    /// </para>
    /// </summary>
    [Fact]
    public void A_Legacy_Document_Prints_Neither_Sexe_Nor_Poids()
    {
        var data = new MedicalDocumentPdfData
        {
            DocumentType = DocumentTypes.Prescription,
            PatientName = "Amine Trabelsi",
            PatientAge = "12/03/1994",
            Content = new Dictionary<string, string>
            {
                ["patientSex"] = "Homme",
                ["patientWeightKg"] = "72",
            },
        };

        var labels = DocumentIdentity.PatientLines(data).Select(l => l.Label).ToList();

        Assert.Contains("Patient", labels);
        Assert.Contains("Date de naissance", labels);
        Assert.DoesNotContain("Sexe", labels);
        Assert.DoesNotContain("Poids", labels);
    }

    // ── The specialty label's server half ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The server had no French specialty label until it began issuing documents itself; without one an
    /// ordonnance would print « Orthodontist » under the prescriber's name. The browser half is
    /// <c>web/lib/specialties.ts</c>, and <c>check:responsive</c>'s <c>specialty-labels-have-one-owner</c>
    /// compares the two maps in both directions — this only pins the fall-through, which that check cannot see.
    /// </summary>
    [Theory]
    [InlineData("Orthodontist", "Orthodontiste")]
    [InlineData("Oral Surgeon", "Chirurgien buccal")]
    [InlineData("  Dentist  ", "Médecin dentiste")]
    public void A_Known_Specialty_Key_Prints_In_French(string stored, string expected) =>
        Assert.Equal(expected, DoctorSpecialtyLabels.Label(stored));

    [Theory]
    [InlineData("Médecin dentiste")]
    [InlineData("Chirurgien maxillo-facial")]
    public void An_Unknown_Specialty_Prints_Verbatim(string stored) =>
        // Two reasons, both real: a clinic that typed its own specialty keeps it, and documents issued before
        // either map existed already hold French snapshots that must pass straight through.
        Assert.Equal(stored, DoctorSpecialtyLabels.Label(stored));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_Specialty_Prints_Nothing(string? stored) =>
        Assert.Equal(string.Empty, DoctorSpecialtyLabels.Label(stored));
}
