using System.Text.RegularExpressions;
using ClinicManagement.Infrastructure.Persistence;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Seed-integrity guard for the DCH dental-act catalogue — the sibling of <see cref="CnamCatalogSeedTests"/> and
/// <c>MedicationCatalogSeedTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// Written for <c>adoption-qa-k</c>, which made this catalogue load-bearing for the BS1: <b>K1</b> re-pointed the
/// bulletin's act picker here, and <b>K11</b> corrected the Prothèse accord-préalable flag. Before K1 the picker
/// read <see cref="CnamCatalogSeed"/>, whose <c>CodeActe</c> values are 26 <i>internal mnemonics</i>
/// (<c>DETART</c>, <c>OBT-2F</c>, <c>EXT-SIMPLE</c>…) rather than nomenclature codes — so every bulletin filled
/// from the picker was rejected at the caisse on the code column.
/// </para>
/// <para>
/// ⚠️ The load-bearing case here is <see cref="The_Two_Catalogues_Are_Disjoint"/>. It does not merely check this
/// seed; it pins the <i>reason</i> K1 existed, so if someone later "unifies" the catalogues by giving the
/// mnemonics DCH-shaped codes (the fork the spec considered and rejected) the change has to be a deliberate one
/// that edits this test, rather than something that quietly makes the two reads interchangeable again.
/// </para>
/// </remarks>
public class DentalActCatalogSeedTests
{
    // Chapitre DCH of the CNAM "Liste des actes": DCH + section (2) + act (4).
    private static readonly Regex DchCode = new(@"^DCH\d{6}$", RegexOptions.Compiled);

    private static readonly HashSet<string> ExpectedCategories = new()
    {
        DentalActCatalogSeed.SoinsConservateurs, DentalActCatalogSeed.SoinsChirurgicaux,
        DentalActCatalogSeed.Parodontologie, DentalActCatalogSeed.Pedodontie,
        DentalActCatalogSeed.OrthopedieDentoFaciale, DentalActCatalogSeed.Prothese,
    };

    [Fact]
    public void Seed_Is_A_NonEmpty_Catalogue() // [K1]
    {
        Assert.NotEmpty(DentalActCatalogSeed.Acts);
    }

    [Fact] // [K1] Every code the bulletin's picker can supply is a real DCH nomenclature code.
    public void Every_Code_Is_A_Real_Dch_Code()
    {
        // This is the assertion the caisse enforces on paper. A single mnemonic slipping into this seed would be
        // stamped onto a BS1 and refused, and nothing else in the suite would notice.
        Assert.All(DentalActCatalogSeed.Acts, act =>
            Assert.Matches(DchCode, act.CodeActe));
    }

    [Fact] // [single-act-catalogue] There is one catalogue now, and the consultations are the only non-DCH rows.
    public void Only_The_Consultations_Sit_Outside_The_Dch_Code_Range()
    {
        Assert.All(DentalActCatalogSeed.ConsultationActs, c => Assert.DoesNotMatch(DchCode, c.CodeActe));

        var dch = DentalActCatalogSeed.Acts.Select(a => a.CodeActe).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var consultations = DentalActCatalogSeed.ConsultationActs
            .Select(c => c.CodeActe)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Empty(dch.Intersect(consultations));
    }

    [Fact] // [single-act-catalogue] Each consultation bills under a lettre cle the VLC set actually values.
    public void Consultations_Use_A_Valued_Lettre_Cle()
    {
        var valued = CnamCatalogSeed.LetterValues.Select(v => v.LettreCle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(DentalActCatalogSeed.ConsultationActs, c => Assert.Contains(c.LettreCle, valued));
    }

    [Fact]
    public void Codes_Are_Unique() // [K1]
    {
        var codes = DentalActCatalogSeed.Acts.Select(a => a.CodeActe).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_Act_Has_A_Designation_And_A_Known_Category() // [K1]
    {
        Assert.All(DentalActCatalogSeed.Acts, act =>
        {
            Assert.False(string.IsNullOrWhiteSpace(act.DesignationFr));
            Assert.Contains(act.Category, ExpectedCategories);
        });
    }

    [Fact]
    public void Seed_Ids_Are_Deterministic() // [K1]
    {
        var first = DentalActCatalogSeed.Acts[0];
        Assert.Equal(first.Id, DentalActCatalogSeed.DeterministicGuid($"dental-act:{first.CodeActe}"));
    }

    // ===================== K11 — accord préalable =====================

    [Fact] // [K11] No prosthesis act requires an accord préalable (hors plafond, without one, since April 2019).
    public void No_Prothese_Act_Requires_An_Accord_Prealable()
    {
        var prostheses = DentalActCatalogSeed.Acts
            .Where(a => a.Category == DentalActCatalogSeed.Prothese)
            .ToList();

        Assert.NotEmpty(prostheses); // guard: an empty set would make the assertion below vacuously true
        Assert.All(prostheses, act => Assert.False(
            act.RequiresAccordPrealable,
            $"{act.CodeActe} still requires an accord préalable — prostheses have not since April 2019."));
    }

    [Fact] // [K11] The families the research could NOT verify are deliberately left flagged as they were.
    public void Parodontologie_And_Odf_Keep_Their_Flags()
    {
        // The convention (art. 24) settles the *procedure*; which families need it is fixed by an arrêté conjoint
        // nobody could retrieve. So this asserts the flags were not "tidied up" along with the sourced Prothèse
        // correction — inventing the list is the failure mode the spec names explicitly. If a primary source ever
        // turns up, this test is the deliberate edit that records it.
        Assert.Contains(DentalActCatalogSeed.Acts,
            a => a.Category == DentalActCatalogSeed.OrthopedieDentoFaciale && a.RequiresAccordPrealable);
        Assert.Contains(DentalActCatalogSeed.Acts,
            a => a.Category == DentalActCatalogSeed.Parodontologie && a.RequiresAccordPrealable);
    }

    [Fact] // [K11] SupersededAccordPrealable names exactly the rows the startup correction may clear.
    public void Superseded_Accord_Prealable_Is_Limited_To_Prothese()
    {
        foreach (var act in DentalActCatalogSeed.Acts)
        {
            var superseded = DentalActCatalogSeed.SupersededAccordPrealable(act.CodeActe);

            // True for a Prothèse row (shipped `true`, now `false`), false for everything else — including a
            // Parodontologie row that legitimately still carries the flag. Getting this wrong in the permissive
            // direction would let the startup pass clear a flag nobody corrected.
            Assert.Equal(act.Category == DentalActCatalogSeed.Prothese, superseded);
        }
    }

    [Theory] // [K11] An unknown or blank code is never "superseded" — the pass must not act on it.
    [InlineData("DCH999999")]
    [InlineData("DETART")]
    [InlineData("")]
    [InlineData(null)]
    public void Superseded_Accord_Prealable_Is_False_For_Unknown_Codes(string? codeActe)
    {
        Assert.False(DentalActCatalogSeed.SupersededAccordPrealable(codeActe));
    }

    [Fact] // [K11] Matching is case-insensitive, since the caller passes a stored value rather than a literal.
    public void Superseded_Accord_Prealable_Ignores_Case_And_Whitespace()
    {
        var prosthesis = DentalActCatalogSeed.Acts.First(a => a.Category == DentalActCatalogSeed.Prothese);

        Assert.True(DentalActCatalogSeed.SupersededAccordPrealable(prosthesis.CodeActe.ToLowerInvariant()));
        Assert.True(DentalActCatalogSeed.SupersededAccordPrealable($"  {prosthesis.CodeActe}  "));
    }
}
