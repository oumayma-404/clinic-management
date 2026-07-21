using System.Collections;
using System.Reflection;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// CNAM BS1 official-form overlay renderer. The renderer is <c>internal</c> and its parsing/formatting
/// logic lives on private nested types (<c>Bs1Model</c>/<c>Bs1Act</c>) and private static helpers, so the
/// pure-logic cases are exercised via reflection (no public seam, and the coordinate stamping itself is a
/// geometry concern verified numerically + visually — see progress.md). The overflow-paging and fail-fast
/// behaviours are exercised end-to-end through the public <c>Render</c> method, using the BS1 asset that the
/// Infrastructure project copies into the test output and the OS sans-serif font the resolver loads.
/// </summary>
public class CnamBs1BulletinRendererTests
{
    private static readonly Type RendererType =
        typeof(TeifXmlGenerator).Assembly.GetType("ClinicManagement.Infrastructure.Services.CnamBs1BulletinRenderer")!;

    private static readonly Type Bs1ModelType = RendererType.GetNestedType("Bs1Model", BindingFlags.NonPublic)!;
    private static readonly Type Bs1ActType = RendererType.GetNestedType("Bs1Act", BindingFlags.NonPublic)!;

    // ---- reflection helpers ----

    private static object BuildModel(Dictionary<string, string> content, string patientName = "", string? patientAge = null)
    {
        var data = new MedicalDocumentPdfData
        {
            DocumentType = "bulletin-cnam",
            PatientName = patientName,
            PatientAge = patientAge,
            Content = content,
        };

        var from = Bs1ModelType.GetMethod("From", BindingFlags.Public | BindingFlags.Static)!;
        return from.Invoke(null, new object[] { data })!;
    }

    private static string Prop(object model, string name) =>
        (string)Bs1ModelType.GetProperty(name)!.GetValue(model)!;

    private static IReadOnlyList<object> Acts(object model) =>
        ((IEnumerable)Bs1ModelType.GetProperty("Acts")!.GetValue(model)!).Cast<object>().ToList();

    private static string ActProp(object act, string name) =>
        (string)Bs1ActType.GetProperty(name)!.GetValue(act)!;

    private static IReadOnlyList<int> ChunkPageSizes(object model)
    {
        var actsList = Bs1ModelType.GetProperty("Acts")!.GetValue(model)!;
        var chunk = RendererType.GetMethod("ChunkActs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pages = (IEnumerable)chunk.Invoke(null, new[] { actsList })!;
        return pages.Cast<object>().Select(p => ((IEnumerable)p).Cast<object>().Count()).ToList();
    }

    private static byte[] Render(MedicalDocumentPdfData data)
    {
        var renderer = Activator.CreateInstance(RendererType, nonPublic: true)!;
        var method = RendererType.GetMethod("Render", BindingFlags.Public | BindingFlags.Instance)!;
        try
        {
            return (byte[])method.Invoke(renderer, new object[] { data })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static string ActsJson(int count)
    {
        var items = Enumerable.Range(1, count).Select(i =>
            $"{{\"date\":\"2026-07-{i:D2}\",\"teeth\":\"{i}\",\"codeActe\":\"D0{i}\",\"cotation\":\"C2\",\"honoraires\":\"45.5\"}}");
        return "[" + string.Join(",", items) + "]";
    }

    // ===================== Honoraires formatting (AC-3) =====================

    [Theory] // [AC-3] TND with 3 decimals (millimes), no currency symbol.
    [InlineData("12.5", "12.500")]
    [InlineData("0", "0.000")]
    [InlineData("150", "150.000")]
    [InlineData("12.345", "12.345")]
    // fix-document-cnam-accuracy #3: ',' is the Tunisian DECIMAL separator, not a thousands separator.
    // Before the fix these parsed ~1000× too large (e.g. "12,000" → "12000.000").
    [InlineData("12,000", "12.000")]
    [InlineData("35,500", "35.500")]
    [InlineData("12,5", "12.500")]
    public void Honoraires_Formatted_To_Three_Decimals(string raw, string expected)
    {
        var model = BuildModel(new Dictionary<string, string>
        {
            ["acts"] = $"[{{\"date\":\"2026-07-20\",\"honoraires\":\"{raw}\"}}]",
        });

        Assert.Equal(expected, ActProp(Acts(model).Single(), "Honoraires"));
    }

    [Theory] // [AC-3] Non-numeric / empty honoraires are passed through, never NaN or a symbol.
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    public void Honoraires_NonNumeric_Or_Empty_PassedThrough(string raw, string expected)
    {
        var model = BuildModel(new Dictionary<string, string>
        {
            ["acts"] = $"[{{\"date\":\"2026-07-20\",\"honoraires\":\"{raw}\"}}]",
        });

        Assert.Equal(expected, ActProp(Acts(model).Single(), "Honoraires"));
    }

    // ===================== Date formatting =====================

    [Theory] // ISO editor dates become the French dd/MM/yyyy the form expects; unparseable input is kept verbatim.
    [InlineData("2026-07-20", "20/07/2026")]
    [InlineData("notadate", "notadate")]
    [InlineData("", "")]
    public void Act_Date_Formatted_As_French_Date(string raw, string expected)
    {
        var model = BuildModel(new Dictionary<string, string>
        {
            ["acts"] = $"[{{\"date\":\"{raw}\",\"honoraires\":\"1\"}}]",
        });

        Assert.Equal(expected, ActProp(Acts(model).Single(), "Date"));
    }

    // ===================== Acts parsing robustness =====================

    [Theory] // Malformed / non-array acts JSON must render the form without acts rather than throw.
    [InlineData("not json")]
    [InlineData("{\"date\":\"2026-07-20\"}")] // a JSON object, not an array
    [InlineData("")]
    public void Malformed_Or_NonArray_Acts_Json_Yields_Empty_List(string actsJson)
    {
        var model = BuildModel(new Dictionary<string, string> { ["acts"] = actsJson });

        Assert.Empty(Acts(model));
    }

    [Fact] // [AC-5] An act missing its code/cotation still keeps its date and honoraires.
    public void Act_Missing_Code_And_Cotation_Still_Has_Date_And_Honoraires()
    {
        var model = BuildModel(new Dictionary<string, string>
        {
            ["acts"] = "[{\"date\":\"2026-07-20\",\"teeth\":\"11\",\"honoraires\":\"30\"}]",
        });

        var act = Acts(model).Single();
        Assert.Equal(string.Empty, ActProp(act, "CodeActe"));
        Assert.Equal(string.Empty, ActProp(act, "Cotation"));
        Assert.Equal("20/07/2026", ActProp(act, "Date"));
        Assert.Equal("30.000", ActProp(act, "Honoraires"));
    }

    // ===================== Empty identity (AC-5) =====================

    [Fact] // [AC-5] A patient with no CNAM identity produces a model with blank fields (no nulls), not an exception.
    public void From_Empty_Content_Produces_Blank_Model_Without_Throwing()
    {
        var model = BuildModel(new Dictionary<string, string>());

        Assert.Equal(string.Empty, Prop(model, "IdentifiantUnique"));
        Assert.Equal(string.Empty, Prop(model, "Regime"));
        Assert.Equal(string.Empty, Prop(model, "AssureFirstName"));
        Assert.Equal(string.Empty, Prop(model, "AssurePostalCode"));
        Assert.Equal(string.Empty, Prop(model, "ApciCode"));
        Assert.Empty(Acts(model));
    }

    // ===================== Régime / lien unknown value (edge case) =====================

    [Fact] // Edge: an unknown régime/lien value is preserved verbatim, so the stamp switch ticks nothing.
    public void Unknown_Regime_And_Lien_Preserved_Verbatim()
    {
        var model = BuildModel(new Dictionary<string, string>
        {
            ["regime"] = "RégimeInconnu",
            ["maladeLien"] = "LienInconnu",
        });

        Assert.Equal("RégimeInconnu", Prop(model, "Regime"));
        Assert.Equal("LienInconnu", Prop(model, "MaladeLien"));
    }

    [Fact] // Known régime/lien values carry through unchanged for the stamp switch to match.
    public void Known_Regime_And_Lien_Carried_Through()
    {
        var model = BuildModel(new Dictionary<string, string>
        {
            ["regime"] = "CNSS",
            ["maladeLien"] = "Conjoint",
        });

        Assert.Equal("CNSS", Prop(model, "Regime"));
        Assert.Equal("Conjoint", Prop(model, "MaladeLien"));
    }

    // ===================== Malade name-split fallback (AC-5 robustness) =====================

    [Fact] // Documents saved before the FE plumbing have no malade name keys: fall back to splitting PatientName.
    public void Malade_Name_Falls_Back_To_Splitting_PatientName()
    {
        var model = BuildModel(new Dictionary<string, string>(), patientName: "Jean Dupont");

        Assert.Equal("Jean", Prop(model, "MaladeFirstName"));
        Assert.Equal("Dupont", Prop(model, "MaladeLastName"));
    }

    [Fact] // A single-token patient name maps to the first name with an empty last name.
    public void Malade_Name_Fallback_Single_Token()
    {
        var model = BuildModel(new Dictionary<string, string>(), patientName: "Jean");

        Assert.Equal("Jean", Prop(model, "MaladeFirstName"));
        Assert.Equal(string.Empty, Prop(model, "MaladeLastName"));
    }

    [Fact] // Explicit malade keys win over the PatientName fallback.
    public void Malade_Name_Explicit_Keys_Win_Over_Fallback()
    {
        var model = BuildModel(
            new Dictionary<string, string> { ["maladeFirstName"] = "Ali", ["maladeLastName"] = "Ben Salah" },
            patientName: "Jean Dupont");

        Assert.Equal("Ali", Prop(model, "MaladeFirstName"));
        Assert.Equal("Ben Salah", Prop(model, "MaladeLastName"));
    }

    // ===================== Act pagination (AC-4) =====================

    [Theory] // [AC-4] Acts are chunked six-per-page so overflow acts get their own appended copy.
    [InlineData(0, new int[0])]
    [InlineData(6, new[] { 6 })]
    [InlineData(7, new[] { 6, 1 })]
    [InlineData(13, new[] { 6, 6, 1 })]
    public void Acts_Chunked_Six_Per_Page(int actCount, int[] expectedPageSizes)
    {
        var model = BuildModel(new Dictionary<string, string> { ["acts"] = ActsJson(actCount) });

        Assert.Equal(expectedPageSizes, ChunkPageSizes(model));
    }

    // ===================== End-to-end Render =====================

    [Fact] // [AC-1] Rendering produces a real (non-empty) PDF with at least the two BS1 pages.
    public void Render_Produces_Valid_Bs1_Pdf()
    {
        var data = SampleData(ActsJson(2));

        var bytes = Render(data);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount >= 2);
    }

    [Fact] // [AC-4] More than six acts append copies of the identity+acts page so nothing is dropped.
    public void Render_Appends_Pages_When_Acts_Exceed_Six()
    {
        // 13 acts → three act-pages (6+6+1); the base BS1 is 2 pages, so two extra pages are appended → 4.
        var data = SampleData(ActsJson(13));

        var bytes = Render(data);

        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(4, doc.PageCount);
    }

    [Fact] // [AC-5] A patient with no CNAM identity still yields the form (acts filled) without extra pages.
    public void Render_Succeeds_With_No_Cnam_Identity()
    {
        var data = new MedicalDocumentPdfData
        {
            DocumentType = "bulletin-cnam",
            Content = new Dictionary<string, string> { ["acts"] = ActsJson(2) },
        };

        var bytes = Render(data);

        Assert.True(bytes.Length > 0);
        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(2, doc.PageCount);
    }

    [Fact] // [AC-6] A missing BS1 asset fails fast with a clear French operator message (never a blank PDF).
    public void Render_Fails_Fast_With_French_Message_When_Bs1_Asset_Missing()
    {
        var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "BS1.pdf");
        var stashed = assetPath + ".stashed";
        Assert.True(File.Exists(assetPath), "Expected the BS1 asset to be copied into the test output.");

        File.Move(assetPath, stashed);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Render(SampleData(ActsJson(1))));
            Assert.Contains("BS1", ex.Message);
        }
        finally
        {
            File.Move(stashed, assetPath);
        }
    }

    private static MedicalDocumentPdfData SampleData(string actsJson) => new()
    {
        DocumentType = "bulletin-cnam",
        PatientName = "Jean Dupont",
        PatientAge = "1990-01-15",
        Content = new Dictionary<string, string>
        {
            ["identifiantUnique"] = "1234567890",
            ["regime"] = "CNSS",
            ["assureFirstName"] = "Jean",
            ["assureLastName"] = "Dupont",
            ["assureAddress"] = "12 rue de Tunis",
            ["assurePostalCode"] = "1000",
            ["maladeLien"] = "Assuré lui-même",
            ["careType"] = "APCI",
            ["apciCode"] = "42",
            ["doctorCodeProfessionnel"] = "PS-001",
            ["patientPhone"] = "20123456",
            ["acts"] = actsJson,
        },
    };
}
