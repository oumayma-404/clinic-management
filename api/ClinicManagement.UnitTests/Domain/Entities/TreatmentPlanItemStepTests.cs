using System;
using System.Linq;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// The step rules of a multi-séance act — an implant, a bridge, a couronne carried out over several visits.
///
/// <para>Two invariants carry the whole feature, and each is a defect if reversed:</para>
///
/// <para><b>1. An act with NO steps behaves exactly as it always did.</b> That is what makes the change additive:
/// every devis written before steps existed still walks <c>Planned → Done</c>, and any read that ignores the new
/// collection is still correct. The parity tests here are not padding — they are the property the migration's
/// safety rests on.</para>
///
/// <para><b>2. Each step holds its OWN fiche link.</b> <c>TreatmentPlanItem.MarkDone</c> refuses a second,
/// different record, so before steps a bridge charted across three séances was refused on the second with
/// « Cet acte est déjà réalisé et rattaché à une autre fiche de soins ». The act now spans as many fiches as it
/// has steps, and that is the only thing that made the client's case representable.</para>
///
/// <para>Everything goes through <see cref="TreatmentPlan"/> rather than the child directly, because the plan owns
/// the promotion chain (act → InProgress → Done, plan → InProgress → Completed) and a test that bypassed it would
/// pin the half of the rule that cannot break on its own.</para>
/// </summary>
public class TreatmentPlanItemStepTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FicheOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FicheTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FicheThree = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Day1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day3 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>An accepted plan holding one act, with the given steps set on it.</summary>
    private static (TreatmentPlan Plan, TreatmentPlanItem Item) PlanWithSteps(params string[] labels)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Bridge");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Bridge 4 dents", 1000m, null, Array.Empty<int>()),
        });
        plan.Accept("2026-0001");

        var item = plan.Items.Single();
        if (labels.Length > 0)
        {
            plan.SetItemSteps(item.Id, labels.Select(l => new TreatmentPlanItemStepInput(null, l, null)));
        }

        return (plan, item);
    }

    // ── parity: an act with no steps is untouched by any of this ──────────────────────────────────────

    /// <summary>
    /// The property the whole change rests on. A line with no steps must reach <c>Done</c> in one call, exactly
    /// as it did before — if this ever fails, every devis already in every cabinet has changed behaviour.
    /// </summary>
    [Fact]
    public void An_Act_With_No_Steps_Still_Goes_Straight_To_Done()
    {
        var (plan, item) = PlanWithSteps();

        plan.MarkItemDone(item.Id, Day1, FicheOne);

        Assert.False(item.HasSteps);
        Assert.Equal(TreatmentPlanItemStatus.Done, item.Status);
        Assert.Equal(Day1, item.DoneDate);
        Assert.Equal(FicheOne, item.LinkedDentalRecordId);
        // Its only act being done closes the plan, as it always has.
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    /// <summary>And un-marking it is still the exact inverse, with no step machinery involved.</summary>
    [Fact]
    public void An_Act_With_No_Steps_Un_Marks_The_Way_It_Always_Did()
    {
        var (plan, item) = PlanWithSteps();
        plan.MarkItemDone(item.Id, Day1, FicheOne);

        plan.UnmarkItemDone(item.Id);

        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
        Assert.Null(item.DoneDate);
        Assert.Null(item.LinkedDentalRecordId);
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    // ── the derived status ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Stepped_Act_Starts_Planned_With_Nothing_Done()
    {
        var (_, item) = PlanWithSteps("Préparation", "Empreinte", "Scellement");

        Assert.True(item.HasSteps);
        Assert.Equal(3, item.StepsTotal);
        Assert.Equal(0, item.StepsDone);
        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
        Assert.Equal("Préparation", item.NextStep!.Label);
    }

    /// <summary>
    /// The state that did not exist before this feature, and the reason the enum grew a third member: two of
    /// three steps done is neither « prévu » nor « réalisé », and the client's dentist had nowhere to say it.
    /// </summary>
    [Fact]
    public void Two_Of_Three_Steps_Done_Reads_InProgress_And_Names_What_Is_Left()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte", "Scellement");
        var steps = item.Steps.ToList();

        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);
        plan.MarkItemStepDone(item.Id, steps[1].Id, Day1, FicheOne);

        Assert.Equal(TreatmentPlanItemStatus.InProgress, item.Status);
        Assert.Equal(2, item.StepsDone);
        Assert.Equal("Scellement", item.NextStep!.Label);
        // An act under way is not a réalisé act: neither the date nor the fiche link may be claimed yet.
        Assert.Null(item.DoneDate);
        Assert.Null(item.LinkedDentalRecordId);
        // And the plan is under way, not finished.
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
    }

    /// <summary>
    /// ⚠️ The act's own <c>LinkedDentalRecordId</c> becomes the LAST step's — « the fiche that finished it » —
    /// which is what keeps every existing reader of that field correct with no change.
    /// </summary>
    [Fact]
    public void The_Last_Step_Completes_The_Act_And_The_Plan_Closes_Itself()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte", "Scellement");
        var steps = item.Steps.ToList();

        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);
        plan.MarkItemStepDone(item.Id, steps[1].Id, Day2, FicheTwo);
        plan.MarkItemStepDone(item.Id, steps[2].Id, Day3, FicheThree);

        Assert.Equal(TreatmentPlanItemStatus.Done, item.Status);
        Assert.Equal(Day3, item.DoneDate);
        Assert.Equal(FicheThree, item.LinkedDentalRecordId);
        Assert.Null(item.NextStep);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    // ── one act, several fiches: the refusal this feature exists to remove ───────────────────────────

    /// <summary>
    /// <b>This is the test for the client's actual problem.</b> Before steps, the second séance of a bridge threw
    /// « Cet acte est déjà réalisé et rattaché à une autre fiche de soins » — a devis line could be evidenced by
    /// exactly one fiche, for ever.
    /// </summary>
    [Fact]
    public void Three_Steps_Of_One_Act_Are_Recorded_By_Three_Different_Fiches()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte", "Scellement");
        var steps = item.Steps.ToList();

        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);
        plan.MarkItemStepDone(item.Id, steps[1].Id, Day2, FicheTwo);
        plan.MarkItemStepDone(item.Id, steps[2].Id, Day3, FicheThree);

        Assert.Equal(
            new Guid?[] { FicheOne, FicheTwo, FicheThree },
            item.Steps.Select(s => s.LinkedDentalRecordId).ToArray());
    }

    /// <summary>Re-saving the same fiche is idempotent — a fiche is legitimately saved twice.</summary>
    [Fact]
    public void Re_Linking_A_Step_To_The_Same_Fiche_Is_A_No_Op()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte");
        var step = item.Steps.First();

        plan.MarkItemStepDone(item.Id, step.Id, Day1, FicheOne);
        plan.MarkItemStepDone(item.Id, step.Id, Day2, FicheOne);

        // The first date stands: nothing about the step changed, so nothing was rewritten.
        Assert.Equal(Day1, step.DoneDate);
    }

    /// <summary>But a DIFFERENT fiche is refused — that would claim the step happened at a visit it did not.</summary>
    [Fact]
    public void Re_Linking_A_Step_To_A_Different_Fiche_Is_Refused()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte");
        var step = item.Steps.First();
        plan.MarkItemStepDone(item.Id, step.Id, Day1, FicheOne);

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.MarkItemStepDone(item.Id, step.Id, Day2, FicheTwo));

        Assert.Contains("déjà réalisée", ex.Message);
        Assert.Contains("Préparation", ex.Message);
    }

    // ── the act-level entry point on a stepped act ───────────────────────────────────────────────────

    /// <summary>
    /// A séance that named no step — an older booking, or the single-act shorthand — must still record
    /// honestly. It advances the next pending step rather than declaring the whole bridge finished, which would
    /// attribute the préparation to the scellement's visit.
    /// </summary>
    [Fact]
    public void MarkItemDone_On_A_Stepped_Act_Advances_Only_The_Next_Step()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte", "Scellement");

        plan.MarkItemDone(item.Id, Day1, FicheOne);

        Assert.Equal(TreatmentPlanItemStatus.InProgress, item.Status);
        Assert.Equal(1, item.StepsDone);
        Assert.Equal("Empreinte", item.NextStep!.Label);
    }

    // ── corrections ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_Marking_A_Step_Reopens_The_Act_And_The_Plan()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte");
        var steps = item.Steps.ToList();
        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);
        plan.MarkItemStepDone(item.Id, steps[1].Id, Day2, FicheTwo);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        Assert.True(plan.UnmarkItemStep(item.Id, steps[1].Id));

        Assert.Equal(TreatmentPlanItemStatus.InProgress, item.Status);
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
        Assert.Null(steps[1].DoneDate);
        // The other step's own evidence is untouched — its fiche was never in question.
        Assert.Equal(FicheOne, steps[0].LinkedDentalRecordId);
    }

    /// <summary>
    /// ⚠️ Un-marking the last remaining done step walks the plan back to <c>Accepted</c>, not to
    /// <c>InProgress</c>. Reading only <c>Done</c> acts when deciding that would leave a plan « en cours » with
    /// no work recorded anywhere on it.
    /// </summary>
    [Fact]
    public void Un_Marking_The_Only_Done_Step_Returns_The_Plan_To_Accepted()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte");
        var step = item.Steps.First();
        plan.MarkItemStepDone(item.Id, step.Id, Day1, FicheOne);

        plan.UnmarkItemStep(item.Id, step.Id);

        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    [Fact]
    public void Un_Marking_A_Step_That_Was_Never_Done_Reports_Nothing_To_Undo()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte");

        Assert.False(plan.UnmarkItemStep(item.Id, item.Steps.First().Id));
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    // ── editing the step list ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Echoing an id back keeps the step's identity, which is what lets its réalisé date, its fiche link and any
    /// séance already booked for it survive a re-wording of the protocol.
    /// </summary>
    [Fact]
    public void Echoing_A_Step_Id_Back_Preserves_Its_Progress()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte", "Scellement");
        var steps = item.Steps.ToList();
        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);

        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(steps[0].Id, "Préparation de la dent", 45),
            new TreatmentPlanItemStepInput(steps[1].Id, "Empreinte", null),
            new TreatmentPlanItemStepInput(steps[2].Id, "Scellement", null),
            new TreatmentPlanItemStepInput(null, "Contrôle", 20),
        });

        var kept = item.Steps.First();
        Assert.Equal(steps[0].Id, kept.Id);
        Assert.Equal("Préparation de la dent", kept.Label);
        Assert.Equal(45, kept.EstimatedDurationMinutes);
        Assert.Equal(Day1, kept.DoneDate);
        Assert.Equal(FicheOne, kept.LinkedDentalRecordId);
        // Ranks stay dense 0..n-1, which verify-schema's plan-step-sequence-dense also holds.
        Assert.Equal(new[] { 0, 1, 2, 3 }, item.Steps.Select(s => s.SequenceNumber));
    }

    [Fact]
    public void A_Step_Already_Carried_Out_Cannot_Be_Dropped()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Empreinte");
        var steps = item.Steps.ToList();
        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);

        var ex = Assert.Throws<InvalidOperationException>(() => plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(steps[1].Id, "Empreinte", null),
        }));

        Assert.Contains("déjà réalisée", ex.Message);
        Assert.Contains("Préparation", ex.Message);
    }

    /// <summary>
    /// Adding a step to a finished act reopens it — and the plan with it. Correct, and worth pinning: the whole
    /// point is that a treatment is not finished while a step remains.
    /// </summary>
    [Fact]
    public void Adding_A_Step_To_A_Finished_Act_Reopens_The_Plan()
    {
        var (plan, item) = PlanWithSteps("Préparation", "Scellement");
        var steps = item.Steps.ToList();
        plan.MarkItemStepDone(item.Id, steps[0].Id, Day1, FicheOne);
        plan.MarkItemStepDone(item.Id, steps[1].Id, Day2, FicheTwo);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(steps[0].Id, "Préparation", null),
            new TreatmentPlanItemStepInput(steps[1].Id, "Scellement", null),
            new TreatmentPlanItemStepInput(null, "Contrôle à 6 mois", 20),
        });

        Assert.Equal(TreatmentPlanItemStatus.InProgress, item.Status);
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
    }

    /// <summary>
    /// A step-less act that is already réalisé may not be cut into steps: the recompute would have nothing to
    /// derive « réalisé » from and would silently drop the fiche link that evidenced it.
    /// </summary>
    [Fact]
    public void A_Finished_Step_Less_Act_Cannot_Be_Cut_Into_Steps()
    {
        var (plan, item) = PlanWithSteps();
        plan.MarkItemDone(item.Id, Day1, FicheOne);

        var ex = Assert.Throws<InvalidOperationException>(() => plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", null),
        }));

        Assert.Contains("découpé en étapes", ex.Message);
        Assert.Equal(FicheOne, item.LinkedDentalRecordId);
    }

    [Fact]
    public void Steps_Are_Capped_And_Labelled()
    {
        var (plan, item) = PlanWithSteps();

        Assert.Throws<InvalidOperationException>(() => plan.SetItemSteps(
            item.Id,
            Enumerable.Range(0, TreatmentPlanItemStep.MaxStepsPerItem + 1)
                .Select(i => new TreatmentPlanItemStepInput(null, $"Étape {i}", null))));

        Assert.Throws<ArgumentException>(() => plan.SetItemSteps(
            item.Id, new[] { new TreatmentPlanItemStepInput(null, "   ", null) }));
    }

    /// <summary>A cancelled devis is void — its protocol is not something to keep editing.</summary>
    [Fact]
    public void A_Cancelled_Plan_Refuses_Step_Edits()
    {
        var (plan, item) = PlanWithSteps("Préparation");
        plan.Cancel("Patient parti à l'étranger");

        var ex = Assert.Throws<InvalidOperationException>(() => plan.SetItemSteps(
            item.Id, new[] { new TreatmentPlanItemStepInput(null, "Empreinte", null) }));

        Assert.Contains("annulé", ex.Message);
    }
}
