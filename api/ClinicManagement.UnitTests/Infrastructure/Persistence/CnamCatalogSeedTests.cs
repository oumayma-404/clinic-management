using ClinicManagement.Infrastructure.Persistence;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Seed-integrity guard for the DB-backed CNAM catalog (FR-5.1/5.2). Replaces the old
/// <c>CnamNomenclatureProviderTests</c>: the curated data moved from the in-code provider to the
/// <see cref="CnamCatalogSeed"/> single-source-of-truth that the <c>AddCnamCatalog</c> migration inserts.
/// These assertions protect the contract the editor's indicative estimate relies on — every entry's
/// lettre clé must be one the VLC set values, and every coefficient must be positive.
/// </summary>
public class CnamCatalogSeedTests
{
    [Fact]
    public void Seed_Is_A_NonEmpty_Vlc_Set() // [FR-5.2]
    {
        Assert.NotEmpty(CnamCatalogSeed.LetterValues);
    }

    [Fact]
    public void Seed_Ids_Are_Unique_And_Deterministic() // [FR-5.2] stable ids across machines/regenerations
    {
        var ids = CnamCatalogSeed.LetterValues.Select(v => v.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var first = CnamCatalogSeed.LetterValues[0];
        Assert.Equal(first.Id, CnamCatalogSeed.DeterministicGuid($"cnam-vlc:{first.LettreCle}"));
    }

    [Fact] // [single-act-catalogue] VD is gone: it is in neither the NGAP arrete's art. 4 nor any CNAM tariff table.
    public void Seed_Holds_Only_Lettres_Cles_The_Nomenclature_Defines()
    {
        var expected = new HashSet<string> { CnamCatalogSeed.CD, CnamCatalogSeed.CDS, CnamCatalogSeed.D, CnamCatalogSeed.RD };
        Assert.Equal(expected, CnamCatalogSeed.LetterValues.Select(v => v.LettreCle).ToHashSet());
    }

    [Fact]
    public void Vlc_Values_Are_NonNegative_And_Keys_Unique() // [FR-5.2]
    {
        Assert.All(CnamCatalogSeed.LetterValues, v => Assert.True(v.Value >= 0));
        var keys = CnamCatalogSeed.LetterValues.Select(v => v.LettreCle).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ===================== K10 — the seeded VLC values are the convention's =====================
    //
    // The seed shipped Cd 7 / Cds 10 / D 1,200 against a convention in force since 01/01/2021 that fixes 30,000 /
    // 45,000 / 3,000 — figures older even than the convention's own recorded predecessors (Cd 18,000, D 1,700).
    // Since the indicative estimate is `coefficient × VLC × taux`, EVERY reimbursement figure the software showed
    // a patient was understated by roughly 60–75 %. These cases pin the corrected numbers and, more importantly,
    // pin that they are *derived* from the shared table rather than retyped here.

    private static decimal ValueOf(string lettreCle) =>
        CnamCatalogSeed.LetterValues.Single(v => v.LettreCle == lettreCle).Value;

    [Theory] // [K10] The three values the convention settles are seeded at the convention's figures.
    [InlineData(CnamCatalogSeed.CD, 30.000)]
    [InlineData(CnamCatalogSeed.CDS, 45.000)]
    [InlineData(CnamCatalogSeed.D, 3.000)]
    public void Seeded_Vlc_Matches_The_Convention_In_Force(string lettreCle, double expected)
    {
        Assert.Equal((decimal)expected, ValueOf(lettreCle));
    }

    [Fact] // [K10] The seed does not hold its own copy of the numbers — it reads the shared Domain table.
    public void Seeded_Vlc_Is_Derived_From_The_Convention_Table()
    {
        // A second hand-written copy of 30,000 / 45,000 / 3,000 in the seed is exactly the drift
        // CnamConventionTariffs exists to prevent, and it is the shape the original defect had.
        foreach (var value in CnamCatalogSeed.LetterValues)
        {
            var inForce = ClinicManagement.Domain.Services.CnamConventionTariffs.ValueFor(value.LettreCle);
            if (inForce.HasValue)
            {
                Assert.Equal(inForce.Value, value.Value);
            }
        }
    }

    [Theory] // [K10] A lettre clé the convention text did not settle keeps its older, still-provisional figure.
    [InlineData(CnamCatalogSeed.RD, 2.000)]
    public void Unverified_Vlc_Keeps_Its_Seeded_Value(string lettreCle, double expected)
    {
        // Deliberately NOT corrected: the convention settles nothing for Vd/Rd, so there is no figure to apply.
        // Inventing one would be the same class of defect as the stale value, only harder to notice.
        Assert.Equal((decimal)expected, ValueOf(lettreCle));
        Assert.Null(ClinicManagement.Domain.Services.CnamConventionTariffs.ValueFor(lettreCle));
    }

    // ===================== K10 — the startup correction's predicate (DEV-4) =====================

    [Theory] // [K10] The superseded figure is the OLD one, so the correction can be surgical.
    [InlineData(CnamCatalogSeed.CD, 7.000)]
    [InlineData(CnamCatalogSeed.CDS, 10.000)]
    [InlineData(CnamCatalogSeed.D, 1.200)]
    public void Superseded_Letter_Value_Returns_The_Figure_The_Seed_Used_To_Ship(string lettreCle, double legacy)
    {
        // The startup pass only overwrites a row still holding *this exact number*. That third predicate term is
        // what stops it touching a value an admin typed themselves — `SetValue` stamps `UpdatedAt` but does NOT
        // clear `IsProvisional`, so the flag alone cannot distinguish "untouched" from "deliberate" (DEV-4).
        Assert.Equal((decimal)legacy, CnamCatalogSeed.SupersededLetterValue(lettreCle));
    }

    [Theory] // [K10] Nothing to correct → null, so the pass leaves those rows entirely alone.
    [InlineData(CnamCatalogSeed.RD)]
    [InlineData("ZZ")]                // a lettre clé this seed never held
    [InlineData("")]
    [InlineData(null)]
    public void Superseded_Letter_Value_Is_Null_When_There_Is_Nothing_To_Correct(string? lettreCle)
    {
        Assert.Null(CnamCatalogSeed.SupersededLetterValue(lettreCle));
    }

    [Fact] // [K10] A superseded figure never equals the value now in force — otherwise the pass would be a no-op.
    public void Superseded_Letter_Value_Always_Differs_From_The_Value_In_Force()
    {
        foreach (var value in CnamCatalogSeed.LetterValues)
        {
            var superseded = CnamCatalogSeed.SupersededLetterValue(value.LettreCle);
            if (superseded.HasValue)
            {
                Assert.NotEqual(superseded.Value, value.Value);
            }
        }
    }

    [Fact] // [K10] Matching is case-insensitive — the caller passes a stored value, not a literal.
    public void Superseded_Letter_Value_Ignores_Case_And_Whitespace()
    {
        Assert.Equal(7.000m, CnamCatalogSeed.SupersededLetterValue("cd"));
        Assert.Equal(7.000m, CnamCatalogSeed.SupersededLetterValue("  CD  "));
    }
}
