using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The reversibility half of the treatment-plan audit: the verbs that let a dentist take something back.
///
/// <para>
/// ⚠️ <b>The domain was guarded well and reversible badly, and that is what the owner reported as « once
/// something becomes a treatment plan, I am always stuck ».</b> Nearly every refusal in the aggregate protects
/// real money or real clinical truth and is correct to keep; what was missing was the other half of each guard
/// — the remedy it names. <c>Cancelled</c> could not be left by anything in the product, parking was
/// all-or-nothing in both directions, a devis on the wrong patient could only be retyped under a second number,
/// and the échéancier could be left disagreeing with the total by three different writers.
/// </para>
/// </summary>
public class TreatmentPlanReversibilityTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherPatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime Today = new(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Two priced acts, the second cut into three séances. Numbered unless asked otherwise.</summary>
    private static TreatmentPlan Plan(bool numbered = true)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation 12-14");
        plan.SetItems(new[]
        {
            ("Extraction simple", 200m, (IReadOnlyList<int>)new List<int> { 12 }),
            ("Couronne céramique", 1000m, (IReadOnlyList<int>)new List<int> { 14 }),
        });

        var couronne = plan.Items.Last();
        plan.SetItemSteps(couronne.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", 60, null),
            new TreatmentPlanItemStepInput(null, "Essai de l'armature", 30, 14),
            new TreatmentPlanItemStepInput(null, "Scellement", 30, 14),
        });

        if (numbered)
        {
            plan.Accept("2026-0044");
        }

        return plan;
    }

    private static Installment SoleInstallment(TreatmentPlan plan) => plan.Installments.Single();

    // ---- C7 · one respread rule --------------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: the « déjà encaissé » refusal lived on two of four writers.</b> The amend handler's
    /// respread branch — taken whenever a total changes and no schedule is sent, which is every price
    /// correction made from the booking dialog or the acts table — had none. Removing a 200 DT act from a
    /// 1 200 DT devis with 1 200 DT collected left <c>Σ Amount</c> at 1 200 against a <c>TotalPlanned</c> of
    /// 1 000: <c>Outstanding</c> clamps at 0, both balance reads show 0, and 200 DT of the patient's money
    /// became unreachable — no credit line, no avoir prompt, no error.
    /// </summary>
    [Fact]
    public void Lowering_The_Total_Below_What_Was_Collected_Is_Refused_On_The_Respread_Branch()
    {
        var plan = Plan();
        plan.RecordInstallmentPayment(SoleInstallment(plan).Id, 1200m, PaymentMethod.Cash, Today);

        var extraction = plan.Items.First();
        plan.RemoveItem(extraction.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => plan.RespreadScheduleToTotal(Today));

        Assert.Contains("avoir", ex.Message);
    }

    /// <summary>The invariant the refusal exists for: every writer leaves Σ Amount == TotalPlanned.</summary>
    [Fact]
    public void A_Respread_Leaves_The_Schedule_Equal_To_The_Total()
    {
        var plan = Plan();
        plan.RecordInstallmentPayment(SoleInstallment(plan).Id, 300m, PaymentMethod.Cash, Today);

        plan.RemoveItem(plan.Items.First().Id);
        plan.RespreadScheduleToTotal(Today);

        Assert.Equal(1000m, plan.TotalPlanned);
        Assert.Equal(1000m, plan.Installments.Sum(i => i.Amount));
        Assert.Equal(300m, plan.AmountPaid);
    }

    // ---- C11 · Reopen re-spreads -------------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: two screens, two balances, no error.</b> <c>Reopen</c> restored the parked acts and
    /// recomputed <c>TotalPlanned</c> with no re-spread, and the plan is debt-bearing the instant its status is
    /// written. « Solde patient » reads <c>TotalPlanned − AmountPaid</c> while « Créances », the dashboard and
    /// <c>PatientDebtLines</c> sum <c>Amount − AmountPaid</c> over the installment ROWS — so a reopened
    /// treatment showed the patient owing on one screen and nothing on the other, and the receptionist was
    /// offered a payable room of 0.
    /// </summary>
    [Fact]
    public void Reopening_Puts_The_Schedule_Back_In_Step_With_The_Total()
    {
        var plan = Plan();
        var couronne = plan.Items.Last();
        plan.MarkItemStepDone(couronne.Id, couronne.Steps.First().Id, Today.AddMonths(-2), Guid.NewGuid());
        plan.RecordInstallmentPayment(SoleInstallment(plan).Id, 400m, PaymentMethod.Cash, Today.AddMonths(-2));

        plan.StopTreatment(Today);
        Assert.Equal(TreatmentPlanStatus.Stopped, plan.Status);

        plan.Reopen(Today);

        Assert.Equal(1200m, plan.TotalPlanned);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
        Assert.Equal(400m, plan.AmountPaid);
        Assert.Equal(800m, plan.Outstanding);
    }

    // ---- C2 · the stop button stops knowing about money ---------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: « Arrêter le traitement » branched into <c>Cancel</c> on a devis holding a deposit.</b>
    /// <c>StopWouldCancel</c> asked only « numbered, and nothing delivered? » — which is precisely the shape of
    /// a devis accepted with a deposit taken and no séance yet — and the dialog on that branch says
    /// « il n'y a donc rien à conserver », which is false to the dinar.
    /// </summary>
    [Fact]
    public void A_Devis_Holding_A_Deposit_Does_Not_Take_The_Cancel_Branch()
    {
        var plan = Plan();
        plan.RecordInstallmentPayment(SoleInstallment(plan).Id, 500m, PaymentMethod.Cash, Today);

        Assert.False(plan.StopWouldCancel);
    }

    /// <summary>
    /// And the stop it now takes refuses honestly, naming the avoir — never « annulez-le », which
    /// <see cref="TreatmentPlan.Cancel"/> would then refuse. A remedy a refusal names has to exist.
    /// </summary>
    [Fact]
    public void Stopping_A_Deposit_Only_Devis_Names_The_Avoir_Not_The_Cancellation()
    {
        var plan = Plan();
        plan.RecordInstallmentPayment(SoleInstallment(plan).Id, 500m, PaymentMethod.Cash, Today);

        var ex = Assert.Throws<InvalidOperationException>(() => plan.StopTreatment(Today));

        Assert.Contains("avoir", ex.Message);
        Assert.DoesNotContain("annulez", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_Numbered_Devis_With_Nothing_Delivered_And_No_Money_Still_Takes_The_Cancel_Branch()
    {
        var plan = Plan();

        Assert.True(plan.StopWouldCancel);
    }

    // ---- C3 · Cancelled is leavable ----------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: <c>Cancelled</c> was absorbing.</b> Every guard excluded it and <c>CanBeDeleted</c> is
    /// Draft-only, so a devis cancelled by mistake left the workspace with « Devis PDF » as its only surviving
    /// control — no amend, no correction, no payment, no delete, and no way back.
    /// </summary>
    [Fact]
    public void A_Cancelled_Devis_Can_Be_Brought_Back()
    {
        var plan = Plan();
        plan.Cancel("Erreur de patient");

        plan.Uncancel("Annulation par erreur", Today);

        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
        Assert.Equal("2026-0044", plan.Number);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    /// <summary>The number is never released and the first motif is never erased — the devis may be on paper.</summary>
    [Fact]
    public void Un_Cancelling_Keeps_The_Number_And_Appends_To_The_Motif()
    {
        var plan = Plan();
        plan.Cancel("Erreur de patient");

        plan.Uncancel("Le patient reprend le devis", Today);

        Assert.Contains("Erreur de patient", plan.CancellationReason);
        Assert.Contains("Le patient reprend le devis", plan.CancellationReason);
        Assert.Equal("2026-0044", plan.Number);
    }

    /// <summary>An un-numbered treatment comes back a Draft — never promoted into a debt it never carried.</summary>
    [Fact]
    public void Un_Cancelling_Never_Promotes_An_Un_Numbered_Treatment()
    {
        var plan = Plan(numbered: false);
        plan.Accept("2026-0045");
        plan.Cancel("Erreur");

        plan.Uncancel("Rétabli", Today);

        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    [Fact]
    public void Only_A_Cancelled_Devis_Can_Be_Un_Cancelled()
    {
        var plan = Plan();

        Assert.Throws<InvalidOperationException>(() => plan.Uncancel("Pourquoi pas", Today));
    }

    [Fact]
    public void Un_Cancelling_Requires_A_Motif()
    {
        var plan = Plan();
        plan.Cancel("Erreur");

        Assert.Throws<ArgumentException>(() => plan.Uncancel("   ", Today));
    }

    // ---- M2 / M1 · per-act park and restore ---------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The capability the driving complaint was asking for.</b> Parking was all-or-nothing both ways, so
    /// « la patiente revient, mais seulement pour la couronne » meant reopening the whole treatment and
    /// re-inflating the total with the acts she had declined — straight back into « Créances ». And
    /// <c>RemoveItem</c>, the only other way to drop an act, is refused the moment anything is delivered, with
    /// a remedy three refusals deep.
    /// </summary>
    [Fact]
    public void One_Act_Can_Be_Put_Aside_Without_Touching_The_Rest()
    {
        var plan = Plan();
        var extraction = plan.Items.First();

        plan.WithdrawItem(extraction.Id, Today);

        Assert.Equal(1000m, plan.TotalPlanned);
        Assert.Single(plan.ActiveItems);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    /// <summary>Unlike <c>RemoveItem</c>, an act with delivered work may be put aside — nothing is destroyed.</summary>
    [Fact]
    public void An_Act_With_Delivered_Work_Can_Be_Put_Aside_And_Keeps_Its_Seances()
    {
        var plan = Plan();
        var couronne = plan.Items.Last();
        var ficheId = Guid.NewGuid();
        plan.MarkItemStepDone(couronne.Id, couronne.Steps.First().Id, Today.AddDays(-30), ficheId);

        plan.WithdrawItem(couronne.Id, Today);

        var parked = plan.Items.Single(i => i.Id == couronne.Id);
        Assert.True(parked.IsWithdrawn);
        Assert.Equal(1, parked.StepsDone);
        Assert.Equal(ficheId, parked.Steps.First().LinkedDentalRecordId);
        Assert.Equal(200m, plan.TotalPlanned);
    }

    [Fact]
    public void A_Put_Aside_Act_Comes_Back_At_The_Etat_Its_Steps_Derive()
    {
        var plan = Plan();
        var couronne = plan.Items.Last();
        plan.MarkItemStepDone(couronne.Id, couronne.Steps.First().Id, Today.AddDays(-30), Guid.NewGuid());
        plan.WithdrawItem(couronne.Id, Today);

        plan.RestoreItem(couronne.Id, Today);

        var restored = plan.Items.Single(i => i.Id == couronne.Id);
        Assert.False(restored.IsWithdrawn);
        Assert.Equal(TreatmentPlanItemStatus.InProgress, restored.Status);
        Assert.Equal(1200m, plan.TotalPlanned);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    /// <summary>
    /// Emptying the plan is « arrêter le traitement », which writes the right status and says so. Putting the
    /// last act aside would leave a live devis claiming nothing, badged as if it were still running.
    /// </summary>
    [Fact]
    public void The_Last_Active_Act_Cannot_Be_Put_Aside()
    {
        var plan = Plan();
        plan.WithdrawItem(plan.Items.First().Id, Today);

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.WithdrawItem(plan.Items.Last().Id, Today));

        Assert.Contains("arrêtez le traitement", ex.Message);
    }

    /// <summary>The money rule is the shared one — putting an act aside cannot strand collected cash either.</summary>
    [Fact]
    public void Putting_An_Act_Aside_Cannot_Take_The_Total_Below_What_Was_Collected()
    {
        var plan = Plan();
        plan.RecordInstallmentPayment(SoleInstallment(plan).Id, 1100m, PaymentMethod.Cash, Today);

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.WithdrawItem(plan.Items.Last().Id, Today));

        Assert.Contains("avoir", ex.Message);
    }

    // ---- M5 · the wrong patient ---------------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: <c>PatientId</c> was constructor-only.</b> A devis issued on the wrong file could only
    /// be deleted (Draft, no work) or retyped under a **new number** — spending a second one out of a
    /// per-clinic-per-year series to correct a mis-click.
    /// </summary>
    [Fact]
    public void A_Devis_On_The_Wrong_Patient_Can_Be_Moved()
    {
        var plan = Plan();

        plan.ReassignPatient(OtherPatientId);

        Assert.Equal(OtherPatientId, plan.PatientId);
        Assert.Equal("2026-0044", plan.Number);
    }

    /// <summary>A recorded séance belongs to the mouth it was carried out in.</summary>
    [Fact]
    public void A_Devis_With_Delivered_Work_Cannot_Change_Patient()
    {
        var plan = Plan();
        var couronne = plan.Items.Last();
        plan.MarkItemStepDone(couronne.Id, couronne.Steps.First().Id, Today.AddDays(-30), Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => plan.ReassignPatient(OtherPatientId));
    }

    // ---- C8 · SetItems protects a followed treatment ------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: silent destruction of clinical history from a title edit.</b> <c>SetItems</c> reuses an
    /// echoed-back **id** but builds a brand-new act — empty steps, no fiche link, <c>Planned</c> — and
    /// <c>EnsureDraft</c> was its only gate. Since « Suivre ce traitement » a Draft routinely carries recorded
    /// séances, so « Modifier le brouillon » from the plans list (which opens this editor, not the amend one)
    /// deleted the protocol and every fiche link with no error.
    /// </summary>
    [Fact]
    public void A_Draft_Carrying_Seances_Cannot_Be_Re_Typed_Line_By_Line()
    {
        var plan = Plan(numbered: false);
        var couronne = plan.Items.Last();
        plan.MarkItemStepDone(couronne.Id, couronne.Steps.First().Id, Today.AddDays(-30), Guid.NewGuid());

        var ex = Assert.Throws<InvalidOperationException>(() => plan.SetItems(new[]
        {
            ("Extraction simple", 200m, (IReadOnlyList<int>)new List<int> { 12 }),
        }));

        Assert.Contains("Modifier les actes et les prix", ex.Message);
    }

    /// <summary>A Draft carrying a step protocol is protected too — losing it loses the séances to come.</summary>
    [Fact]
    public void A_Draft_Carrying_Steps_Cannot_Be_Re_Typed_Line_By_Line()
    {
        var plan = Plan(numbered: false);

        Assert.Throws<InvalidOperationException>(() => plan.SetItems(new[]
        {
            ("Extraction simple", 200m, (IReadOnlyList<int>)new List<int> { 12 }),
        }));
    }

    /// <summary>A plain draft nobody has worked on is still edited line by line — that is what the editor is for.</summary>
    [Fact]
    public void A_Plain_Draft_Is_Still_Editable_Line_By_Line()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis simple");
        plan.SetItems(new[] { ("Détartrage", 60m, (IReadOnlyList<int>)new List<int>()) });

        plan.SetItems(new[] { ("Détartrage deux séances", 120m, (IReadOnlyList<int>)new List<int>()) });

        Assert.Equal(120m, plan.TotalPlanned);
    }

    // ---- m3 · reorder reads the live acts ------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The defect: <c>SetItemOrder</c> demanded every row, parked acts included</b> — while every surface
    /// that renders acts renders <c>ActiveItems</c>. Any reorder on a stopped-then-partly-restored plan was
    /// refused with a sentence naming nothing the user could act on.
    /// </summary>
    [Fact]
    public void Reordering_Names_Only_The_Active_Acts()
    {
        var plan = Plan();
        var extraction = plan.Items.First();
        var couronne = plan.Items.Last();
        plan.WithdrawItem(extraction.Id, Today);

        plan.SetItemOrder(new[] { couronne.Id });

        Assert.Equal(0, plan.Items.Single(i => i.Id == couronne.Id).SequenceNumber);
        // The parked act keeps its history and sits behind everything still being treated.
        Assert.Equal(1, plan.Items.Single(i => i.Id == extraction.Id).SequenceNumber);
    }

    // ---- m7 · one statement of « which statuses are live » -------------------------------------------

    [Theory]
    [InlineData(TreatmentPlanStatus.Draft, true)]
    [InlineData(TreatmentPlanStatus.Accepted, true)]
    [InlineData(TreatmentPlanStatus.InProgress, true)]
    [InlineData(TreatmentPlanStatus.Completed, false)]
    [InlineData(TreatmentPlanStatus.Stopped, false)]
    [InlineData(TreatmentPlanStatus.Cancelled, false)]
    public void The_Aggregate_Reads_The_One_Live_Status_List(TreatmentPlanStatus status, bool live)
        => Assert.Equal(live, TreatmentPlanLifecycle.IsLive(status));
}
