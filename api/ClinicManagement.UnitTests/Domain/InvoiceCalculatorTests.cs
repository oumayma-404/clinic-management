using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-3] The frozen totals follow HT → +TVA (single clinic rate on the HT total, 0 if exonerated)
/// → +timbre → TTC, rounded to the millime.
/// </summary>
public class InvoiceCalculatorTests
{
    // [AC-3] Standard case: VAT 7 % + 1,000 DT stamp on a 100 DT HT total.
    [Fact]
    public void Compute_Applies_Vat_And_Stamp()
    {
        var totals = InvoiceCalculator.Compute(100m, vatApplicable: true, vatRate: 7m, stampDutyAmount: 1.000m);

        Assert.Equal(100.000m, totals.TotalHt);
        Assert.Equal(7.000m, totals.TotalVat);
        Assert.Equal(108.000m, totals.TotalTtc);
    }

    // [AC-3] Exonerated (VAT not applicable): no VAT, stamp still applies.
    [Fact]
    public void Compute_Exonerated_Has_No_Vat_But_Keeps_Stamp()
    {
        var totals = InvoiceCalculator.Compute(100m, vatApplicable: false, vatRate: 7m, stampDutyAmount: 1.000m);

        Assert.Equal(0m, totals.TotalVat);
        Assert.Equal(101.000m, totals.TotalTtc);
    }

    // [AC-3] VAT applicable but rate 0 → no VAT line value.
    [Fact]
    public void Compute_Zero_Rate_Has_No_Vat()
    {
        var totals = InvoiceCalculator.Compute(100m, vatApplicable: true, vatRate: 0m, stampDutyAmount: 1.000m);

        Assert.Equal(0m, totals.TotalVat);
        Assert.Equal(101.000m, totals.TotalTtc);
    }

    // [AC-3] Stamp disabled (amount 0) → not added to TTC.
    [Fact]
    public void Compute_No_Stamp_When_Amount_Zero()
    {
        var totals = InvoiceCalculator.Compute(100m, vatApplicable: true, vatRate: 7m, stampDutyAmount: 0m);

        Assert.Equal(107.000m, totals.TotalTtc);
    }

    // [AC-3] Rounding is to the millime (3 decimals).
    [Fact]
    public void Compute_Rounds_Vat_To_Millime()
    {
        var totals = InvoiceCalculator.Compute(33.333m, vatApplicable: true, vatRate: 7m, stampDutyAmount: 0m);

        // 33.333 * 0.07 = 2.33331 -> 2.333
        Assert.Equal(2.333m, totals.TotalVat);
        Assert.Equal(35.666m, totals.TotalTtc);
    }

    [Fact]
    public void LineTotal_Is_Quantity_Times_UnitPrice_Rounded()
    {
        Assert.Equal(99.999m, InvoiceCalculator.LineTotal(3, 33.333m));
        Assert.Equal(150.000m, InvoiceCalculator.LineTotal(2, 75m));
    }

    [Fact]
    public void RoundMoney_Rounds_Away_From_Zero_At_Three_Decimals()
    {
        Assert.Equal(1.235m, InvoiceCalculator.RoundMoney(1.2345m));
        Assert.Equal(1.001m, InvoiceCalculator.RoundMoney(1.0005m));
    }
}
