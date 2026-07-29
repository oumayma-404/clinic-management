using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>
/// Turns a fiche de soins' acts into invoice lines — the single authority on how recorded work is priced onto a
/// note d'honoraires.
///
/// <para><b>Why this exists as a server-side helper.</b> The rule lived in the <b>frontend</b>, inline in the
/// patient page's « Facturer cette intervention » handler: it read each act's <c>IsPerTooth</c>/<c>UnitCost</c>
/// provenance and decided between « quantity × unit price » and « one flat line ». That was fine while the only
/// caller was a browser prefill, and became a second pricing authority the moment a fiche could bill itself
/// server-side. Two implementations of how work becomes money is the § 5.10 defect in a new place, so the rule
/// was <b>moved</b> here, not copied.</para>
///
/// <para><b>The provenance is why a per-tooth act is not one line.</b> <c>DentalRecordAct.Cost</c> is
/// authoritative and is never recomputed, but for a per-tooth act it is <c>UnitCost × teeth</c> — and a patient
/// reading « Composite (dents 16, 26, 36) … 270,000 DT » cannot check the arithmetic. Billing it as 3 × 90,000
/// shows what the total covers. A flat fee, or a legacy act with no captured unit price, stays one line.</para>
/// </summary>
public static class DentalRecordInvoiceLines
{
    /// <summary>One priced invoice line: designation, quantity and unit price HT.</summary>
    public sealed record Line(string Designation, int Quantity, decimal UnitPriceHt);

    /// <summary>
    /// The lines billing <paramref name="record"/>'s acts, in the order the fiche records them.
    /// <para>
    /// A fiche with no acts falls back to a single line carrying the record's own derived <c>Cost</c> and
    /// summary: legacy fiches predate the multi-act model, and returning nothing would produce an empty invoice
    /// with a number attached to it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Line> For(DentalRecord record)
    {
        if (record.Acts.Count == 0)
        {
            return new[] { new Line(Designation(record.ProcedureType, record.Teeth.Select(t => t.ToothNumber)), 1, record.Cost) };
        }

        return record.Acts.Select(ToLine).ToList();
    }

    private static Line ToLine(DentalRecordAct act)
    {
        var designation = Designation(act.ProcedureName, act.ToothNumbers);

        // Per-tooth AND priced per tooth AND actually applied to teeth — all three, or the quantity would be a
        // guess. `UnitCost` is nullable precisely because a legacy act never captured one.
        if (act.IsPerTooth && act.ToothNumbers.Count > 0 && act.UnitCost is { } unitCost)
        {
            return new Line(designation, act.ToothNumbers.Count, unitCost);
        }

        return new Line(designation, 1, act.Cost);
    }

    /// <summary>
    /// « Composite (dents 16, 26) » — the teeth belong on the line so the patient can see what was treated.
    /// Never a diagnosis: an invoice line carries the act, not the condition (medical secrecy).
    /// </summary>
    private static string Designation(string procedureName, IEnumerable<int> toothNumbers)
    {
        var teeth = toothNumbers.ToList();
        return teeth.Count > 0 ? $"{procedureName} (dents {string.Join(", ", teeth)})" : procedureName;
    }
}
