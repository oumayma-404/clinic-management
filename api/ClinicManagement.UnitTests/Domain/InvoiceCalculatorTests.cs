using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// An act's price is the whole of what the patient owes: the frozen totals are the sum of the line totals,
/// rounded to the millime, with no TVA and no timbre fiscal.
/// </summary>
/// <remarks>
/// These cases used to assert the opposite (HT → +TVA → +timbre → TTC). The tax was removed because it made
/// the total a function of clinic settings the chairside fiche de soins knew nothing about: the fiche priced
/// the acts and told the dentist « Reste à payer : 0,000 » while the note d'honoraires it generated was 8.7 %
/// higher, silently creating a receivable at the moment cash changed hands. The load-bearing case here is
/// <see cref="Total_Owed_Is_Exactly_The_Sum_Of_The_Acts"/> — it is what makes that disagreement impossible.
/// </remarks>
public class InvoiceCalculatorTests
{
    // The whole point: what the acts cost is what the invoice totals, to the millime.
    [Fact]
    public void Total_Owed_Is_Exactly_The_Sum_Of_The_Acts()
    {
        var totals = InvoiceCalculator.Compute(100m);

        Assert.Equal(100.000m, totals.TotalHt);
        Assert.Equal(0m, totals.TotalVat);
        Assert.Equal(100.000m, totals.TotalTtc);
    }

    // The exact figures from the defect this change closes: a 60,000 DT séance used to be billed 65,200 TTC
    // (7 % TVA + 1,000 timbre), so a patient who had paid in full still owed 5,200. It is now 60,000 flat.
    [Fact]
    public void A_Sixty_Dinar_Seance_Is_Billed_Sixty_Dinars()
    {
        var totals = InvoiceCalculator.Compute(60m);

        Assert.Equal(60.000m, totals.TotalTtc);
        Assert.NotEqual(65.200m, totals.TotalTtc);
    }

    // TotalTtc and TotalHt are the same number for every value, which is the property the fiche relies on.
    [Theory]
    [InlineData(0)]
    [InlineData(33.333)]
    [InlineData(1250.5)]
    public void Ttc_Always_Equals_Ht(decimal ht)
    {
        var totals = InvoiceCalculator.Compute(ht);

        Assert.Equal(totals.TotalHt, totals.TotalTtc);
        Assert.Equal(0m, totals.TotalVat);
    }

    // Rounding is still to the millime (3 decimals) — dropping the tax did not change the money grain.
    [Fact]
    public void Compute_Rounds_To_The_Millime()
    {
        var totals = InvoiceCalculator.Compute(33.3335m);

        Assert.Equal(33.334m, totals.TotalHt);
        Assert.Equal(33.334m, totals.TotalTtc);
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
