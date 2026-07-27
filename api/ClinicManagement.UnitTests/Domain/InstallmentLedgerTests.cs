using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-19][AC-20][AC-21] Each installment payment is its own ledger row, dated when the money was received.
///
/// <para>
/// This is what fixes the wrong-month bug. An installment used to keep only a running <c>AmountPaid</c> plus
/// the <b>latest</b> date, so 400 DT in January and 600 in February reported 0 then 1000 — and January's
/// already-published figure changed <i>retroactively</i> the moment February's payment landed. The invoice side
/// was always event-sourced and correct; this mirrors it.
/// </para>
/// </summary>
public class InstallmentLedgerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime January = new(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime February = new(2026, 2, 5, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>An accepted 1000 DT plan whose échéancier is a single 1000 DT installment.</summary>
    private static TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(new[]
        {
            ("Couronne", 1000m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }),
        });
        plan.Accept("2026-0001");
        return plan;
    }

    private static Installment SoleInstallment(TreatmentPlan plan) => plan.Installments.Single();

    // [AC-19] THE fix. Two payments in different months are two rows, each on its own date — so each month
    // reports what it actually took.
    [Fact]
    public void Two_Payments_In_Different_Months_Are_Two_Dated_Rows()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);

        plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);
        plan.RecordInstallmentPayment(installment.Id, 600m, PaymentMethod.Card, February);

        Assert.Equal(2, installment.Payments.Count);
        Assert.Equal(400m, installment.Payments.Single(p => p.PaidOn == January).Amount);
        Assert.Equal(600m, installment.Payments.Single(p => p.PaidOn == February).Amount);
        Assert.Equal(1000m, installment.AmountPaid);
    }

    // [AC-20] The stored denormalizations are derived from the ledger: AmountPaid is the live sum, and
    // Last* follow the most recent live payment.
    [Fact]
    public void The_Stored_Totals_Are_Derived_From_The_Ledger()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);

        plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);
        plan.RecordInstallmentPayment(installment.Id, 600m, PaymentMethod.Card, February);

        Assert.Equal(1000m, installment.AmountPaid);
        Assert.Equal(February, installment.LastPaidOn);
        Assert.Equal(PaymentMethod.Card, installment.LastMethod);
        Assert.True(installment.IsPaid);
        Assert.Equal(0m, installment.Outstanding);
    }

    // [AC-21] Voiding keeps the row, marks it, and re-derives the totals from what is left.
    [Fact]
    public void Voiding_Keeps_The_Row_And_Re_Derives_The_Totals()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);
        var second = plan.RecordInstallmentPayment(installment.Id, 600m, PaymentMethod.Card, February);

        plan.VoidInstallmentPayment(installment.Id, second.Id, "Erreur de saisie", "local|abc", "Dr Bel Hadj");

        Assert.Equal(2, installment.Payments.Count);          // nothing deleted
        Assert.True(second.IsVoided);
        Assert.Equal("Erreur de saisie", second.VoidReason);
        Assert.Equal("Dr Bel Hadj", second.VoidedByName);
        Assert.Equal(400m, installment.AmountPaid);            // re-derived
        Assert.Equal(January, installment.LastPaidOn);         // falls back to the remaining live payment
        Assert.Equal(PaymentMethod.Cash, installment.LastMethod);
        Assert.False(installment.IsPaid);
    }

    // [AC-20] AmountPaid is no longer monotonic — which is exactly what Revise and the amendment rules key off.
    [Fact]
    public void AmountPaid_Can_Decrease_After_A_Void()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        var payment = plan.RecordInstallmentPayment(installment.Id, 1000m, PaymentMethod.Cash, January);
        Assert.Equal(1000m, installment.AmountPaid);

        plan.VoidInstallmentPayment(installment.Id, payment.Id, "Erreur");

        Assert.Equal(0m, installment.AmountPaid);
        Assert.Null(installment.LastPaidOn);
        Assert.Null(installment.LastMethod);
    }

    // [AC-21] The plan's status is NOT walked back — it tracks clinical progress, not payment. « Terminé »
    // means every act is done; correcting a payment must not un-complete the treatment.
    [Fact]
    public void Voiding_Does_Not_Walk_The_Plan_Status_Back()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        var payment = plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);

        plan.VoidInstallmentPayment(installment.Id, payment.Id, "Erreur");

        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
    }

    // [AC-21] A second void is refused rather than re-deriving twice.
    [Fact]
    public void Voiding_An_Already_Voided_Payment_Is_Refused()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        var payment = plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);
        plan.VoidInstallmentPayment(installment.Id, payment.Id, "Erreur");

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.VoidInstallmentPayment(installment.Id, payment.Id, "Encore"));

        Assert.Contains("déjà annulé", ex.Message);
        Assert.Equal("Erreur", payment.VoidReason);
    }

    // [AC-21] A motif is mandatory.
    [Fact]
    public void A_Void_Without_A_Reason_Is_Refused()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        var payment = plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);

        Assert.Throws<ArgumentException>(
            () => plan.VoidInstallmentPayment(installment.Id, payment.Id, "   "));
    }

    // [AC-21] A cancelled devis's payments are frozen.
    [Fact]
    public void Payments_On_A_Cancelled_Plan_Cannot_Be_Voided()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        var payment = plan.RecordInstallmentPayment(installment.Id, 400m, PaymentMethod.Cash, January);
        plan.Cancel("Devis à revoir");

        Assert.Throws<InvalidOperationException>(
            () => plan.VoidInstallmentPayment(installment.Id, payment.Id, "Erreur"));
    }

    // [AC-19] Overpayment is still refused, now measured against the live ledger sum.
    [Fact]
    public void Overpaying_An_Installment_Is_Still_Refused()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        plan.RecordInstallmentPayment(installment.Id, 900m, PaymentMethod.Cash, January);

        Assert.Throws<InvalidOperationException>(
            () => plan.RecordInstallmentPayment(installment.Id, 200m, PaymentMethod.Cash, February));
    }

    // [AC-19] …and voiding frees the room back up, because the guard reads the live sum.
    [Fact]
    public void Voiding_Frees_Room_For_A_Corrected_Payment()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);
        var wrong = plan.RecordInstallmentPayment(installment.Id, 900m, PaymentMethod.Cash, January);

        plan.VoidInstallmentPayment(installment.Id, wrong.Id, "Montant erroné");
        plan.RecordInstallmentPayment(installment.Id, 90m, PaymentMethod.Cash, January);

        Assert.Equal(90m, installment.AmountPaid);
        Assert.Equal(2, installment.Payments.Count);
    }

    // [AC-29] A sub-millime payment is refused rather than stored as 0,000.
    [Fact]
    public void A_Sub_Millime_Installment_Payment_Is_Refused()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);

        var ex = Assert.Throws<ArgumentException>(
            () => plan.RecordInstallmentPayment(installment.Id, 0.0004m, PaymentMethod.Cash, January));

        Assert.Contains("millime", ex.Message);
        Assert.Empty(installment.Payments);
    }

    // [AC-20] Same-day payments are ordered deterministically by insertion, so "most recent" is stable.
    [Fact]
    public void Same_Day_Payments_Resolve_The_Latest_Deterministically()
    {
        var plan = AcceptedPlan();
        var installment = SoleInstallment(plan);

        plan.RecordInstallmentPayment(installment.Id, 100m, PaymentMethod.Cash, January);
        plan.RecordInstallmentPayment(installment.Id, 200m, PaymentMethod.Cheque, January);

        Assert.Equal(300m, installment.AmountPaid);
        Assert.Equal(PaymentMethod.Cheque, installment.LastMethod);
    }
}
