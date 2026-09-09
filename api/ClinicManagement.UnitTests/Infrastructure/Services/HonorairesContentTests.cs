using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The note d'honoraires <b>document</b>'s body — the sheet a practitioner prints, never an <c>Invoice</c>.
/// </summary>
public class HonorairesContentTests
{
    private static Dictionary<string, string> Content(string? acts = null, string? note = null)
    {
        var content = new Dictionary<string, string>();
        if (acts != null) content[HonorairesContent.ActsKey] = acts;
        if (note != null) content[HonorairesContent.NoteKey] = note;
        return content;
    }

    [Fact]
    public void A_Line_Total_Is_Quantity_Times_Unit_Price_And_The_Document_Total_Is_Their_Sum()
    {
        var acts = """[{"designation":"Détartrage","quantity":"1","unitPrice":"60.000"},{"designation":"Composite","quantity":"3","unitPrice":"75.500"}]""";

        var body = HonorairesContent.Build(Content(acts));

        Assert.Equal(2, body.Lines.Count);
        Assert.Equal(60.000m, body.Lines[0].Total);
        Assert.Equal(226.500m, body.Lines[1].Total);
        Assert.Equal(286.500m, body.Total);
    }

    // The app's own money inputs produce « 75,500 ». Reading only the period form turns a real fee into 0 on a
    // printed sheet, with nothing on screen saying so.
    [Theory]
    [InlineData("75,500", 75.500)]
    [InlineData("75.500", 75.500)]
    [InlineData("1 250,000", 1250.000)]
    public void A_Price_Is_Read_However_The_Editor_Wrote_It(string raw, double expected)
    {
        var acts = $$"""[{"designation":"Acte","quantity":"1","unitPrice":"{{raw}}"}]""";

        var body = HonorairesContent.Build(Content(acts));

        Assert.Equal((decimal)expected, body.Lines.Single().UnitPrice);
    }

    [Fact]
    public void A_Numeric_Quantity_Or_Price_Is_Read_As_Well_As_A_String_One()
    {
        var acts = """[{"designation":"Acte","quantity":2,"unitPrice":40}]""";

        var body = HonorairesContent.Build(Content(acts));

        Assert.Equal(80m, body.Lines.Single().Total);
    }

    // Defaulting an unparsable quantité to 0 would silently zero a priced act on the paper.
    [Fact]
    public void An_Unreadable_Quantity_Falls_Back_To_One_Rather_Than_Zero()
    {
        var acts = """[{"designation":"Acte","quantity":"deux","unitPrice":"50.000"}]""";

        var body = HonorairesContent.Build(Content(acts));

        Assert.Equal(1m, body.Lines.Single().Quantity);
        Assert.Equal(50.000m, body.Total);
    }

    [Fact]
    public void A_Line_With_No_Designation_Is_Dropped_Rather_Than_Printed_As_An_Empty_Priced_Row()
    {
        var acts = """[{"designation":"  ","quantity":"1","unitPrice":"50.000"},{"designation":"Acte","quantity":"1","unitPrice":"20.000"}]""";

        var body = HonorairesContent.Build(Content(acts));

        Assert.Equal("Acte", body.Lines.Single().Designation);
        Assert.Equal(20.000m, body.Total);
    }

    // A sheet the practitioner cannot get out of the app at all is worse than one with an empty table.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[{\"designation\":]")]
    public void Unreadable_Content_Yields_No_Lines_Rather_Than_Throwing(string? acts)
    {
        var body = HonorairesContent.Build(Content(acts));

        Assert.Empty(body.Lines);
        Assert.Equal(0m, body.Total);
    }

    [Fact]
    public void The_Free_Note_Is_Carried_And_A_Blank_One_Is_Absent()
    {
        Assert.Equal("Règlement à 30 jours.", HonorairesContent
            .Build(Content("[]", "  Règlement à 30 jours.  ")).Note);
        Assert.Null(HonorairesContent.Build(Content("[]", "   ")).Note);
        Assert.Null(HonorairesContent.Build(Content("[]")).Note);
    }
}
