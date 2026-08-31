using ClinicManagement.Application.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>The near-duplicate rule is measured, not asserted</b> (<c>calendar-import-duplicate-merge</c> AC-2 to AC-6).
///
/// <para>Three corpora, and all three are the specification: names that must match, pairs that must be refused,
/// and a list of distinct people cross-multiplied so a loosening shows up as a count rather than as an argument.
/// The rule this file guards was chosen <b>by running these numbers</b> — a per-half edit budget of 1–3 edits
/// scored 16/16 on the variants but claimed <b>16</b> pairs of different people were the same; a whole-name budget
/// scored 13/14 and 14. The canonical-equality rule scores 17/17 and 1 (an intended pair). Anyone widening the
/// table re-measures instead of reasoning, which is the entire point of <see cref="DistinctPeople"/>.</para>
///
/// <para>⚠️ These are <b>suggestions shown to a human</b>, never automatic links. The import books an event onto an
/// existing patient only on a byte-exact match. That is why a false pair here costs one click and a missed pair
/// costs a permanent duplicate — the asymmetry the corpora are balanced against.</para>
/// </summary>
public class PatientNameEquivalenceTests
{
    private static bool Match(string a, string b)
    {
        var left = PatientNameEquivalence.SplitTitle(a);
        var right = PatientNameEquivalence.SplitTitle(b);
        Assert.NotNull(left);
        Assert.NotNull(right);
        return PatientNameEquivalence.AreWritingVariants(
            left!.Value.First, left.Value.Last, right!.Value.First, right.Value.Last);
    }

    /// <summary>The same person, typed two ways. Every row must be offered as a suggestion.</summary>
    public static TheoryData<string, string, string> WritingVariants() => new()
    {
        { "accent", "Youssef Mrád", "Youssef Mrad" },
        { "accent", "Chaïma Belhaj", "Chaima Belhaj" },
        { "spacing in the surname", "Chaima Ben Khalifa", "Chaima Benkhalifa" },
        { "spacing in the surname", "Sami Ben Salah", "Sami Bensalah" },
        { "hyphen", "Mohamed-Ali Gharbi", "Mohamed Ali Gharbi" },
        { "surname written first", "Zouari Fatma", "Fatma Zouari" },
        { "repeated letter", "Mohammed Ben Salah", "Mohamed Ben Salah" },
        { "repeated letter", "Aniss Kacem", "Anis Kacem" },
        { "repeated letter", "Salmaa Trabelsi", "Salma Trabelsi" },
        { "repeated letter", "Hedi Chaabane", "Hedi Chabane" },
        { "repeated letter", "Amine Trabelssi", "Amine Trabelsi" },
        { "y = i", "Karim Hamdy", "Karim Hamdi" },
        { "y = i", "Leyla Gharbi", "Leila Gharbi" },
        { "y = i", "Samy Nasri", "Sami Nasri" },
        { "ou = u", "Nour Gharbi", "Nur Gharbi" },
        { "w = u", "Aoun Msakni", "Aun Msakni" },
        { "c = s = ss", "Yacine Bouzid", "Yassine Bouzid" },
        { "c = s = ss", "Anis Kacem", "Anis Kassem" },
        { "kh = k", "Ahmed Khelifi", "Ahmed Kelifi" },
        { "gh = g", "Nour Gharbi", "Nour Garbi" },
        { "ph = f", "Ali Chaphi", "Ali Chafi" },
        { "x = ks", "Rania Sfaxi", "Rania Sfaksi" },
        { "trailing e", "Imene Nasri", "Imen Nasri" },
        { "trailing e", "Nourhene Gharbi", "Nourhen Gharbi" },
        { "silent h", "Nourhene Gharbi", "Nourene Gharbi" },
        { "silent h", "Ahmed Zouari", "Amed Zouari" },
    };

    /// <summary>Different people. Every row must be refused — a wrong « Oui » books a séance onto the wrong file.</summary>
    public static TheoryData<string, string, string> DifferentPeople() => new()
    {
        { "different vowel inside", "Imen Nasri", "Iman Nasri" },
        { "different vowel inside", "Olfa Ayari", "Alfa Ayari" },
        { "different vowel inside", "Sonia Trabelsi", "Samia Trabelsi" },
        { "different vowel inside", "Ines Ghanmi", "Anes Ghanmi" },
        { "different consonant inside", "Hamza Dridi", "Hamdi Dridi" },
        { "different consonant inside", "Mohamed Ben Salah", "Mohsen Ben Salah" },
        { "different consonant inside", "Ahmed Khelifi", "Ahlem Khelifi" },
        { "a letter added makes another name", "Ali Trabelsi", "Alia Trabelsi" },
        { "a letter added makes another name", "Sabri Ounalli", "Sabrine Ounalli" },
        { "a letter added makes another name", "Rania Sfaxi", "Rana Sfaxi" },
        { "a letter added makes another name", "Kais Hammami", "Kaies Hammami" },
        { "a letter added makes another name", "Slim Ferchichi", "Selim Ferchichi" },
        { "a letter added makes another name", "Yassine Bouzid", "Yasmine Bouzid" },
        { "a letter added makes another name", "Nour Gharbi", "Nourhene Gharbi" },
        { "siblings sharing a surname", "Ali Ben Salah", "Sami Ben Salah" },
        { "siblings sharing a surname", "Amine Trabelsi", "Sonia Trabelsi" },
        { "same given name, other surname", "Nour Gharbi", "Nour Ayari" },
        { "same given name, other surname", "Amine Rekik", "Amine Trabelsi" },
        { "ch is not s", "Chaima Belhaj", "Samia Belhaj" },
        { "ch is not s", "Chokri Belhaj", "Sokri Belhaj" },
        { "nickname is not a spelling", "Mohamed Gharbi", "Hamma Gharbi" },
        { "nickname is not a spelling", "Abdelaziz Mrad", "Aziz Mrad" },
    };

    /// <summary>
    /// A clinic's list, every pair of it distinct. Loaded on purpose with siblings and first names one letter
    /// apart, because those are what a distance-based rule cannot tell from a typo.
    /// </summary>
    private static readonly string[] DistinctPeople =
    {
        "Mehdi Bouazizi", "Hedi Chaabane", "Leila Gharbi", "Karim Hamdi", "Nadia Jelassi",
        "Youssef Mrad", "Amine Rekik", "Amine Trabelsi", "Sonia Trabelsi", "Fatma Zouari",
        "Mohamed Ben Salah", "Mohsen Ben Salah", "Ali Ben Salah", "Sami Ben Salah",
        "Samia Trabelsi", "Salma Trabelsi", "Nour Gharbi", "Nourhene Gharbi",
        "Ahmed Khelifi", "Ahlem Khelifi", "Rania Sfaxi", "Rana Sfaxi",
        "Yassine Bouzid", "Yasmine Bouzid", "Chaima Belhaj", "Chokri Belhaj",
        "Imen Nasri", "Iman Nasri", "Anis Kacem", "Aniss Kacem",
        "Olfa Ayari", "Alfa Ayari", "Wael Msakni", "Wafa Msakni",
        "Hamza Dridi", "Hamdi Dridi", "Ines Ghanmi", "Anes Ghanmi",
        "Slim Ferchichi", "Selim Ferchichi", "Kais Hammami", "Kaies Hammami",
        "Ali Trabelsi", "Alia Trabelsi", "Sabri Ounalli", "Sabrine Ounalli",
    };

    /// <summary>
    /// The one pair in <see cref="DistinctPeople"/> the rule does claim, by the owner's explicit decision: a
    /// repeated letter is a spelling variant wherever it sits, so « Aniss Kacem » is offered as « Anis Kacem ».
    /// </summary>
    private static readonly string[] IntendedPairs = { "Anis Kacem ~ Aniss Kacem" };

    [Theory]
    [MemberData(nameof(WritingVariants))]
    public void The_Same_Name_Written_Differently_Is_Suggested(string category, string a, string b)
    {
        Assert.True(Match(a, b), $"[{category}] « {a} » and « {b} » must be offered as the same person.");
    }

    [Theory]
    [MemberData(nameof(DifferentPeople))]
    public void Two_Different_People_Are_Never_Suggested(string category, string a, string b)
    {
        Assert.False(Match(a, b), $"[{category}] « {a} » and « {b} » are different patients and must NOT be paired.");
    }

    // The measurement itself. A widened table shows up here as a longer list, with both names printed.
    [Fact]
    public void Across_A_Clinics_List_Only_The_Intended_Pairs_Are_Claimed()
    {
        var claimed = new List<string>();
        for (var i = 0; i < DistinctPeople.Length; i++)
        {
            for (var j = i + 1; j < DistinctPeople.Length; j++)
            {
                if (Match(DistinctPeople[i], DistinctPeople[j]))
                {
                    claimed.Add($"{DistinctPeople[i]} ~ {DistinctPeople[j]}");
                }
            }
        }

        Assert.Equal(IntendedPairs.OrderBy(p => p), claimed.OrderBy(p => p));
    }

    // A title that cannot yield both halves is not a patient — the import refuses it before this rule is reached.
    [Theory]
    [InlineData("Karim")]
    [InlineData("   ")]
    [InlineData("")]
    public void A_Title_Without_Two_Parts_Has_No_Split(string title)
    {
        Assert.Null(PatientNameEquivalence.SplitTitle(title));
    }

    [Fact]
    public void A_Blank_Half_Is_Never_A_Variant()
    {
        Assert.False(PatientNameEquivalence.AreWritingVariants("Ali", "", "Ali", ""));
        Assert.False(PatientNameEquivalence.AreWritingVariants("Ali", "Gharbi", "Ali", "  "));
    }
}
