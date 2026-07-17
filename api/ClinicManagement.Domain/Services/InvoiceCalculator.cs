namespace ClinicManagement.Domain.Services;

/// <summary>
/// Pure, testable invoice arithmetic (no persistence). Encapsulates the Tunisian note-d'honoraires
/// rule: Total HT → +TVA (single clinic rate applied to the HT total, or 0 when exonerated) → +timbre
/// fiscal → Total TTC. All money is rounded to the millime (3 decimals), away-from-zero.
/// </summary>
public static class InvoiceCalculator
{
    /// <summary>Number of decimals for Tunisian dinar money values (millimes).</summary>
    public const int MoneyDecimals = 3;

    /// <summary>Round a money value to the millime (3 decimals, away-from-zero).</summary>
    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

    /// <summary>Line total HT = quantity × unit price HT, rounded to the millime.</summary>
    public static decimal LineTotal(int quantity, decimal unitPriceHt) =>
        RoundMoney(quantity * unitPriceHt);

    /// <summary>
    /// Compute the frozen totals from the sum of line totals HT plus the clinic's VAT/stamp settings.
    /// VAT is only applied when <paramref name="vatApplicable"/> and the rate is &gt; 0; the stamp duty is
    /// added to the TTC only when its amount is &gt; 0 (it is never part of the VAT base).
    /// </summary>
    public static InvoiceTotals Compute(decimal totalHt, bool vatApplicable, decimal vatRate, decimal stampDutyAmount)
    {
        var ht = RoundMoney(totalHt);
        var vat = vatApplicable && vatRate > 0
            ? RoundMoney(ht * vatRate / 100m)
            : 0m;
        var stamp = stampDutyAmount > 0 ? RoundMoney(stampDutyAmount) : 0m;
        var ttc = RoundMoney(ht + vat + stamp);
        return new InvoiceTotals(ht, vat, ttc);
    }
}

/// <summary>Frozen money totals of an invoice, all in TND millimes.</summary>
public readonly record struct InvoiceTotals(decimal TotalHt, decimal TotalVat, decimal TotalTtc);
