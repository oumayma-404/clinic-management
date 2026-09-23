using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « Cette fiche est liée à un acte du devis qu'elle ne contient pas. »
///
/// <para>
/// <b>The reported defect.</b> « Changer d'acte » on a devis-carried card rewrites the act and leaves
/// <c>TreatmentPlanItemId</c> pointing at the line the fiche was opened on, so the save arrived claiming to
/// carry out a couronne while recording an extraction. Nothing refused it: <see cref="PlanCarriedAct.IndexIn"/>
/// answered <c>-1</c>, so <c>PlanCarriedActPricing</c> imposed no 0 and <c>ToothChartingRules</c> withheld
/// nothing, while <c>DentalRecordLinker</c> went on to mark the <b>couronne's</b> step done against a fiche
/// that does not record it. The devis then read « fait » for work nobody had carried out, and the extraction —
/// still wearing the browser's locked 0 — was billed nothing at all.
/// </para>
///
/// <para>
/// The narrowing is what these mostly pin: <c>-1</c> has two innocent causes and both must stay innocent, or
/// every fiche recorded before the plan prefill carried a <c>ProcedureTypeId</c> becomes impossible to re-save.
/// </para>
/// </summary>
public class PlanCarriedActMismatchTests
{
    private static readonly Guid Couronne = Guid.NewGuid();
    private static readonly Guid Extraction = Guid.NewGuid();

    private static DentalRecordActInput Act(Guid? procedureTypeId, string name) =>
        new(procedureTypeId, name, 120m, null, false, new List<int> { 16 }, (ToothCondition?)null, null, null);

    private static TreatmentPlanItem Item(Guid? procedureTypeId)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Couronne");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Couronne / bridge (par élément)", 400m, procedureTypeId, new List<int>()),
        });
        return plan.Items.First();
    }

    /// <summary>The defect itself.</summary>
    [Fact]
    public void An_Act_Swapped_For_Another_Catalogue_Act_Is_Refused()
    {
        var acts = new List<DentalRecordActInput> { Act(Extraction, "Extraction simple") };

        Assert.True(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(acts, Item(Couronne)));
    }

    /// <summary>The ordinary case, and the one that must never start refusing.</summary>
    [Fact]
    public void The_Devis_Act_Being_Present_Is_Not_A_Mismatch()
    {
        var acts = new List<DentalRecordActInput> { Act(Couronne, "Couronne / bridge (par élément)") };

        Assert.False(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(acts, Item(Couronne)));
    }

    /// <summary>
    /// A mixed séance — the devis act plus a filling done the same day — holds the act, so it is not a
    /// mismatch however many other acts are on the fiche.
    /// </summary>
    [Fact]
    public void A_Mixed_Seance_Holding_The_Devis_Act_Is_Not_A_Mismatch()
    {
        var acts = new List<DentalRecordActInput>
        {
            Act(Extraction, "Extraction simple"),
            Act(Couronne, "Couronne / bridge (par élément)"),
        };

        Assert.False(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(acts, Item(Couronne)));
    }

    /// <summary>
    /// A hand-typed fiche act is <b>unidentifiable</b>, not wrong — and it is the state every fiche opened on a
    /// devis step was recorded in before the prefill carried the catalogue id. Refusing it would make an old
    /// plan fiche impossible to reopen and fix.
    /// </summary>
    [Fact]
    public void A_Free_Text_Act_Is_Unidentifiable_Rather_Than_Wrong()
    {
        var acts = new List<DentalRecordActInput> { Act(null, "Bridge — scellement") };

        Assert.False(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(acts, Item(Couronne)));
    }

    /// <summary>One free-text act among catalogue ones is enough to make the fiche unidentifiable.</summary>
    [Fact]
    public void One_Free_Text_Act_Exempts_The_Whole_Fiche()
    {
        var acts = new List<DentalRecordActInput>
        {
            Act(Extraction, "Extraction simple"),
            Act(null, "Reprise de couronne (hors catalogue)"),
        };

        Assert.False(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(acts, Item(Couronne)));
    }

    /// <summary>A hand-typed devis line names no act, so no act of the fiche can contradict it.</summary>
    [Fact]
    public void A_Devis_Line_Naming_No_Catalogue_Act_Cannot_Be_Contradicted()
    {
        var acts = new List<DentalRecordActInput> { Act(Extraction, "Extraction simple") };

        Assert.False(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(acts, Item(null)));
    }

    /// <summary>
    /// An empty list is « nothing charted yet », which the fiche's own « ajoutez au moins un acte » refusal
    /// owns. Answering true here would replace that message with one about the devis.
    /// </summary>
    [Fact]
    public void An_Empty_Fiche_Is_Not_A_Mismatch()
    {
        Assert.False(PlanCarriedAct.NamesAnActTheFicheDoesNotHold(new List<DentalRecordActInput>(), Item(Couronne)));
    }

    /// <summary>
    /// Both fiche commands must READ the refusal, not just the acts — the whole defect was a correct rule
    /// nobody consulted. Derived from the call sites rather than from a list, so a third command is covered
    /// the day it is written.
    /// </summary>
    [Fact]
    public void Every_Caller_Of_ImposeAsync_Reads_Its_Refusal()
    {
        var callers = Directory
            .EnumerateFiles(SolutionRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.EndsWith("PlanCarriedActPricing.cs", StringComparison.Ordinal))
            .Select(f => (Path: f, Text: File.ReadAllText(f)))
            .Where(f => f.Text.Contains("PlanCarriedActPricing.ImposeAsync", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(callers);

        // Comments are masked: the prose explaining the rule would otherwise satisfy a scan of raw source.
        var deaf = callers
            .Where(f => !Regex.Replace(f.Text, @"//.*?$|/\*.*?\*/", string.Empty,
                            RegexOptions.Multiline | RegexOptions.Singleline)
                        .Contains(".Refusal", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.Path))
            .ToList();

        Assert.True(
            deaf.Count == 0,
            $"These callers of PlanCarriedActPricing.ImposeAsync ignore its refusal: {string.Join(", ", deaf)}. "
            + "A fiche linked to a devis act it does not hold makes the devis mark the wrong act done.");
    }

    /// <summary>
    /// ⚠️ <c>[CallerFilePath]</c>, never <c>AppContext.BaseDirectory</c> — this repo builds its tests to a temp
    /// <c>BaseOutputPath</c> (Smart App Control refuses freshly-built in-repo assemblies), so walking up from the
    /// binary lands in <c>AppData\Local\Temp</c> and finds no source at all. It <b>throws</b> rather than
    /// skipping: a derived guard that scans nothing passes, which is indistinguishable from finding no offenders.
    /// </summary>
    private static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicManagement.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException(
                   $"ClinicManagement.sln not found above '{thisFile}'.");
    }
}
