using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The static, in-code CNAM dental nomenclature (cnam-nomenclature-lookup, AC-1). These guard the
/// integrity of the curated catalogue that <c>GET /api/cnam-nomenclature</c> serves — and, crucially,
/// the contract the editor's indicative estimate relies on: every entry's lettre clé must be one of the
/// keys the frontend reimbursement config understands ("&lt;lettreCle&gt; &lt;coefficient&gt;" is parsed
/// back for the estimate), and every coefficient must be positive or the estimate would be blank/zero.
/// </summary>
public class CnamNomenclatureProviderTests
{
    // Must mirror the lettres clés the frontend reimbursement config (lib/api/cnam-nomenclature.ts) keys on.
    private static readonly HashSet<string> KnownLettresCles = new() { "CD", "CDS", "VD", "D", "RD" };

    // The five categories the spec / UI filter by.
    private static readonly HashSet<string> ExpectedCategories = new()
    {
        "Consultation", "Soins conservateurs", "Chirurgie/Extraction", "Prothèse", "Radiologie",
    };

    private static readonly CnamNomenclatureProvider Provider = new();

    [Fact]
    public void GetAll_Returns_A_NonEmpty_Curated_Catalogue() // [AC-1]
    {
        Assert.NotEmpty(Provider.GetAll());
    }

    [Fact]
    public void GetAll_Covers_Every_Category() // [AC-1]
    {
        var categories = Provider.GetAll().Select(e => e.Category).ToHashSet();

        Assert.Equal(ExpectedCategories, categories);
    }

    [Fact]
    public void Every_Entry_Has_Required_Fields() // [AC-1]
    {
        Assert.All(Provider.GetAll(), entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.CodeActe));
            Assert.False(string.IsNullOrWhiteSpace(entry.DesignationFr));
            Assert.Contains(entry.Category, ExpectedCategories);
        });
    }

    [Fact]
    public void Every_Entry_Uses_A_Known_Lettre_Cle_And_Positive_Coefficient() // [AC-1] guards the estimate contract
    {
        Assert.All(Provider.GetAll(), entry =>
        {
            Assert.Contains(entry.LettreCle, KnownLettresCles);
            Assert.True(entry.Coefficient > 0, $"Coefficient must be positive for {entry.CodeActe}");
        });
    }

    [Fact]
    public void Code_Acte_Values_Are_Unique() // [AC-1] a stable key for the editor lookup + list rendering
    {
        var all = Provider.GetAll();
        var distinct = all.Select(e => e.CodeActe).Distinct().Count();

        Assert.Equal(all.Count, distinct);
    }
}
