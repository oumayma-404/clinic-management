using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-P2.8][AC-P2.9] Un-marking an act as réalisé — the operation <c>MarkDone</c> has always told the user to
/// perform (« détachez-le de cette fiche ») and that existed nowhere in the domain, application, API or UI.
/// <para>
/// The load-bearing case is the <b>Completed</b> plan. Marking the last act done auto-completes the devis, so a
/// correction gated on an *active* plan could never reach the mistake it exists for: one act ticked against the
/// wrong fiche closed the whole plan and <c>EnsureAmendable</c> then refused every amendment. That is why
/// <c>UnmarkItemDone</c> uses <c>EnsureCorrectable</c> (Accepted/InProgress/**Completed**) and not
/// <c>EnsureActive</c>.
/// </para>
/// <para>
/// Status transitions are asserted as the exact inverse of <c>MarkItemDone</c>'s promotions, since a reopen that
/// disagreed with the forward path would leave the plan in a state the forward path can never produce.
/// </para>
/// </summary>
public class TreatmentPlanItemUnmarkTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecordId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime DoneOn = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);

    private static TreatmentPlan AcceptedPlan(int actCount = 1)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(Enumerable.Range(0, actCount).Select(i =>
            ($"Acte {i + 1}", 500m, (IReadOnlyList<int>)new[] { 11 + i })));
        plan.Accept("2026-0001");
        return plan;
    }

    // [AC-P2.8] The act returns to « prévu » and its evidence link is cleared.
    [Fact]
    public void Unmark_Returns_The_Act_To_Planned_And_Clears_Its_Record_Link()
    {
        var plan = AcceptedPlan(2);
        var item = plan.Items.First();
        plan.MarkItemDone(item.Id, DoneOn, RecordId);
        Assert.Equal(TreatmentPlanItemStatus.Done, item.Status);

        plan.UnmarkItemDone(item.Id);

        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
        Assert.Null(item.DoneDate);
        Assert.Null(item.LinkedDentalRecordId);
    }

    // [AC-P2.9] The whole point: a plan auto-completed by its last act can be reopened.
    [Fact]
    public void Unmark_Reopens_A_Plan_That_Its_Last_Act_Auto_Completed()
    {
        var plan = AcceptedPlan(1);
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, DoneOn, RecordId);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        plan.UnmarkItemDone(item.Id);

        // No act done at all ⇒ back where acceptance left it, the exact inverse of MarkItemDone's promotion.
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    // [AC-P2.9] With other acts still done the plan is InProgress, not Accepted.
    [Fact]
    public void Unmark_Leaves_The_Plan_InProgress_When_Another_Act_Is_Still_Done()
    {
        var plan = AcceptedPlan(2);
        var first = plan.Items.First();
        var second = plan.Items.Last();
        plan.MarkItemDone(first.Id, DoneOn, RecordId);
        plan.MarkItemDone(second.Id, DoneOn, null);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        plan.UnmarkItemDone(second.Id);

        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
    }

    /*
     * [AC-P2.9] Reopening puts the plan back to InProgress.
     *
     * ⚠️ This used to assert that un-marking RESTORED amendability, because `EnsureAmendable` refused a
     * Completed plan. It no longer does — a completed plan is correctable, which is the whole point of the
     * widened window — so what is left to pin is the status transition itself, and that the amendment stamp
     * works either side of it. The old assertion would now be vacuous rather than wrong.
     */
    [Fact]
    public void Unmark_Reopens_A_Completed_Plan_And_Amendment_Works_Either_Side()
    {
        var plan = AcceptedPlan(1);
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, DoneOn, RecordId);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
        plan.RecordAmendment(); // a completed plan is correctable now

        plan.UnmarkItemDone(item.Id);

        // ⚠️ `Accepted`, not `InProgress`: this plan has ONE act, so un-marking it leaves no work recorded at
        // all and the status re-derives to « accepté, rien de commencé ». A two-act plan with one still done
        // would land on InProgress — see `Unmark_Is_A_No_Op_On_An_Act_That_Was_Never_Done`.
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
        plan.RecordAmendment();
        Assert.Equal(2, plan.RevisionNumber);
    }

    // [AC-P2.8] Un-marking an act that was never done changes nothing — and must not reopen a plan.
    [Fact]
    public void Unmark_Is_A_No_Op_On_An_Act_That_Was_Never_Done()
    {
        var plan = AcceptedPlan(2);
        var done = plan.Items.First();
        var untouched = plan.Items.Last();
        plan.MarkItemDone(done.Id, DoneOn, RecordId);
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);

        plan.UnmarkItemDone(untouched.Id);

        Assert.Equal(TreatmentPlanItemStatus.Planned, untouched.Status);
        Assert.Equal(TreatmentPlanItemStatus.Done, done.Status);
        // Crucially the plan is untouched — a no-op must not demote a legitimately InProgress plan.
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
    }

    // [AC-P2.8] An unknown act id is refused rather than silently ignored.
    [Fact]
    public void Unmark_Refuses_An_Unknown_Act()
    {
        var plan = AcceptedPlan(1);

        var ex = Assert.Throws<InvalidOperationException>(() => plan.UnmarkItemDone(Guid.NewGuid()));
        Assert.Contains("introuvable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // [AC-P2.8] A cancelled devis is void — there is nothing to correct on it.
    [Fact]
    public void Unmark_Refuses_A_Cancelled_Devis()
    {
        var plan = AcceptedPlan(1);
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, DoneOn, RecordId);
        plan.Cancel("Patient injoignable");

        var ex = Assert.Throws<InvalidOperationException>(() => plan.UnmarkItemDone(item.Id));
        Assert.Contains("corrigé", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // [AC-P2.8] After detaching, the act can be re-linked to a different fiche — the case MarkDone refuses
    // outright while the act is still Done. This is the round trip the error message promises.
    [Fact]
    public void Unmark_Then_MarkDone_Can_Re_Link_The_Act_To_A_Different_Fiche()
    {
        var otherRecord = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var plan = AcceptedPlan(1);
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, DoneOn, RecordId);

        // Before the un-mark this is exactly what MarkDone refuses.
        Assert.Throws<InvalidOperationException>(() => plan.MarkItemDone(item.Id, DoneOn, otherRecord));

        plan.UnmarkItemDone(item.Id);
        plan.MarkItemDone(item.Id, DoneOn, otherRecord);

        Assert.Equal(TreatmentPlanItemStatus.Done, item.Status);
        Assert.Equal(otherRecord, item.LinkedDentalRecordId);
    }
}
