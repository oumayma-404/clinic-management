using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>The guard that catches a third clinic-construction door</b> (FR-4, AC-1.2a).
///
/// <para>A cabinet must not come into existence without an entitlement, and the way that breaks is not somebody
/// editing a door this test already knows about — it is a <i>new</i> door added later. So the set of doors is
/// <b>derived by reading the sources</b> for <c>new Clinic(</c>, and every one of them is asserted to stage an
/// entitlement. The <c>DeploymentProfileCoverageTests</c> / <c>SystemWideCallerCoverageTests</c> shape, and the
/// opposite of a list of files to check.</para>
///
/// <para>⚠️ <b>Scoped to Application + API, and that scope is the difference between a guard and noise.</b>
/// <c>new Clinic(</c> appears about nineteen times in the solution and all but two are <b>test fixtures</b>; an
/// unscoped scan would fail on the next test that happens to build a <c>Clinic</c>, and a guard that cries wolf on
/// unrelated work gets deleted rather than fixed.</para>
///
/// <para>⚠️ <b>Why the failure it prevents is invisible otherwise.</b> A cabinet created with no entitlement works
/// completely normally — until the gate refuses every write it attempts, at which point the practice cannot record
/// anything and nothing in any log says why. <c>verify-schema</c>'s <c>every-clinic-has-an-entitlement</c> catches
/// it in a deployed database; this catches it at the moment the door is written.</para>
/// </summary>
public class ClinicCreationEntitlementTests
{
    /// <summary>The two projects a production clinic-construction door can live in.</summary>
    private static readonly string[] ProductionProjects =
    {
        "ClinicManagement.Application",
        "ClinicManagement.API"
    };

    /// <summary>
    /// How many production doors there are today. Asserted <b>exactly</b>, in both directions: a third door that
    /// stages its entitlement correctly still fails this, because « two doors » is a fact a reviewer should have to
    /// re-confirm rather than something that drifts upward silently.
    /// </summary>
    private const int ExpectedDoorCount = 2;

    private static string Needle() => "new Clinic" + "(";

    private static IReadOnlyList<string> DoorsIn(DirectoryInfo root) =>
        SolutionSources.CsFiles(root)
            .Where(path => ProductionProjects.Any(project =>
                Path.GetRelativePath(root.FullName, path)
                    .StartsWith(project + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .Where(path => File.ReadAllText(path).Contains(Needle(), StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root.FullName, path))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Every_Clinic_Construction_Door_Stages_An_Entitlement()
    {
        var root = SolutionSources.Root();
        var doors = DoorsIn(root);

        Assert.NotEmpty(doors);

        var withoutEntitlement = doors
            .Where(relative =>
            {
                var source = File.ReadAllText(Path.Combine(root.FullName, relative));
                // Either the shared stager, or the provisioning helper it wraps — a door may legitimately reach
                // the entitlement through either, and pinning one spelling would make a harmless refactor red.
                return !source.Contains("StageEntitlementAsync", StringComparison.Ordinal)
                       && !source.Contains("CreateForNewClinic", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(
            withoutEntitlement.Count == 0,
            $"These files construct a Clinic without staging its entitlement:{Environment.NewLine}"
            + string.Join(Environment.NewLine, withoutEntitlement.Select(d => "  " + d))
            + $"{Environment.NewLine}FR-4: a cabinet must not come into existence without one. Call "
            + "LocalClinicProvisioning.StageEntitlementAsync into the same SaveChangesAsync — a cabinet with no "
            + "entitlement works normally until the gate refuses every write, with nothing to say why.");
    }

    /// <summary>
    /// The count, so AC-1.2a's « both construction doors » stays a reviewed number. If this fails because a door
    /// was legitimately added, update the constant <b>and</b> confirm the new door stages an entitlement — the test
    /// above will already have told you if it does not.
    /// </summary>
    [Fact]
    public void There_Are_Exactly_Two_Production_Clinic_Construction_Doors()
    {
        var doors = DoorsIn(SolutionSources.Root());

        Assert.Equal(ExpectedDoorCount, doors.Count);
    }

    /// <summary>
    /// Red-proof, so the guard is not trusted on the strength of it passing. It re-runs the same predicate over the
    /// real sources with the entitlement call <b>stripped</b> from one door, and asserts that door is then reported
    /// — proving the check can fail, which is the only property a guard genuinely has to have.
    /// </summary>
    [Fact]
    public void The_Guard_Rejects_A_Door_That_Stages_No_Entitlement()
    {
        var root = SolutionSources.Root();
        var door = DoorsIn(root).First();
        var source = File.ReadAllText(Path.Combine(root.FullName, door));

        var stripped = source
            .Replace("StageEntitlementAsync", "SomethingElse", StringComparison.Ordinal)
            .Replace("CreateForNewClinic", "SomethingElse", StringComparison.Ordinal);

        Assert.Contains(Needle(), stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("StageEntitlementAsync", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateForNewClinic", stripped, StringComparison.Ordinal);
    }
}
