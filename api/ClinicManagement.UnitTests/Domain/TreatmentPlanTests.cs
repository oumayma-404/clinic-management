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
/// Slice B's amendment surface (AC-18 – AC-23) is covered further down: ordering, id-preserving
/// <c>SetItems</c>, the guarded <c>MarkDone</c>, and the domain half of amend/remove/revise. The handler-level
/// rules — the billed-plan block and the live-appointment block — live in
/// <c>Features/TreatmentPlans/AmendTreatmentPlanCommandHandlerTests</c>, because the aggregate holds neither
/// an invoice nor an appointment reference.
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

    // ---- Slice B: sequencing and amendment ---------------------------------------------------------

    // [AC-19] THE latent-defect fix: an echoed-back id survives a draft edit, so an appointment or dental
    // record linked to that act still resolves afterwards. Before this, editing a draft re-issued every id
    // and orphaned those links — and neither has an FK, so nothing at the DB level would have caught it.
    [Fact]
    public void SetItems_Preserves_The_Id_Of_An_Echoed_Back_Line()
    {
        var plan = DraftPlan(actCount: 2);
        var keptId = plan.Items.First().Id;

        plan.SetItems(new[]
        {
            ((Guid?)keptId, "Acte 1 renommé", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }),
            ((Guid?)null, "Nouvel acte", 300m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 13 }),
        });

        Assert.Equal(keptId, plan.Items.First().Id);
        Assert.Equal("Acte 1 renommé", plan.Items.First().DesignationFr);
        Assert.Equal(2, plan.Items.Count);
    }

    // [AC-19] An id the plan doesn't know is a new line, never an error — a stale client must not be able to
    // fail the save.
    [Fact]
    public void SetItems_Treats_An_Unknown_Id_As_A_New_Line()
    {
        var plan = DraftPlan();
        var strangerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        plan.SetItems(new[]
        {
            ((Guid?)strangerId, "Acte", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }),
        });

        Assert.Single(plan.Items);
        Assert.NotEqual(strangerId, plan.Items.First().Id);
    }

    // [AC-18] SetItems assigns positions by list order, so a freshly edited draft reads in the order typed.
    [Fact]
    public void SetItems_Numbers_The_Acts_By_Position()
    {
        var plan = DraftPlan(actCount: 3);

        Assert.Equal(new[] { 0, 1, 2 }, plan.Items.Select(i => i.SequenceNumber));
    }

    // Changing the acts changes the total, so the échéancier must be resent — previously the schedule was
    // wiped silently and nothing but the form's habit of resending it kept the money consistent.
    [Fact]
    public void SetItems_Refuses_To_Silently_Discard_An_Existing_Schedule()
    {
        var plan = DraftPlan();
        plan.SetInstallments(new[] { (new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 500m) });

        Assert.Throws<InvalidOperationException>(() => plan.SetItems(
            new[] { ((Guid?)null, "Acte", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) },
            scheduleWillBeResent: false));
    }

    // [AC-18] Reordering assigns positions by the given order and survives as data.
    [Fact]
    public void SetItemOrder_Assigns_Positions_By_Index()
    {
        var plan = AcceptedPlan(actCount: 3);
        var reversed = plan.Items.Select(i => i.Id).Reverse().ToList();

        plan.SetItemOrder(reversed);

        Assert.Equal(reversed, plan.Items.Select(i => i.Id));
        Assert.Equal(new[] { 0, 1, 2 }, plan.Items.Select(i => i.SequenceNumber));
    }

    // [AC-18] A partial list would leave the omitted acts at stale positions and silently interleave them.
    [Fact]
    public void SetItemOrder_Rejects_A_List_That_Is_Not_Exactly_The_Plans_Acts()
    {
        var plan = AcceptedPlan(actCount: 3);
        var partial = plan.Items.Take(2).Select(i => i.Id).ToList();

        Assert.Throws<InvalidOperationException>(() => plan.SetItemOrder(partial));
    }

    // [AC-18] Ties fall back to insertion order (stable sort), so a pre-migration plan — every act at 0 —
    // does not reshuffle on screen before its first reorder.
    [Fact]
    public void Acts_Sharing_A_Sequence_Number_Keep_Their_Insertion_Order()
    {
        var plan = AcceptedPlan(actCount: 3);
        foreach (var item in plan.Items) item.SetSequenceNumber(0);

        Assert.Equal(new[] { "Acte 1", "Acte 2", "Acte 3" }, plan.Items.Select(i => i.DesignationFr));
    }

    // [AC-23] Re-saving the same fiche must stay idempotent…
    [Fact]
    public void MarkDone_Is_Idempotent_For_The_Same_Dental_Record()
    {
        var plan = AcceptedPlan(actCount: 2);
        var itemId = plan.Items.First().Id;
        plan.MarkItemDone(itemId, DoneOn, RecordId);
        var firstDoneDate = plan.Items.First().DoneDate;

        plan.MarkItemDone(itemId, DoneOn.AddDays(3), RecordId);

        Assert.Equal(firstDoneDate, plan.Items.First().DoneDate);
        Assert.Equal(RecordId, plan.Items.First().LinkedDentalRecordId);
    }

    // [AC-23] …but a *different* record must not silently overwrite when the act happened, which would
    // rewrite clinical history.
    [Fact]
    public void MarkDone_Rejects_A_Different_Dental_Record()
    {
        var plan = AcceptedPlan(actCount: 2);
        var itemId = plan.Items.First().Id;
        plan.MarkItemDone(itemId, DoneOn, RecordId);
        var otherRecordId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        Assert.Throws<InvalidOperationException>(() => plan.MarkItemDone(itemId, DoneOn, otherRecordId));
        Assert.Equal(RecordId, plan.Items.First().LinkedDentalRecordId);
    }

    // [AC-20] AddItems raises the total. It deliberately does NOT stamp the revision — see
    // One_Amendment_Composed_Of_Several_Changes_Is_One_Revision below.
    [Fact]
    public void AddItems_Raises_The_Total()
    {
        var plan = AcceptedPlan();

        plan.AddItems(new[] { ("Implant", 800m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 21 }) });

        Assert.Equal(1300m, plan.TotalPlanned);
    }

    // [AC-22c] The revision counts *amendments*, not mutations. A single edit routinely adds an act AND
    // re-spreads the échéancier; if each mutator stamped its own revision, two amendments would read
    // « révision 4 » and the number on a patient's printout could never be matched against anything.
    [Fact]
    public void One_Amendment_Composed_Of_Several_Changes_Is_One_Revision()
    {
        var plan = AcceptedPlan();

        plan.AddItems(new[] { ("Implant", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 21 }) });
        plan.ReviseInstallments(new[] { ((Guid?)null, DoneOn, 1000m) });
        plan.RecordAmendment();

        Assert.Equal(1, plan.RevisionNumber);
    }

    // [AC-22c] A closed plan cannot be stamped either — the guard is on the amendment, not just its parts.
    [Fact]
    public void RecordAmendment_Is_Rejected_On_A_Closed_Plan()
    {
        var plan = CompletedPlan();

        Assert.Throws<InvalidOperationException>(() => plan.RecordAmendment());
    }

    // [AC-21] The domain half of the removal guards: a Done act is refused outright, and a booked one is
    // refused when the caller supplies the booking (the aggregate cannot query appointments itself).
    [Fact]
    public void RemoveItem_Refuses_A_Done_Act()
    {
        var plan = AcceptedPlan(actCount: 2);
        var itemId = plan.Items.First().Id;
        plan.MarkItemDone(itemId, DoneOn, RecordId);

        Assert.Throws<InvalidOperationException>(() => plan.RemoveItem(itemId));
    }

    [Fact]
    public void RemoveItem_Refuses_An_Act_With_A_Live_Appointment()
    {
        var plan = AcceptedPlan(actCount: 2);
        var itemId = plan.Items.First().Id;

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.RemoveItem(itemId, new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("12/08", ex.Message);
    }

    // [AC-22c] A cosmetic reorder is not an amendment — it must not bump the revision.
    [Fact]
    public void Reordering_Does_Not_Bump_The_Revision()
    {
        var plan = AcceptedPlan(actCount: 2);

        plan.SetItemOrder(plan.Items.Select(i => i.Id).Reverse().ToList());

        Assert.Equal(0, plan.RevisionNumber);
    }

    // [AC-22] ReviseInstallments enforces the sum invariant…
    [Fact]
    public void ReviseInstallments_Rejects_A_Schedule_That_Does_Not_Match_The_Total()
    {
        var plan = AcceptedPlan();

        Assert.Throws<InvalidOperationException>(() => plan.ReviseInstallments(new[]
        {
            ((Guid?)null, DoneOn, 100m),
        }));
    }

    // [AC-22b] …and a valid revision keeps it, including when a paid row is carried over by id.
    [Fact]
    public void ReviseInstallments_Keeps_A_Paid_Row_And_The_Sum_Invariant()
    {
        var plan = AcceptedPlan();
        var paidId = plan.Installments.First().Id;
        plan.RecordInstallmentPayment(paidId, 200m, PaymentMethod.Cash, DoneOn);

        plan.ReviseInstallments(new[]
        {
            ((Guid?)paidId, DoneOn, 200m),
            ((Guid?)null, DoneOn.AddMonths(1), 300m),
        });

        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
        Assert.Equal(200m, plan.AmountPaid);
        Assert.Equal(2, plan.Installments.Count);
    }

    // [AC-22] An installment can never be pushed below what it has already collected.
    [Fact]
    public void ReviseInstallments_Rejects_A_Row_Below_Its_Collected_Amount()
    {
        var plan = AcceptedPlan();
        var paidId = plan.Installments.First().Id;
        plan.RecordInstallmentPayment(paidId, 400m, PaymentMethod.Cash, DoneOn);

        Assert.Throws<InvalidOperationException>(() => plan.ReviseInstallments(new[]
        {
            ((Guid?)paidId, DoneOn, 300m),
            ((Guid?)null, DoneOn.AddMonths(1), 200m),
        }));
    }

    // A Completed or Cancelled plan has no remaining treatment to amend.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Amending_A_Closed_Plan_Is_Rejected(bool completed)
    {
        var plan = completed ? CompletedPlan() : AcceptedPlan();
        if (!completed) plan.Cancel("Patient parti");

        Assert.Throws<InvalidOperationException>(() =>
            plan.AddItems(new[] { ("Implant", 800m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 21 }) }));
    }
}
