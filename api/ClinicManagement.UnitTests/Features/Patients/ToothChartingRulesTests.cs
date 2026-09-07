using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « When may an act's finished state be written onto the odontogram? »
///
/// <para>
/// The defect these pin was silent and clinical: a multi-séance act's <b>first</b> fiche charted the act's end
/// state, so three teeth read « Implant » weeks before the implant existed and two read « Extrait/absent » while
/// the extraction was half done. Measured on the live database as seven rows, every one written from a step 1
/// of 2, with no error anywhere.
/// </para>
/// </summary>
public class ToothChartingRulesTests
{
    private static readonly Guid ImplantProcedure = Guid.NewGuid();

    private static DentalRecordActInput Act(
        Guid? procedureTypeId, ToothCondition? condition, params int[] teeth) =>
        new(procedureTypeId, "Implant dentaire", 1500m, null, false, teeth.ToList(), condition, null, null);

    private static TreatmentPlanItem StepItem(Guid? procedureTypeId, int steps, int done)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Implant");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Implant dentaire", 1500m, procedureTypeId, new List<int>()),
        });
        var item = plan.Items.First();
        plan.SetItemSteps(item.Id, Enumerable.Range(0, steps)
            .Select(i => new TreatmentPlanItemStepInput(null, $"Étape {i + 1}", null)));
        for (var i = 0; i < done; i++)
        {
            var step = item.Steps.OrderBy(s => s.SequenceNumber).First(s => s.DoneDate == null);
            plan.MarkItemStepDone(item.Id, step.Id, DateTime.UtcNow, Guid.NewGuid());
        }

        return item;
    }

    /// <summary>The defect itself: step 1 of 2 must chart nothing.</summary>
    [Fact]
    public void An_Unfinished_Treatment_Charts_Nothing_For_Its_Own_Act()
    {
        var acts = new List<DentalRecordActInput> { Act(ImplantProcedure, ToothCondition.Implant, 36, 46) };
        var item = StepItem(ImplantProcedure, steps: 2, done: 1);

        var chartable = ToothChartingRules.ChartableActs(acts, item, itemIsComplete: false);

        Assert.Null(chartable[0].ResultingCondition);
        // The teeth, the fee and the name are untouched — only the odontogram claim is withheld.
        Assert.Equal(new[] { 36, 46 }, chartable[0].ToothNumbers);
        Assert.Equal(1500m, chartable[0].Cost);
    }

    /// <summary>And the last séance charts it, or the state would never be recorded at all.</summary>
    [Fact]
    public void The_Finishing_Seance_Charts_The_End_State()
    {
        var acts = new List<DentalRecordActInput> { Act(ImplantProcedure, ToothCondition.Implant, 36) };
        var item = StepItem(ImplantProcedure, steps: 2, done: 2);

        var chartable = ToothChartingRules.ChartableActs(acts, item, itemIsComplete: true);

        Assert.Equal(ToothCondition.Implant, chartable[0].ResultingCondition);
        Assert.Same(acts, chartable);
    }

    /// <summary>An ordinary fiche — no devis behind it — is completely unaffected.</summary>
    [Fact]
    public void A_Fiche_With_No_Plan_Is_Untouched()
    {
        var acts = new List<DentalRecordActInput> { Act(null, ToothCondition.Obturation, 26) };

        var chartable = ToothChartingRules.ChartableActs(acts, item: null, itemIsComplete: true);

        Assert.Same(acts, chartable);
        Assert.Equal(ToothCondition.Obturation, chartable[0].ResultingCondition);
    }

    /// <summary>
    /// ⚠️ The mixed séance, and the reason `PlanCarriedAct` had to be extracted: an implant's step-1 fiche plus a
    /// filling done the same day. Withholding the wrong one would either chart an implant that does not exist or
    /// lose a real obturation.
    /// </summary>
    [Fact]
    public void A_Filling_Done_The_Same_Day_Still_Charts()
    {
        var filling = Guid.NewGuid();
        var acts = new List<DentalRecordActInput>
        {
            Act(ImplantProcedure, ToothCondition.Implant, 36),
            Act(filling, ToothCondition.Obturation, 26) with { ProcedureName = "Soin de carie" },
        };
        var item = StepItem(ImplantProcedure, steps: 2, done: 1);

        var chartable = ToothChartingRules.ChartableActs(acts, item, itemIsComplete: false);

        Assert.Null(chartable[0].ResultingCondition);
        Assert.Equal(ToothCondition.Obturation, chartable[1].ResultingCondition);
    }

    /// <summary>
    /// A devis line naming no catalogue act (hand-typed) is resolvable only on a single-act fiche. With two acts
    /// the rule refuses to guess — a wrong guess here writes clinical evidence.
    /// </summary>
    [Fact]
    public void An_Unresolvable_Line_On_A_Multi_Act_Fiche_Withholds_Nothing()
    {
        var acts = new List<DentalRecordActInput>
        {
            Act(Guid.NewGuid(), ToothCondition.Implant, 36),
            Act(Guid.NewGuid(), ToothCondition.Obturation, 26),
        };
        var item = StepItem(procedureTypeId: null, steps: 2, done: 1);

        var chartable = ToothChartingRules.ChartableActs(acts, item, itemIsComplete: false);

        Assert.Same(acts, chartable);
    }

    /// <summary>
    /// An act with no steps finishes on its first fiche, so nothing is withheld — which is what keeps every
    /// single-séance devis act behaving exactly as it did.
    /// </summary>
    [Fact]
    public void A_Single_Seance_Act_Charts_Immediately()
    {
        var acts = new List<DentalRecordActInput> { Act(ImplantProcedure, ToothCondition.Couronne, 16) };
        var item = StepItem(ImplantProcedure, steps: 0, done: 0);

        var chartable = ToothChartingRules.ChartableActs(acts, item, itemIsComplete: true);

        Assert.Equal(ToothCondition.Couronne, chartable[0].ResultingCondition);
    }

    /// <summary>
    /// `PlanCarriedAct` is the shared answer to « which act does the devis carry », and both the fee rule and
    /// this one must ask it. A copy in either place prices the implant and charts the filling.
    /// </summary>
    [Fact]
    public void The_Carried_Act_Is_Matched_On_Its_Procedure()
    {
        var filling = Guid.NewGuid();
        var acts = new List<DentalRecordActInput>
        {
            Act(filling, ToothCondition.Obturation, 26),
            Act(ImplantProcedure, ToothCondition.Implant, 36),
        };
        var item = StepItem(ImplantProcedure, steps: 2, done: 1);

        Assert.Equal(1, PlanCarriedAct.IndexIn(acts, item));
    }
}
