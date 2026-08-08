using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// What billing a fiche de soins actually did — the typed return of <c>BillDentalRecordCommand</c>.
///
/// <para><b>Why a type and not a bare <see cref="InvoiceDto"/>.</b> The command has three distinct successful
/// endings — a note d'honoraires was raised, an existing one was topped up, or there was simply nothing to add —
/// and the caller has to tell them apart to say anything true to the user. Before this it returned the invoice
/// alone, so the auto-billing path recovered the outcome by matching the French substring « déjà facturée »
/// against the error message: a sentence reworded anywhere is a behaviour change nothing compiles against and no
/// test notices. The outcome is now data.</para>
///
/// <para>Refusals are <b>not</b> outcomes here — they are <c>Result.Failure</c> with a
/// <see cref="Result.Code"/> from <c>DentalRecordBillingRefusals</c>, because a refusal has no invoice and no
/// amount to report, and because the caller branches on the code rather than on prose for the same reason.</para>
/// </summary>
public class DentalRecordBillingResult
{
    /// <summary>
    /// <see cref="DentalRecordBillingOutcome.Billed"/>, <see cref="DentalRecordBillingOutcome.ToppedUp"/> or
    /// <see cref="DentalRecordBillingOutcome.AlreadyBilled"/>. The refusal and failure members of that enum are
    /// produced by the caller from a failed <c>Result</c>, never by this type.
    /// </summary>
    public required DentalRecordBillingOutcome Outcome { get; init; }

    /// <summary>
    /// The note d'honoraires this fiche is on — newly raised, topped up, or the one that already billed it.
    /// Always present: every outcome this type can carry names a real document.
    /// </summary>
    public required InvoiceDto Invoice { get; init; }

    /// <summary>
    /// What this call put in the till, which is <b>not</b> the invoice's collected total: a top-up of 200,000 on a
    /// note that had already taken 200,000 reports 200,000 here and 400,000 on <see cref="Invoice"/>. The user is
    /// told what just moved; the document states what it holds.
    /// </summary>
    public decimal AmountCollected { get; init; }

    /// <summary>The French explanation for <see cref="DentalRecordBillingOutcome.AlreadyBilled"/>; null otherwise.</summary>
    public string? Message { get; init; }
}
