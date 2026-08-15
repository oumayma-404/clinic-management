namespace ClinicManagement.Domain.Services;

/// <summary>
/// Pure, testable invoice arithmetic (no persistence). All money is rounded to the millime
/// (3 decimals), away-from-zero.
/// </summary>
/// <remarks>
/// ⚠️ <b>An act's price IS what the patient pays — there is no TVA and no timbre fiscal.</b> This used to
/// compute Total HT → +TVA → +timbre → Total TTC, which made the total a function of clinic settings the
/// chairside fiche de soins knew nothing about: the fiche priced the acts and told the dentist
/// « Reste à payer : 0,000 » while the note d'honoraires it generated was 8.7 % higher, silently creating a
/// receivable at the exact moment cash changed hands. Removing the tax is what makes that disagreement
/// <i>unrepresentable</i> rather than merely fixed in one of the two places.
/// <para><b>Already-issued invoices are not rewritten.</b> They are numbered legal documents that really were
/// issued with TVA, and their frozen <c>TotalVat</c>/<c>TotalTtc</c> stay exactly as they were — nothing
/// recomputes an issued invoice, so history keeps rendering truthfully. Only new invoices are untaxed.</para>
/// </remarks>
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
    /// Compute the frozen totals from the sum of line totals. The total the patient owes is the sum of the
    /// acts and nothing else, so <c>TotalTtc == TotalHt</c> and <c>TotalVat</c> is always zero.
    /// </summary>
    /// <remarks>
    /// The parameter is still named <c>totalHt</c> and <see cref="InvoiceTotals.TotalTtc"/> still exists
    /// because both are persisted columns on every historical invoice. Keeping the names is what lets the
    /// stored totals of a note issued last year keep their meaning; it is not a claim that a tax is applied.
    /// </remarks>
    public static InvoiceTotals Compute(decimal totalHt)
    {
        var ht = RoundMoney(totalHt);
        return new InvoiceTotals(ht, 0m, ht);
    }
}

/// <summary>Frozen money totals of an invoice, all in TND millimes.</summary>
public readonly record struct InvoiceTotals(decimal TotalHt, decimal TotalVat, decimal TotalTtc);
