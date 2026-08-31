using ClinicManagement.Application.Features.Invoices;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// « Montant payé » may not exceed what the séance's acts come to.
///
/// <para><b>The reported bug.</b> A fiche submitted with <c>Cost 40,000</c> and <c>AmountPaid 999,000</c> was
/// persisted with both values, <b>no note d'honoraires was raised</b>, nothing reached la caisse — and the patient's
/// « Actes dentaires » table then displayed « 999,000 DT payé · Reste 0,000 », i.e. asserted the cabinet had
/// collected money that existed in no ledger. Reproduced twice; a fiche with <c>AmountPaid == Cost</c> billed
/// correctly, which is what identified the amount as the cause.</para>
///
/// <para><b>Why the rule had no owner.</b> The only <c>paid ≤ total</c> check was in
/// <c>BillDentalRecordCommand.ResolvePayment</c>, which runs <b>post-commit</b> on both fiche paths and returns a
/// failure carrying no <c>Result.Code</c> — so <c>DentalRecordAutoBilling</c> missed its three coded-refusal
/// branches and demoted it to a warning log plus a DTO field inside an HTTP 200. Neither dental-record handler
/// compared the two, and the entity ctor only rejects negatives.</para>
///
/// <para>The two sibling money dialogs already refuse the same class of mistake — an installment payment above the
/// balance and an avoir above the collected amount — so this closes the third of three.</para>
/// </summary>
public class DentalRecordPaymentWithinCostTests
{
    [Fact]
    public void Paid_Above_Cost_Is_Refused_With_Its_Own_Code()
    {
        var result = DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 40m, amountPaid: 999m);

        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.PaymentExceedsCostCode, result.Code);
    }

    /// <summary>
    /// The refusal names both figures. « Le montant est invalide » sends the user looking at the wrong field on a
    /// fiche whose real problem may be a missing act rather than a mis-keyed amount.
    /// </summary>
    [Fact]
    public void Refusal_Names_Both_The_Amount_And_The_Total()
    {
        var result = DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 180m, amountPaid: 500m);

        Assert.Contains("500,000", result.Error);
        Assert.Contains("180,000", result.Error);
    }

    [Fact]
    public void Paid_Equal_To_Cost_Is_Allowed()
    {
        Assert.True(DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 60m, amountPaid: 60m).IsSuccess);
    }

    /// <summary>Paying part of a séance is the ordinary « il paiera le reste » case, not an error.</summary>
    [Fact]
    public void Paid_Below_Cost_Is_Allowed()
    {
        Assert.True(DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 180m, amountPaid: 50m).IsSuccess);
    }

    [Fact]
    public void Nothing_Paid_Is_Allowed()
    {
        Assert.True(DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 180m, amountPaid: 0m).IsSuccess);
    }

    /// <summary>
    /// Compared through <c>InvoiceCalculator.RoundMoney</c>, so a sub-millime float artefact is not a refusal the
    /// user cannot act on — they typed the same number the screen shows.
    /// </summary>
    [Fact]
    public void A_Sub_Millime_Excess_Is_Not_Refused()
    {
        Assert.True(
            DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 60m, amountPaid: 60.0001m).IsSuccess);
    }

    [Fact]
    public void A_Millime_Above_Is_Refused()
    {
        Assert.True(
            DentalRecordBillingGuard.CheckPaymentWithinCost(cost: 60m, amountPaid: 60.001m).IsFailure);
    }
}
