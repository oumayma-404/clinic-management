using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Documents;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// The <c>content.medications</c> wire shape the fiche de soins writes and reads — the contract it shares with
/// the document editor, which reads the same array back with plain property access.
/// </summary>
public class PrescriptionLinesTests
{
    private static readonly DateTime Intervention = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

    private static PrescriptionLineInput Medicament(
        string name, string dosage = "", string times = "", string duration = "") => new()
    {
        Kind = PrescriptionLineKinds.Medicament,
        Name = name,
        Dosage = dosage,
        TimesPerDay = times,
        Duration = duration,
    };

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ── The camelCase requirement ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The one property-naming test in this file that is not style. The document editor reads these lines
    /// with `med.timesPerDay`, which is case-sensitive, while the C# reader is deliberately case-INSENSITIVE —
    /// so a PascalCase write would hand a dentist an ordonnance whose posologies had silently emptied, and no
    /// server-side test would have noticed.
    /// </summary>
    [Fact]
    public void A_Written_Line_Uses_The_Exact_Keys_The_Browser_Reads()
    {
        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput
            {
                Lines = new List<PrescriptionLineInput>
                {
                    new()
                    {
                        Kind = PrescriptionLineKinds.Medicament,
                        Name = "Augmentin Comprimé",
                        Dosage = "1 g",
                        TimesPerDay = "3",
                        Duration = "7",
                        Route = "par voie orale",
                        Quantity = "1 boîte",
                        MedicationId = Guid.NewGuid(),
                        Dci = new List<string> { "Amoxicilline", "Acide clavulanique" },
                    },
                },
            },
            Intervention);

        var line = (JsonObject)Parse(json)["medications"]!.AsArray()[0]!;

        Assert.Equal("Augmentin Comprimé", (string?)line["name"]);
        Assert.Equal("1 g", (string?)line["dosage"]);
        Assert.Equal("3", (string?)line["timesPerDay"]);
        Assert.Equal("7", (string?)line["duration"]);
        Assert.Equal("par voie orale", (string?)line["route"]);
        Assert.Equal("1 boîte", (string?)line["quantity"]);
        Assert.Equal("medicament", (string?)line["kind"]);
        Assert.Equal(2, line["dci"]!.AsArray().Count);
    }

    /// <summary>
    /// The four the editor always writes are present even when blank, so a document opened in both surfaces
    /// never shows one of them a missing property.
    /// </summary>
    [Fact]
    public void An_Empty_Medicament_Still_Carries_The_Four_Always_Written_Keys()
    {
        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput { Lines = new List<PrescriptionLineInput> { Medicament("Ibuprofène") } },
            Intervention);

        var line = (JsonObject)Parse(json)["medications"]!.AsArray()[0]!;

        Assert.True(line.ContainsKey("name"));
        Assert.True(line.ContainsKey("dosage"));
        Assert.True(line.ContainsKey("timesPerDay"));
        Assert.True(line.ContainsKey("duration"));
        // The optional pair is omitted rather than written blank — as the editor omits them.
        Assert.False(line.ContainsKey("route"));
        Assert.False(line.ContainsKey("quantity"));
    }

    // ── The examen ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An examen carries a name and nothing else — no dosage, no posologie, no catalogue link — which is
    /// exactly what lets <c>PrescriptionContent.FormatLine</c> print it verbatim without a branch.
    /// </summary>
    [Fact]
    public void An_Examen_Line_Carries_Only_Its_Text()
    {
        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput
            {
                Lines = new List<PrescriptionLineInput>
                {
                    new()
                    {
                        Kind = PrescriptionLineKinds.Examen,
                        Name = "Radiographie panoramique dentaire",
                        // A caller may leave these populated from a line that changed kind; they must not
                        // survive, or the printed line would grow a posology for a radiograph.
                        Dosage = "1 g",
                        TimesPerDay = "3",
                        Duration = "7",
                        Route = "par voie orale",
                        Quantity = "1 boîte",
                        MedicationId = Guid.NewGuid(),
                        Dci = new List<string> { "Amoxicilline" },
                    },
                },
            },
            Intervention);

        var line = (JsonObject)Parse(json)["medications"]!.AsArray()[0]!;

        Assert.Equal("examen", (string?)line["kind"]);
        Assert.Equal("Radiographie panoramique dentaire", (string?)line["name"]);
        Assert.Equal(string.Empty, (string?)line["dosage"]);
        Assert.Equal(string.Empty, (string?)line["timesPerDay"]);
        Assert.Equal(string.Empty, (string?)line["duration"]);
        Assert.False(line.ContainsKey("route"));
        Assert.False(line.ContainsKey("quantity"));
        Assert.False(line.ContainsKey("medicationId"));
        Assert.False(line.ContainsKey("dci"));
    }

    // ── The kind's default ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something-a-later-build-invented")]
    public void An_Absent_Or_Unknown_Kind_Is_A_Medicament(string? kind)
    {
        // Every line written before this key existed is a drug, and so is any line a build ahead of this one
        // labels with a kind this one does not know.
        Assert.Equal(PrescriptionLineKinds.Medicament, PrescriptionLineKinds.Normalize(kind));
        Assert.False(PrescriptionLineKinds.IsExamen(kind));
    }

    [Fact]
    public void The_Examen_Kind_Is_Read_Case_Insensitively()
    {
        Assert.True(PrescriptionLineKinds.IsExamen("Examen"));
        Assert.True(PrescriptionLineKinds.IsExamen(" EXAMEN "));
    }

    // ── The round trip ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A reopened fiche must get its lines back as it left them, or the next save would silently rewrite the
    /// ordonnance — the `SetActs` trap on a new field.
    /// </summary>
    [Fact]
    public void Lines_Survive_A_Write_Then_Read()
    {
        var medicationId = Guid.NewGuid();
        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput
            {
                Renewals = "2",
                Lines = new List<PrescriptionLineInput>
                {
                    new()
                    {
                        Kind = PrescriptionLineKinds.Medicament,
                        Name = "Augmentin Comprimé",
                        Dosage = "1 g",
                        TimesPerDay = "3",
                        Duration = "7",
                        Route = "par voie orale",
                        Quantity = "1 boîte",
                        MedicationId = medicationId,
                        Dci = new List<string> { "Amoxicilline" },
                    },
                    new() { Kind = PrescriptionLineKinds.Examen, Name = "Bilan sanguin : NFS" },
                },
            },
            Intervention);

        var read = PrescriptionLines.Read(json);

        Assert.Equal(2, read.Count);
        Assert.Equal(PrescriptionLineKinds.Medicament, read[0].Kind);
        Assert.Equal("Augmentin Comprimé", read[0].Name);
        Assert.Equal("1 g", read[0].Dosage);
        Assert.Equal("3", read[0].TimesPerDay);
        Assert.Equal("7", read[0].Duration);
        Assert.Equal("par voie orale", read[0].Route);
        Assert.Equal("1 boîte", read[0].Quantity);
        Assert.Equal(medicationId, read[0].MedicationId);
        Assert.Equal(new[] { "Amoxicilline" }, read[0].Dci);
        Assert.Equal(PrescriptionLineKinds.Examen, read[1].Kind);
        Assert.Equal("Bilan sanguin : NFS", read[1].Name);
        Assert.Equal("2", PrescriptionLines.ReadRenewals(json));
    }

    [Fact]
    public void The_Document_Date_Is_A_Bare_Day_Never_An_Instant()
    {
        // The editor writes `content.date` as `yyyy-MM-dd`; an instant here would be a second shape for one key.
        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput { Lines = new List<PrescriptionLineInput> { Medicament("Ibuprofène") } },
            new DateTime(2026, 9, 8, 23, 30, 0, DateTimeKind.Utc));

        Assert.Equal("2026-09-08", (string?)Parse(json)["date"]);
    }

    [Fact]
    public void A_Blank_Renouvellement_Is_Not_Written_At_All()
    {
        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput
            {
                Renewals = "   ",
                Lines = new List<PrescriptionLineInput> { Medicament("Ibuprofène") },
            },
            Intervention);

        Assert.False(Parse(json).ContainsKey("renewals"));
        Assert.Null(PrescriptionLines.ReadRenewals(json));
    }

    // ── Reading what is already stored ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("\"a bare string\"")]
    public void Unreadable_Content_Yields_No_Lines_And_Never_Throws(string? contentJson)
    {
        // A prescription that cannot be reopened is worse than one that reopens with a line to retype, and an
        // exception here would surface as a French business failure on the fiche.
        Assert.Empty(PrescriptionLines.Read(contentJson));
        Assert.Empty(PrescriptionLines.ShortLabels(contentJson));
        Assert.Null(PrescriptionLines.ReadRenewals(contentJson));
    }

    /// <summary>
    /// A pre-array ordonnance holds <c>medications</c> as one plain string. It is a prescription, so the séance
    /// row must still say so rather than showing nothing.
    /// </summary>
    [Fact]
    public void A_Legacy_String_Blob_Still_Produces_A_Label()
    {
        var labels = PrescriptionLines.ShortLabels(
            "{\"medications\":\"Amoxicilline 1g, 2 fois par jour\"}");

        Assert.Equal(new[] { "Amoxicilline 1g, 2 fois par jour" }, labels);
    }

    // ── The short label ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Medicament_Label_Carries_Its_Dosage_And_An_Examen_Does_Not()
    {
        // « Augmentin » alone does not say what was prescribed; « Radiographie panoramique » already is a phrase.
        Assert.Equal("Augmentin Comprimé 1 g", PrescriptionLines.ShortLabel(null, "Augmentin Comprimé", "1 g"));
        Assert.Equal(
            "Radiographie panoramique",
            PrescriptionLines.ShortLabel(PrescriptionLineKinds.Examen, "Radiographie panoramique", "1 g"));
    }

    [Fact]
    public void An_Unnamed_Line_Has_No_Label_And_Is_Dropped_From_The_Summary()
    {
        Assert.Equal(string.Empty, PrescriptionLines.ShortLabel(null, "   ", "1 g"));

        var json = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput
            {
                Lines = new List<PrescriptionLineInput> { Medicament("Ibuprofène", "400 mg"), Medicament("  ") },
            },
            Intervention);

        Assert.Equal(new[] { "Ibuprofène 400 mg" }, PrescriptionLines.ShortLabels(json));
    }

    /// <summary>
    /// A free-text examen can be a paragraph; a table row cannot. The whole text stays on the ordonnance.
    /// </summary>
    [Fact]
    public void A_Very_Long_Examen_Label_Is_Clamped_With_An_Ellipsis()
    {
        var text = new string('a', 200);
        var label = PrescriptionLines.ShortLabel(PrescriptionLineKinds.Examen, text, null);

        Assert.True(label.Length <= 60, $"a row label ran to {label.Length} characters");
        Assert.EndsWith("…", label);
    }

    /// <summary>
    /// ⚠️ The short label is NOT the printed sentence, and this pins the difference rather than the wording:
    /// unify them and either the history row grows to a full posology or the ordonnance loses one.
    /// </summary>
    [Fact]
    public void The_Row_Label_Is_Shorter_Than_The_Printed_Line()
    {
        var printed = ClinicManagement.Infrastructure.Services.PrescriptionContent.FormatLine(
            "Augmentin Comprimé", "1 g", "3", "par voie orale", "1 boîte", "7",
            new[] { "Amoxicilline" });
        var label = PrescriptionLines.ShortLabel(PrescriptionLineKinds.Medicament, "Augmentin Comprimé", "1 g");

        Assert.Equal("Augmentin Comprimé 1 g", label);
        Assert.Contains(label, printed);
        Assert.True(printed.Length > label.Length);
    }

    // ── The examens wire shape — the second sheet ──────────────────────────────────────────────────────────────

    private static PrescriptionLineInput Examen(string name) =>
        new() { Kind = PrescriptionLineKinds.Examen, Name = name };

    /// <summary>
    /// ⚠️ One field. An examen carries what was written and nothing else — no <c>dosage</c>, no
    /// <c>timesPerDay</c>, no <c>duration</c>, and no <c>kind</c> either: every line of this document is an
    /// examen because the document type says so, and a per-line discriminator inside a single-kind array is
    /// the half-measure the médicament ordonnance was carrying before the split.
    /// </summary>
    [Fact]
    public void An_Examens_Line_Carries_Its_Name_And_Nothing_Else()
    {
        var json = PrescriptionLines.BuildExamensContentJson(
            new[] { Examen("Radiographie panoramique dentaire") }, Intervention);

        var line = (JsonObject)Parse(json)["examens"]!.AsArray()[0]!;

        Assert.Equal("Radiographie panoramique dentaire", line["name"]!.GetValue<string>());
        Assert.Equal(new[] { "name" }, line.Select(p => p.Key));
    }

    [Fact]
    public void An_Examens_Document_Never_Carries_A_Renouvellement()
    {
        var json = PrescriptionLines.BuildExamensContentJson(
            new[] { Examen("Panoramique") }, Intervention);

        Assert.False(Parse(json).ContainsKey(PrescriptionLines.RenewalsKey));
        Assert.Null(PrescriptionLines.ReadRenewals(json));
    }

    [Fact]
    public void An_Examens_Document_Carries_The_Seances_Date()
    {
        var json = PrescriptionLines.BuildExamensContentJson(
            new[] { Examen("Panoramique") }, Intervention);

        Assert.Equal("2026-09-08", Parse(json)[PrescriptionLines.DateKey]!.GetValue<string>());
    }

    [Fact]
    public void A_Nameless_Examen_Is_Not_Written()
    {
        var json = PrescriptionLines.BuildExamensContentJson(
            new[] { Examen("Panoramique"), Examen("   "), Examen("") }, Intervention);

        Assert.Single(Parse(json)["examens"]!.AsArray());
    }

    /// <summary>What the fiche reads back when a séance with a demande d'examens is reopened.</summary>
    [Fact]
    public void The_Examens_Round_Trip_As_Examen_Lines()
    {
        var json = PrescriptionLines.BuildExamensContentJson(
            new[] { Examen("Panoramique"), Examen("Bilan sanguin : NFS") }, Intervention);

        var read = PrescriptionLines.ReadExamens(json);

        Assert.Equal(new[] { "Panoramique", "Bilan sanguin : NFS" }, read.Select(l => l.Name));
        // Every one of them is an examen, so a re-save cannot put one back on the médicament sheet.
        Assert.All(read, l => Assert.True(PrescriptionLineKinds.IsExamen(l.Kind)));
        Assert.Equal(new[] { "Panoramique", "Bilan sanguin : NFS" }, PrescriptionLines.ExamenShortLabels(json));
    }

    /// <summary>
    /// The two readers look at different keys, which is what stops a sheet reporting the other's contents on a
    /// séance history row.
    /// </summary>
    [Fact]
    public void The_Two_Readers_Do_Not_See_Each_Others_Arrays()
    {
        var ordonnance = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput { Lines = new List<PrescriptionLineInput> { Medicament("Augmentin", "1 g") } },
            Intervention);
        var demande = PrescriptionLines.BuildExamensContentJson(new[] { Examen("Panoramique") }, Intervention);

        Assert.Equal(new[] { "Augmentin 1 g" }, PrescriptionLines.ShortLabels(ordonnance));
        Assert.Empty(PrescriptionLines.ExamenShortLabels(ordonnance));

        Assert.Equal(new[] { "Panoramique" }, PrescriptionLines.ExamenShortLabels(demande));
        Assert.Empty(PrescriptionLines.ShortLabels(demande));
    }

    [Fact]
    public void A_Malformed_Or_Empty_Examens_Blob_Reads_As_Nothing()
    {
        foreach (var contentJson in new[] { null, "", "   ", "not json", "{}", "{\"examens\":\"oops\"}" })
        {
            Assert.Empty(PrescriptionLines.ReadExamens(contentJson));
            Assert.Empty(PrescriptionLines.ExamenShortLabels(contentJson));
        }
    }

    // ── The split itself ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <b>An absent or unrecognised kind is a médicament</b>, decided in one place. That is what keeps
    /// every older caller — and every document written before the split — producing exactly the sheet it used
    /// to instead of silently becoming an examen.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("medicament")]
    [InlineData("MEDICAMENT")]
    [InlineData("something-nobody-has-written-yet")]
    public void A_Line_Whose_Kind_Is_Not_Examen_Goes_On_The_Medicament_Sheet(string? kind)
    {
        var (medicaments, examens) = FicheOrdonnanceEmitter.Split(new PrescriptionInput
        {
            Lines = new List<PrescriptionLineInput> { new() { Kind = kind, Name = "Augmentin" } },
        });

        Assert.Single(medicaments);
        Assert.Empty(examens);
    }

    [Theory]
    [InlineData("examen")]
    [InlineData("EXAMEN")]
    [InlineData("  Examen  ")]
    public void A_Line_Marked_Examen_Goes_On_The_Examens_Sheet(string kind)
    {
        var (medicaments, examens) = FicheOrdonnanceEmitter.Split(new PrescriptionInput
        {
            Lines = new List<PrescriptionLineInput> { new() { Kind = kind, Name = "Panoramique" } },
        });

        Assert.Empty(medicaments);
        Assert.Single(examens);
    }

    [Fact]
    public void The_Split_Drops_Nameless_Lines_And_Handles_An_Absent_Payload()
    {
        var (medicaments, examens) = FicheOrdonnanceEmitter.Split(new PrescriptionInput
        {
            Lines = new List<PrescriptionLineInput>
            {
                Medicament("Augmentin", "1 g"),
                Medicament("   "),
                Examen("Panoramique"),
                Examen(""),
            },
        });

        Assert.Single(medicaments);
        Assert.Single(examens);

        var (none, alsoNone) = FicheOrdonnanceEmitter.Split(null);
        Assert.Empty(none);
        Assert.Empty(alsoNone);
    }
}
