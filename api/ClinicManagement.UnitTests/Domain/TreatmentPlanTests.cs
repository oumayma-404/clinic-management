using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-10][AC-11] The two lifecycle rules that had to ship together: a fully-treated plan can still be paid
/// (« Terminé » means every act was carried out, not that the patient has paid), and the "all acts done ⇒
/// Complete" rule lives in <c>MarkItemDone</c> so both completion paths behave identically.
/// <para>
/// These two masked each other before the fix: auto-complete had no frontend caller, so nobody hit the
/// <c>EnsureActive</c> guard that made a Completed plan unpayable. Fixing either alone breaks money
/// collection, which is exactly why they need pinning.
/// </para>
/// <para>
/// AC-18 – AC-23 (<c>AddItems</c>, <c>RemoveItem</c>, <c>ReviseInstallments</c>, <c>SetItemOrder</c>,
/// <c>RevisionNumber</c>, the guarded <c>MarkDone</c> and id-preserving <c>SetItems</c>) land with slice B —
/// those members do not exist yet and their facts belong in the same pass that adds them.
/// </para>
/// </summary>
public class TreatmentPlanTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecordId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime DoneOn = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);

    private static TreatmentPlan DraftPlan(int actCount = 1)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(Enumerable.Range(0, actCount).Select(i =>
            ($"Acte {i + 1}", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 + i })));
        return plan;
    }

    private static TreatmentPlan AcceptedPlan(int actCount = 1)
    {
        var plan = DraftPlan(actCount);
        plan.Accept("2026-0001");
        return plan;
    }

    private static TreatmentPlan CompletedPlan()
    {
        var plan = AcceptedPlan();
        plan.MarkItemDone(plan.Items.First().Id, DoneOn, RecordId);
        return plan;
    }

    // [AC-11] Marking the last act done closes the plan by itself — the rule now lives in the aggregate, so
    // the record-driven path (DentalRecordLinker) gets it for free, not just the command.
    [Fact]
    public void MarkItemDone_On_The_Last_Act_Auto_Completes_The_Plan()
    {
        var plan = AcceptedPlan();

        plan.MarkItemDone(plan.Items.First().Id, DoneOn, RecordId);

        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    // [AC-11] With acts still open the plan only moves Accepted → InProgress; it must not close early.
    [Fact]
    public void MarkItemDone_With_Acts_Remaining_Moves_To_InProgress()
    {
        var plan = AcceptedPlan(actCount: 3);

        plan.MarkItemDone(plan.Items.First().Id, DoneOn, RecordId);

        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
        Assert.Equal(1, plan.Items.Count(i => i.Status == TreatmentPlanItemStatus.Done));
    }

    // [AC-11] The last of several acts closes it.
    [Fact]
    public void MarkItemDone_Closes_The_Plan_Once_Every_Act_Is_Done()
    {
        var plan = AcceptedPlan(actCount: 3);

        foreach (var item in plan.Items.ToList())
        {
            plan.MarkItemDone(item.Id, DoneOn, RecordId);
        }

        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    // [AC-10] THE money fix: a fully-treated plan can still collect its remaining balance. Treatment
    // routinely finishes before the last échéance is paid, so closing the clinical track must not close the
    // financial one.
    [Fact]
    public void A_Completed_Plan_Can_Still_Receive_An_Installment_Payment()
    {
        var plan = CompletedPlan();
        var installmentId = plan.Installments.First().Id;

        plan.RecordInstallmentPayment(installmentId, 200m, PaymentMethod.Cash, DoneOn);

        Assert.Equal(200m, plan.AmountPaid);
        Assert.Equal(300m, plan.Outstanding);
    }

    // [AC-10] Paying a Completed plan never re-opens it — the Accepted → InProgress bump only fires from
    // Accepted, so « Terminé » is stable.
    [Fact]
    public void Paying_A_Completed_Plan_Leaves_It_Completed()
    {
        var plan = CompletedPlan();

        plan.RecordInstallmentPayment(plan.Installments.First().Id, 500m, PaymentMethod.Cash, DoneOn);

        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
        Assert.Equal(0m, plan.Outstanding);
    }

    // [AC-10] A Draft devis is an unaccepted quote — no money may be recorded against it.
    [Fact]
    public void A_Draft_Plan_Rejects_An_Installment_Payment()
    {
        var plan = DraftPlan();

        Assert.Throws<InvalidOperationException>(() =>
            plan.RecordInstallmentPayment(Guid.NewGuid(), 100m, PaymentMethod.Cash, DoneOn));
    }

    // [AC-10] A cancelled plan is void — likewise no payment.
    [Fact]
    public void A_Cancelled_Plan_Rejects_An_Installment_Payment()
    {
        var plan = AcceptedPlan();
        var installmentId = plan.Installments.First().Id;
        plan.Cancel("Patient parti");

        Assert.Throws<InvalidOperationException>(() =>
            plan.RecordInstallmentPayment(installmentId, 100m, PaymentMethod.Cash, DoneOn));
    }

    // [AC-10] Payability is deliberately wider than act-completion: a Completed plan takes money but refuses
    // further clinical changes, so the two guards must not be collapsed into one.
    [Fact]
    public void A_Completed_Plan_Still_Refuses_To_Mark_Another_Act_Done()
    {
        var plan = AcceptedPlan(actCount: 2);
        var acts = plan.Items.ToList();
        plan.MarkItemDone(acts[0].Id, DoneOn, RecordId);
        plan.MarkItemDone(acts[1].Id, DoneOn, RecordId);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);

        Assert.Throws<InvalidOperationException>(() => plan.MarkItemDone(acts[0].Id, DoneOn, RecordId));
    }

    // Accepting a devis with no échéancier seeds one lump-sum installment, otherwise Outstanding would be
    // stuck at the total with no payable row — the precondition every AC-10 case above relies on.
    [Fact]
    public void Accept_Seeds_A_Lump_Sum_Installment_When_No_Schedule_Was_Given()
    {
        var plan = AcceptedPlan();

        Assert.Single(plan.Installments);
        Assert.Equal(plan.TotalPlanned, plan.Installments.First().Amount);
    }

    // [AC-22b] The invariant the money reads depend on: Σ installment.Amount == TotalPlanned, which is what
    // makes plan.Outstanding (« Solde patient ») and Σ(amount − paid) (« Créances ») agree. Slice B makes the
    // total mutable, at which point this must be re-enforced after every amendment.
    [Fact]
    public void Installment_Schedule_Sums_To_The_Planned_Total()
    {
        var plan = AcceptedPlan(actCount: 2);

        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
        Assert.Equal(plan.Outstanding, plan.Installments.Sum(i => i.Amount - i.AmountPaid));
    }

    // A schedule that doesn't sum to the total is rejected outright — the same invariant, at entry.
    [Fact]
    public void SetInstallments_Rejects_A_Schedule_That_Does_Not_Match_The_Total()
    {
        var plan = DraftPlan();

        Assert.Throws<InvalidOperationException>(() => plan.SetInstallments(new[]
        {
            (new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 100m),
        }));
    }
}
