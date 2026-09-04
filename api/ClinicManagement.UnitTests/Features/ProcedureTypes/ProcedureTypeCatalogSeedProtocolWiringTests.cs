using System.Text.RegularExpressions;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.UnitTests.Common;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// Every step protocol the catalogue seed declares must actually be attached to an act.
///
/// <para>A protocol is a <c>private static readonly ProcedureStepTemplate[]</c> field, and a row opts into one by
/// naming it — <c>DefaultSteps: FixedProsthesisSteps</c>. Nothing connects the two: a field whose row forgets the
/// argument compiles, is never reported unused (it *is* referenced, by its own declaration), and simply produces an
/// act with no séances. <c>InlayCoreSteps</c> was in exactly that state — two steps, an interval, and a docstring
/// arguing why the chained protocol is « 3 séances, not 4 » — while the row read
/// <c>new("Inlay-core (…)", 45, 80m, "Prothèse fixe", ToothCondition.Sain)</c> and shipped it single-séance.</para>
///
/// <para>The symptom is silence in both directions: the act looks ordinary in the catalogue, and the treatment
/// worklist simply never has a step to count. It surfaced only when the interval top-up declined that row and the
/// decline had to be explained.</para>
///
/// <para>⚠️ Derived from the source in both directions and holding <b>no list of act names</b> — an expectation
/// list here would need editing every time the catalogue gains a protocol, which is the point at which a guard
/// stops being run.</para>
/// </summary>
public class ProcedureTypeCatalogSeedProtocolWiringTests
{
    private static string Source => File.ReadAllText(Path.Combine(
        SolutionSources.Root().FullName,
        "ClinicManagement.Application", "Features", "ProcedureTypes", "ProcedureTypeCatalogSeed.cs"));

    [Fact]
    public void Every_declared_step_protocol_is_attached_to_an_act()
    {
        var source = Source;

        var declared = Regex
            .Matches(source, @"private static readonly ProcedureStepTemplate\[\]\s+(?<name>\w+)")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var attached = Regex
            .Matches(source, @"DefaultSteps:\s*(?<name>\w+)")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // The scan itself must be known to work: a regex that silently matches nothing would pass for ever.
        Assert.True(
            declared.Count >= 15,
            $"Expected the seed to declare many step protocols; found {declared.Count}. The scan is broken.");

        var orphaned = declared.Except(attached, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(
            orphaned.Count == 0,
            "These step protocols are declared and never attached to a seed row, so the acts they describe ship "
            + "single-séance and the worklist has no step to count: " + string.Join(", ", orphaned)
            + ". Add `DefaultSteps: <name>` to the row, or delete the protocol.");
    }

    /// <summary>
    /// The behavioural half: the constructed catalogue really carries protocols, and every interval on one is
    /// inside the band the domain accepts. This is what actually failed when the interval began to be carried
    /// through — the seed's own orthodontic 540-day wait was refused by a 365-day ceiling, taking the whole
    /// « Charger les actes courants » run down with an <c>ArgumentException</c> and a 400.
    /// </summary>
    [Fact]
    public void Seeded_protocols_are_constructible_and_their_intervals_are_in_band()
    {
        var acts = ProcedureTypeCatalogSeed.CreateFor(Guid.NewGuid()).ToList();

        var withProtocol = acts.Where(a => a.DefaultSteps.Count > 0).ToList();
        Assert.True(withProtocol.Count >= 15, $"Only {withProtocol.Count} seeded acts carry a protocol.");

        var intervals = withProtocol
            .SelectMany(a => a.DefaultSteps)
            .Select(s => s.MinDaysAfterPrevious)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        // The whole point of the fix: the intervals survive construction rather than being dropped by
        // ProcedureType.ValidateSteps, which rebuilt each template without them.
        Assert.NotEmpty(intervals);
        Assert.All(intervals, days => Assert.InRange(days, 1, 1095));
    }
}
