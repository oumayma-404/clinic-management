namespace ClinicManagement.Application.Features.Invoices;

/// <summary>
/// The refusals that protect a fiche de soins already carried by an issued note d'honoraires — written <b>once</b>
/// and shared by the two commands that must agree about them.
///
/// <para><b>Why it is shared and not stated twice.</b> The authoritative guard is in
/// <c>UpdateDentalRecordCommand</c>, <b>pre-commit</b>: the auto-billing runs post-commit by design, so a refusal
/// raised there would arrive after the lowered « Montant payé » — or the changed act list — had already been
/// saved. The user would read a French refusal and the edit would stick anyway, leaving the fiche permanently
/// disagreeing with its own note d'honoraires. « Refusé » has to mean the save did not happen.</para>
///
/// <para>But <c>BillDentalRecordCommand</c> is <i>also</i> reachable directly — it is the manual « Facturer cette
/// intervention » action — so it implements the same two refusals as its own backstop. Two implementations of the
/// same refusal with two hand-typed sentences is how the guard and the backstop start telling a user two different
/// stories about the same rule; here there is one sentence and one code each.</para>
///
/// <para>Each refusal names the <b>avoir</b> as the route, because it is the only one: an issued note is corrected
/// by a credit note, never by an edit, and a refusal that does not say what to do instead reads as a bug.</para>
/// </summary>
public static class DentalRecordBillingRefusals
{
    /// <summary>« Montant payé » was lowered on a fiche whose note d'honoraires has already collected more.</summary>
    public const string PaymentLoweredCode = "dental_record_payment_lowered";

    /// <summary>The acts — and therefore the fiche's <c>Cost</c> — changed after its note was issued.</summary>
    public const string ActsChangedCode = "dental_record_acts_changed_after_billing";

    /// <summary>The fiche's note d'honoraires is cancelled or has been fully credited (A-1).</summary>
    public const string InvoiceNotLiveCode = "dental_record_invoice_not_live";

    /// <summary>« Montant payé » exceeds the séance's own total, on a fiche nothing has billed yet.</summary>
    public const string PaymentExceedsCostCode = "dental_record_payment_exceeds_cost";

    /// <summary>The séance was redated but one of its cheques is already banked (L4).</summary>
    public const string PaymentBankedCode = "dental_record_payment_banked";

    /// <summary>
    /// The refusals a correction can get past — the fiche disagrees with its note, and replacing the note is the
    /// honest way out. Read by the API so the client can offer « Corriger » on exactly these and nothing else.
    ///
    /// <para><see cref="InvoiceNotLiveCode"/> is absent on purpose: there is no live note left to replace, so the
    /// séance is simply re-billed from « Facturer cette intervention ». <see cref="PaymentExceedsCostCode"/> and
    /// <see cref="PaymentBankedCode"/> are absent because neither is a disagreement with a document — the first is
    /// the séance's own arithmetic, the second is a fact about a bank.</para>
    /// </summary>
    public static bool IsCorrectable(string? code) =>
        code is ActsChangedCode or PaymentLoweredCode;

    /// <summary>
    /// Lowering the collected amount. Money already recorded on a numbered document cannot be un-received by
    /// retyping a field.
    /// </summary>
    public static string PaymentLowered(string? invoiceNumber, decimal alreadyCollected) =>
        $"Cette fiche est facturée sur {Document(invoiceNumber)}, qui a déjà encaissé "
        + $"{FormatAmount(alreadyCollected)} DT. Un montant déjà encaissé ne peut pas être diminué ici : "
        + "établissez un avoir sur la note d'honoraires.";

    /// <summary>
    /// Changing the acts of a billed fiche. Not refused because editing is dangerous, but because an issued note's
    /// lines are frozen — the fiche would silently stop describing what was billed.
    /// </summary>
    public static string ActsChanged(string? invoiceNumber) =>
        $"Les actes de cette fiche sont facturés sur {Document(invoiceNumber)} et ne peuvent plus être modifiés. "
        + "Établissez un avoir sur cette note d'honoraires, puis refacturez la séance.";

    /// <summary>
    /// The note this fiche is on is cancelled or fully credited (A-1). Deliberately a refusal rather than raising a
    /// second document: two notes d'honoraires for one séance is exactly the duplicate the whole guard exists to
    /// prevent, and the clinic can no longer tell which one the patient holds.
    /// </summary>
    public static string InvoiceNotLive(string? invoiceNumber) =>
        $"Cette fiche est facturée sur {Document(invoiceNumber)}, qui est annulée ou entièrement créditée. "
        + "Aucun encaissement ne peut y être ajouté, et une seconde note ne sera pas créée : "
        + "facturez la séance à nouveau depuis « Facturer cette intervention ».";

    /// <summary>
    /// More collected than the séance is worth. Refused rather than clamped: the difference is either a mis-key or
    /// an act nobody recorded, and only the person at the chair knows which.
    /// </summary>
    public static string PaymentExceedsCost(decimal amountPaid, decimal cost) =>
        $"Le montant payé ({FormatAmount(amountPaid)} DT) dépasse le total de la séance "
        + $"({FormatAmount(cost)} DT). Corrigez le montant, ou ajoutez l'acte qui manque.";

    /// <summary>
    /// « la note n° 2026-0042 » — or « un brouillon de note d'honoraires » when the invoice has no number yet.
    /// A refusal with no number sends the user hunting through /factures.
    /// </summary>
    private static string Document(string? invoiceNumber) =>
        invoiceNumber is null ? "un brouillon de note d'honoraires" : $"la note n° {invoiceNumber}";

    /// <summary>Millimes, French decimal comma — matching what every money surface in the product prints.</summary>
    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
