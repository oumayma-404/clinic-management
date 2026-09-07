using System.Runtime.CompilerServices;
using System.Text.Json;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Drives <see cref="PhoneNumber.ToE164"/> from <c>shared/phone-e164-corpus.json</c> — the same file
/// check:responsive's <c>phone-rule-matches-the-corpus</c> drives the browser's copy from.
///
/// <para><b>Why a corpus and not a set-equality guard.</b> This repo's other mirror guards
/// (<c>RealtimeResourceResolverTests</c>, <c>OdontogramConditionMirrorTests</c>,
/// <c>CnamClosedSetContractTests</c>) compare two <i>declarations</i>, because that is all two languages can
/// share when one side is a C# enum and the other a TS record. The phone rule is different: both sides are
/// <b>pure total functions of a string</b>, so the two can be driven from the same inputs and pinned to the same
/// outputs. That is a stronger contract and it is available for this rule and no other.</para>
///
/// <para>Before this existed the mirror was held by a comment in each file saying the other was a mirror — the
/// state <c>OdontogramConditionMirrorTests</c> calls « a mirror nobody checks is a mirror that is already
/// wrong », and the state <c>ContentSecurityPolicyAgreementTests</c> was written to end.</para>
/// </summary>
public class PhoneRuleCorpusTests
{
    private sealed record Case(string Raw, string? Region, string? Expected, string Why);

    private static (string DefaultRegion, List<Case> Cases) Corpus([CallerFilePath] string here = "")
    {
        var path = Path.Combine(RepositoryRoot(here), "shared", "phone-e164-corpus.json");

        // Throw rather than skip: a contract test that passes when it cannot find one side is worse than no
        // test, because it reports green while the contract goes unchecked.
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The shared phone corpus is the authority both implementations are driven from and it was not "
                + $"found at {path}. If it moved, update this path — do NOT inline the cases here, which is the "
                + "drift this test exists to prevent.",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var cases = root.GetProperty("cases").EnumerateArray()
            .Select(c => new Case(
                c.GetProperty("raw").GetString()!,
                c.TryGetProperty("region", out var r) ? r.GetString() : null,
                c.TryGetProperty("expected", out var e) && e.ValueKind != JsonValueKind.Null ? e.GetString() : null,
                c.TryGetProperty("why", out var w) ? w.GetString() ?? "" : ""))
            .ToList();

        return (root.GetProperty("defaultRegion").GetString()!, cases);
    }

    /// <summary>
    /// Walks to the <b>repository</b> root, not the solution root: <c>ClinicManagement.sln</c> lives in
    /// <c>api/</c>, so <c>SolutionSources.Root()</c> would land one level too deep and never see
    /// <c>shared/</c> — the same trap <c>PasswordFloorSingleSourceTests</c> documents.
    /// </summary>
    private static string RepositoryRoot(string here)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException($"No repository root above {here}.");
    }

    [Fact]
    public void Every_Corpus_Case_Holds()
    {
        var (_, cases) = Corpus();
        var failures = new List<string>();

        foreach (var c in cases)
        {
            var actual = PhoneNumber.ToE164(c.Raw, c.Region);
            if (actual != c.Expected)
            {
                failures.Add(
                    $"  ToE164({JsonSerializer.Serialize(c.Raw)}, {c.Region ?? "default"}) "
                    + $"= {actual ?? "null"}, corpus says {c.Expected ?? "null"} — {c.Why}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {cases.Count} corpus case(s) disagree with the server's rule:\n"
            + string.Join('\n', failures));
    }

    /// <summary>
    /// The non-vacuity tripwire this repo asks of every derived guard: « found nothing » must not read as
    /// « nothing was wrong ». A corpus that failed to parse would otherwise pass the test above trivially.
    /// </summary>
    [Fact]
    public void The_Corpus_Holds_Both_Kinds_Of_Case_In_Quantity()
    {
        var (_, cases) = Corpus();

        Assert.True(cases.Count >= 25, $"parsed only {cases.Count} corpus cases");
        Assert.True(cases.Count(c => c.Expected != null) >= 15, "too few accept cases to prove anything");
        Assert.True(cases.Count(c => c.Expected == null) >= 8, "too few refusal cases to prove anything");
        Assert.True(cases.Count(c => c.Region != null) >= 4, "no case exercises an explicit region");
    }

    /// <summary>
    /// The fallback country has one home (AC-4). The corpus states it so the browser's copy can be held to the
    /// same value by check:responsive, which cannot read a C# constant.
    /// </summary>
    [Fact]
    public void The_Default_Region_Agrees_With_The_Corpus()
    {
        var (defaultRegion, _) = Corpus();
        // Constant first: xUnit's analyzer reads the first argument as `expected`. The corpus is still the
        // authority here — it is what the browser's copy is held to by `phone-rule-matches-the-corpus`.
        Assert.Equal(PhoneNumber.DefaultRegion, defaultRegion);
    }

    /// <summary>
    /// The red proof, run here rather than left to a reviewer breaking the rule by hand: the corpus must contain
    /// the case that distinguishes a metadata library from a digit count. Without it the whole suite would pass
    /// against a rule that accepts <c>201234567</c>, which is precisely what shipped before.
    /// </summary>
    [Fact]
    public void The_Corpus_Pins_The_Case_A_Length_Rule_Cannot_Catch()
    {
        var (_, cases) = Corpus();

        var ninthDigit = cases.SingleOrDefault(c => c.Raw == "201234567");
        Assert.NotNull(ninthDigit);
        Assert.Null(ninthDigit!.Expected);

        // And it must actually be refused — the assertion above only proves the corpus asks for it.
        Assert.Null(PhoneNumber.ToE164("201234567"));
        Assert.NotNull(PhoneNumber.ToE164("20123456"));
    }

    [Fact]
    public void A_Number_Reports_The_Country_It_Belongs_To()
    {
        Assert.Equal("TN", PhoneNumber.RegionOf("20 123 456"));
        Assert.Equal("FR", PhoneNumber.RegionOf("+33 6 12 34 56 78"));
        Assert.Equal("FR", PhoneNumber.RegionOf("06 12 34 56 78", "FR"));
        Assert.Null(PhoneNumber.RegionOf("71 555 (bureau)"));
        Assert.Null(PhoneNumber.RegionOf(null));
    }
}
