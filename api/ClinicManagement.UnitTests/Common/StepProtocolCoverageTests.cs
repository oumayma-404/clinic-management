using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every path that puts a devis act into a live plan must apply that act's catalogue step protocol.</b>
///
/// <para><b>Why this guard exists.</b> <c>ProcedureType.DefaultSteps</c> shipped with an editor in the
/// catalogue, three seeded protocols in <c>ProcedureTypeCatalogSeed</c>, a column, a DTO field and a
/// frontend form — and <b>no consumer</b>. A dentist could configure « Couronne / bridge → préparation ·
/// empreinte · scellement » and every devis still arrived with an empty « Étapes » strip, because nothing
/// copied the protocol onto an act. Found by opening the page, not by any test: it is the repository's
/// signature defect — a correct, well-documented default wired to nobody — and the cure is a check derived
/// from the sources rather than a promise to remember.</para>
///
/// <para>⚠️ <b>Two paths, and they are structurally different.</b> A new devis is accepted through
/// <c>DevisNumbering.AcceptAndSaveAsync</c>, which applies the protocol itself, so every caller of it is
/// covered by construction — that is the whole reason the call lives inside acceptance instead of beside it.
/// An <b>amendment</b> adds acts to a plan that is already Accepted and never goes near numbering, so it must
/// apply the protocol explicitly. This guard holds both halves: acceptance still owns it, and any file that
/// grows acts on a live plan applies it.</para>
///
/// <para>The failure it prevents is silent. A protocol that is not applied produces an act with no steps,
/// which is a perfectly valid act — the booking dialog offers no step chips, « Traitements en cours » never
/// lists it, and the dentist retypes the same three étapes by hand. Nothing logs, nothing throws.</para>
/// </summary>
public class StepProtocolCoverageTests
{
    private const string Applier = "TreatmentPlanStepProtocol";
    private const string AcceptanceFile = "DevisNumbering.cs";

    /// <summary>
    /// Growing a live plan's act list. <c>AddItems</c> is the aggregate's only post-acceptance way in —
    /// <c>SetItems</c> is <c>EnsureDraft()</c>-only, and a Draft may not hold steps at all.
    ///
    /// <para>⚠️ Anchored on the receiver's dot and the call's parens, as the sibling guards are: without the
    /// dot a doc comment naming <c>AddItems</c> counts as a call site, and comments are where this repository
    /// keeps its reasoning.</para>
    /// </summary>
    private static readonly Regex GrowsALivePlan = new(
        @"\.\s*AddItems\s*\(", RegexOptions.Compiled);

    private static readonly Regex AppliesTheProtocol = new(
        Regex.Escape(Applier) + @"\s*\.\s*ApplyAsync\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Files that grow a live plan's acts without applying the protocol themselves, each with the reason it is
    /// sound anyway.
    ///
    /// <para>⚠️ Asserted <b>empty</b>. It exists to be the place a real exemption is argued in writing, and an
    /// empty map is the claim that today there is none — not a formality. The moment it holds an entry, that
    /// entry has to survive being read aloud.</para>
    /// </summary>
    private static readonly Dictionary<string, string> AppliesItElsewhere = new(StringComparer.Ordinal);

    [Fact]
    public void Every_Path_That_Adds_Acts_To_A_Live_Plan_Applies_The_Catalogue_Protocol()
    {
        var root = SolutionSources.Root();
        var offenders = new List<string>();
        var candidates = 0;

        foreach (var file in SolutionSources.CsFiles(root))
        {
            var name = Path.GetFileName(file);

            // Production code only. The aggregate declares AddItems and the suite calls it constantly to build
            // fixtures; neither is a path a devis reaches in the product.
            if (file.Contains("ClinicManagement.UnitTests", StringComparison.Ordinal)
                || name == "TreatmentPlan.cs")
            {
                continue;
            }

            var source = SolutionSources.WithoutComments(File.ReadAllText(file));
            if (!GrowsALivePlan.IsMatch(source))
            {
                continue;
            }

            candidates++;

            if (AppliesTheProtocol.IsMatch(source))
            {
                continue;
            }

            if (AppliesItElsewhere.ContainsKey(name))
            {
                continue;
            }

            offenders.Add(
                $"{name} adds acts to a live treatment plan but never calls {Applier}.ApplyAsync — the acts it "
                + "adds will carry no étapes, whatever protocol their procedure defines, and nothing will say so");
        }

        Assert.True(
            candidates > 0,
            $"Found no file calling .AddItems( outside the aggregate and the suite. The amendment path is "
            + $"supposed to be one, so the scan is broken — and a guard that matches nothing holds nothing.");

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Acceptance keeps owning the protocol. Every new devis is accepted through <c>AcceptAndSaveAsync</c>, so
    /// this one call is what makes the create path, the legacy accept path and anything written next correct
    /// without a second thought. Move it out to the handlers and the guard above cannot see the hole, because
    /// a handler that numbers a devis does not call <c>AddItems</c>.
    /// </summary>
    [Fact]
    public void Acceptance_Itself_Applies_The_Catalogue_Protocol()
    {
        var root = SolutionSources.Root();
        var acceptance = SolutionSources.CsFiles(root)
            .Where(f => Path.GetFileName(f) == AcceptanceFile)
            .Where(f => !f.Contains("ClinicManagement.UnitTests", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            acceptance.Count == 1,
            $"Expected exactly one production {AcceptanceFile}; found {acceptance.Count}. Acceptance held in two "
            + "copies is how one of them stops applying the protocol.");

        var source = SolutionSources.WithoutComments(File.ReadAllText(acceptance[0]));

        Assert.True(
            AppliesTheProtocol.IsMatch(source),
            $"{AcceptanceFile} no longer calls {Applier}.ApplyAsync. Acceptance is the first instant a devis act "
            + "may hold steps (SetItemSteps refuses a Draft), so it is the only place the protocol can be applied "
            + "for every act at once. Without it a devis is numbered and its « Couronne / bridge » arrives with "
            + "no étape — no error, an empty strip.");
    }

    /// <summary>
    /// The catalogue's protocol has a consumer at all. This is the assertion that would have caught the original
    /// defect: <c>DefaultSteps</c> was written, stored, seeded, edited and mapped to a DTO, and read by nothing
    /// that could act on it.
    /// </summary>
    [Fact]
    public void The_Catalogue_Protocol_Is_Read_By_Something_That_Puts_It_On_An_Act()
    {
        var root = SolutionSources.Root();
        var readsIt = new Regex(@"\.\s*DefaultSteps\b", RegexOptions.Compiled);
        var putsItOnAnAct = new Regex(@"SetItemSteps\s*\(", RegexOptions.Compiled);

        var consumers = new List<string>();

        foreach (var file in SolutionSources.CsFiles(root))
        {
            if (file.Contains("ClinicManagement.UnitTests", StringComparison.Ordinal)
                || file.Contains("Migrations", StringComparison.Ordinal))
            {
                continue;
            }

            var source = SolutionSources.WithoutComments(File.ReadAllText(file));
            if (readsIt.IsMatch(source) && putsItOnAnAct.IsMatch(source))
            {
                consumers.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            consumers.Count > 0,
            "Nothing in production both reads ProcedureType.DefaultSteps and calls SetItemSteps. The catalogue's "
            + "step protocol is then a field with an editor, a column, a seed and a DTO — and no effect on any "
            + "devis. That is exactly the state this feature shipped in once.");
    }
}
