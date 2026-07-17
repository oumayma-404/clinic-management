using System.Xml.Linq;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// TEIF XML generation (FR-1) and the B2C final-consumer buyer mapping (FR-6). The exact XSD is a spec
/// Open Question (#2) not available in-repo, so these pin the structural contract we control: valid XML,
/// the TEIF root + version, document type 380, seller matricule fiscal, the consumer buyer, the act line,
/// and the monetary totals.
/// </summary>
public class TeifXmlGeneratorTests
{
    private static TeifInvoiceInput SampleInput(string? buyerMf = null) => new()
    {
        InvoiceNumber = "2026-0001",
        IssueDate = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc),
        SellerName = "Cabinet Dentaire Test",
        SellerAddress = "Tunis",
        SellerMatriculeFiscal = "1234567A",
        BuyerName = "Mohamed Ben Ali",
        BuyerMatriculeFiscal = buyerMf,
        VatApplicable = true,
        VatRate = 7m,
        TotalHt = 100.000m,
        TotalVat = 7.000m,
        StampDutyAmount = 1.000m,
        TotalTtc = 108.000m,
        Lines = new List<TeifInvoiceLineInput>
        {
            new() { Designation = "Détartrage", Quantity = 1, UnitPriceHt = 100.000m, LineTotalHt = 100.000m },
        },
    };

    private static XDocument Generate(TeifInvoiceInput input) =>
        XDocument.Parse(new TeifXmlGenerator().Generate(input));

    // [FR-1] Output is well-formed XML rooted at TEIF with a version.
    [Fact]
    public void Generate_Produces_Valid_Teif_Root()
    {
        var doc = Generate(SampleInput());

        Assert.Equal("TEIF", doc.Root!.Name.LocalName);
        Assert.False(string.IsNullOrWhiteSpace(doc.Root.Attribute("version")?.Value));
    }

    // [FR-1] The document is a commercial invoice (type 380) carrying its number.
    [Fact]
    public void Generate_Sets_Document_Number_And_Type_380()
    {
        var doc = Generate(SampleInput());

        var docType = doc.Descendants("DocumentType").Single();
        Assert.Equal("380", docType.Attribute("code")!.Value);
        Assert.Equal("2026-0001", doc.Descendants("DocumentIdentifier").Single().Value);
    }

    // [FR-1] The seller party carries the clinic matricule fiscal.
    [Fact]
    public void Generate_Includes_Seller_Matricule_Fiscal()
    {
        var doc = Generate(SampleInput());

        Assert.Contains("1234567A", doc.ToString());
        Assert.Contains(doc.Descendants("PartnerName"), e => e.Value == "Cabinet Dentaire Test");
    }

    // [FR-6] A B2C buyer (no matricule fiscal) is still mapped as a named consumer party.
    [Fact]
    public void Generate_Maps_B2C_Consumer_Buyer()
    {
        var doc = Generate(SampleInput(buyerMf: null));

        Assert.Contains(doc.Descendants("PartnerName"), e => e.Value == "Mohamed Ben Ali");
    }

    // [FR-1] The act line and monetary totals are present.
    [Fact]
    public void Generate_Includes_Line_And_Totals()
    {
        var doc = Generate(SampleInput());

        Assert.Contains(doc.Descendants("ItemDescription"), e => e.Value == "Détartrage");

        var amounts = doc.Descendants("Moa").Select(m => m.Value).ToList();
        Assert.Contains("100.000", amounts); // total HT
        Assert.Contains("108.000", amounts); // total TTC
        Assert.Contains("1.000", amounts);   // stamp duty
    }
}
