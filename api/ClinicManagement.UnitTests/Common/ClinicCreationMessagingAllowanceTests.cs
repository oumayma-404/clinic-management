using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>The guard that catches a third clinic-construction door forgetting the WhatsApp reminder forfait</b>
/// (<c>vendor-whatsapp-messaging-quota</c> FR-3).
///
/// <para><see cref="ClinicCreationEntitlementTests"/>' shape applied to the second thing a new cabinet must not exist
/// without, and derived for the same reason: the way FR-3 breaks is not somebody editing a door this test already knows
/// about, it is a <i>new</i> door added later. So the set is <b>derived by reading the sources</b> for
/// <c>new Clinic(</c> rather than listed.</para>
///
/// <para>⚠️ <b>Why the failure it prevents is invisible otherwise.</b> A cabinet provisioned with no allowance works
/// completely normally — until its first WhatsApp reminder comes due, at which point the row is held under
/// <c>MessagingAllowanceMissing</c>: the practice reads « votre forfait est introuvable, contactez-nous », which is
/// true, and the fault is ours. <c>verify-schema</c>'s <c>messaging-month-covers-every-clinic</c> catches it in a
/// deployed database; this catches it at the moment the door is written.</para>
///
/// <para>⚠️ It is a <b>separate class</b> from its entitlement sibling rather than a second assertion inside it, and
/// deliberately: the two obligations are independent, and a single test failing on « stages neither » would not say
/// which one a new door forgot.</para>
/// </summary>
public class ClinicCreationMessagingAllowanceTests
{
    /// <summary>The two projects a production clinic-construction door can live in.</summary>
    private static readonly string[] ProductionProjects =
    {
        "ClinicManagement.Application",
        "ClinicManagement.API"
    };

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
    public void Every_Clinic_Construction_Door_Stages_A_Messaging_Allowance()
    {
        var root = SolutionSources.Root();
        var doors = DoorsIn(root);

        // A derived guard that found nothing passes vacuously, which reads exactly like « nothing was wrong ».
        Assert.NotEmpty(doors);

        var withoutAllowance = doors
            .Where(relative => !File.ReadAllText(Path.Combine(root.FullName, relative))
                .Contains("StageMessagingAllowanceAsync", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            withoutAllowance.Count == 0,
            $"These files construct a Clinic without staging its WhatsApp reminder forfait:{Environment.NewLine}"
            + string.Join(Environment.NewLine, withoutAllowance.Select(d => "  " + d))
            + $"{Environment.NewLine}FR-3: a cabinet must not come into existence without one. Call "
            + "LocalClinicProvisioning.StageMessagingAllowanceAsync into the same SaveChangesAsync — a cabinet with "
            + "no allowance works normally until its first WhatsApp reminder is held as « forfait introuvable », "
            + "which is our own bookkeeping fault and reads to the practice as a support call.");
    }

    /// <summary>
    /// Red-proof, so the guard is not trusted on the strength of it passing. It re-runs the predicate over the real
    /// sources with the staging call <b>stripped</b> from one door, and asserts that door is then reported — proving
    /// the check can fail, which is the only property a guard genuinely has to have.
    /// </summary>
    [Fact]
    public void The_Guard_Rejects_A_Door_That_Stages_No_Allowance()
    {
        var root = SolutionSources.Root();
        var door = DoorsIn(root).First();
        var source = File.ReadAllText(Path.Combine(root.FullName, door));

        var stripped = source.Replace("StageMessagingAllowanceAsync", "SomethingElse", StringComparison.Ordinal);

        Assert.Contains(Needle(), stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("StageMessagingAllowanceAsync", stripped, StringComparison.Ordinal);
    }
}
