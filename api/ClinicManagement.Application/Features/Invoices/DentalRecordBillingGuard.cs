using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>
/// « Cette fiche est-elle déjà portée par une note d'honoraires, et l'édition proposée est-elle compatible avec
/// elle ? » — asked in two places, answered here once.
///
/// <para><b>Both callers are load-bearing and neither can be dropped.</b> <c>UpdateDentalRecordCommand</c> asks it
/// <b>before</b> <c>SaveChangesAsync</c>, because that is the only moment at which « refusé » can still mean the
/// save did not happen: the auto-billing runs post-commit, so a refusal raised from there arrives after the edit
/// has been persisted and leaves the fiche permanently disagreeing with the note that bills it.
/// <c>BillDentalRecordCommand</c> asks it again on its own path, because « Facturer cette intervention » is a
/// manual action that never passes through the update command at all.</para>
///
/// <para>The wording of each refusal lives in <see cref="DentalRecordBillingRefusals"/>; the ordering of the
/// checks lives here, and it is deliberate — an invoice that can take no money at all is reported before what
/// changed on the fiche, because the second is not actionable until the first is resolved.</para>
/// </summary>
public static class DentalRecordBillingGuard
{
    /// <summary>
    /// What the note d'honoraires billing a fiche currently holds. A flat projection rather than the aggregate:
    /// the two callers need the same five facts, and one of them is a sum from a different aggregate entirely
    /// (the avoirs), so passing the <c>Invoice</c> around would leave each caller to remember to fetch it.
    /// </summary>
    /// <param name="BilledTotalHt">
    /// Σ of the invoice lines that bill <b>this</b> fiche — not the invoice's own total. Scoped to the record so
    /// the comparison stays true if a note ever carries more than one séance.
    /// </param>
    public sealed record Snapshot(
        Guid InvoiceId,
        string? Number,
        InvoiceStatus Status,
        decimal BilledTotalHt,
        decimal AmountCollected,
        decimal CreditedTotal)
    {
        /// <summary>The note is void, or every dinar it collected has been handed back on paper.</summary>
        public bool IsSpent =>
            Status == InvoiceStatus.Cancelled
            || (AmountCollected > 0m && CreditedTotal >= AmountCollected);

        /// <summary>
        /// Does this note still bill the work — i.e. must a correction to that work be refused?
        /// <para>
        /// ⚠️ <b>This is {@link IsSpent}'s inverse and it is exposed because two other guards were asking the
        /// question with `RepresentsItsPlan(Status)` alone, which cannot see an avoir.</b> A credit note is a
        /// separate aggregate keyed on the invoice; issuing one leaves the invoice `Paid`. So « Détacher la
        /// fiche » refused with « Annulez la facture (ou émettez un avoir) avant de corriger », the dentist
        /// issued the avoir, retried — and got the identical refusal, telling them again to do the thing they
        /// had just done. And « Annulez » was not available either: <c>Invoice.CanCancel</c> refuses a note
        /// carrying a live payment. Both remedies the message named were unreachable, so a fiche attached to the
        /// wrong step was permanent and the act could never be re-recorded.
        /// </para>
        /// </summary>
        public bool StillBillsTheWork => !IsSpent && PlanBillingRules.RepresentsItsPlan(Status);

        /// <summary>
        /// What the dentist must actually do before this note will let the work be corrected — the remedy that
        /// exists, with its figure, rather than a choice between two that do not.
        /// </summary>
        /// <remarks>
        /// The branch is <c>CanCancel</c>'s own rule: a note with no live payment is cancelled outright, and one
        /// that has collected money can only be credited — for the <b>whole</b> collected amount, because a
        /// partial avoir leaves the note still billing (see <see cref="IsSpent"/>).
        /// </remarks>
        public string Remedy =>
            AmountCollected > 0m
                ? $"Établissez un avoir pour la totalité des {InvoiceCalculator.RoundMoney(AmountCollected - CreditedTotal):0.000} DT "
                  + "restant à créditer sur cette note ; un avoir partiel ne suffit pas."
                : "Annulez cette note d'honoraires.";
    }

    /// <summary>
    /// The note billing <paramref name="dentalRecordId"/>, or null when nothing bills it yet.
    ///
    /// <para>A <b>cancelled</b> note is returned rather than treated as absent, which is the whole of A-1: the
    /// caller has to be able to tell « jamais facturée » from « facturée puis annulée » — the first may quietly
    /// raise a note, the second must not, or the séance ends up on two documents and nobody can say which one the
    /// patient is holding.</para>
    /// </summary>
    public static async Task<Snapshot?> LoadAsync(
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        Guid clinicId,
        Guid dentalRecordId,
        CancellationToken cancellationToken)
    {
        // The light act-level projection, as before: loading every invoice of the patient with its lines and
        // payments to answer one boolean is the § 9.7 over-fetch.
        var links = await invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken);
        var forRecord = links.Where(l => l.DentalRecordId == dentalRecordId).ToList();
        if (forRecord.Count == 0)
        {
            return null;
        }

        // A live note beats a cancelled one: a séance re-billed after a cancellation legitimately has both, and
        // the live one is the document that speaks for it.
        var link = forRecord.FirstOrDefault(l => PlanBillingRules.RepresentsItsPlan(l.Status));
        if (link.InvoiceId == Guid.Empty)
        {
            link = forRecord[0];
        }

        var invoice = await invoiceRepository.GetByIdAsync(link.InvoiceId, cancellationToken);
        if (invoice == null || invoice.ClinicId != clinicId)
        {
            // The link projection is clinic-scoped, so this is unreachable in practice; treating it as "nothing
            // bills this fiche" would silently authorise a second document, so it is reported as absent only
            // because there is genuinely no invoice to describe.
            return null;
        }

        var creditedTotal = await creditNoteRepository.GetTotalForInvoiceAsync(invoice.Id, cancellationToken);

        return new Snapshot(
            invoice.Id,
            invoice.Number,
            invoice.Status,
            InvoiceCalculator.RoundMoney(
                invoice.Lines.Where(l => l.DentalRecordId == dentalRecordId).Sum(l => l.LineTotalHt)),
            invoice.AmountCollected,
            InvoiceCalculator.RoundMoney(creditedTotal));
    }

    /// <summary>
    /// Refuse a correction to the clinical work a live note is billing — the shared answer for « Détacher la
    /// fiche » at act level and at step level.
    ///
    /// <para>⚠️ It exists because both of those handlers asked the question themselves with
    /// <c>RepresentsItsPlan(link.Status)</c> over the light link projection, which cannot see an avoir — so the
    /// refusal survived the remedy it named. See <see cref="Snapshot.StillBillsTheWork"/>. Returning success for
    /// a spent note is the whole fix: a fully-credited or cancelled note bills nothing, so there is nothing left
    /// to protect.</para>
    ///
    /// <para><paramref name="what"/> is « cet acte » or « cette étape » — the only difference between the two
    /// call sites, and the reason this takes a word rather than being written twice.</para>
    /// </summary>
    public static async Task<Result> EnsureWorkIsNotBilledAsync(
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        Guid clinicId,
        Guid? linkedDentalRecordId,
        string what,
        CancellationToken cancellationToken)
    {
        // No fiche attached ⇒ nothing an invoice line could be billing for it.
        if (linkedDentalRecordId is not { } recordId)
        {
            return Result.Success();
        }

        var note = await LoadAsync(
            invoiceRepository, creditNoteRepository, clinicId, recordId, cancellationToken);

        if (note is null || !note.StillBillsTheWork)
        {
            return Result.Success();
        }

        var document = note.Number is null ? "un brouillon de note d'honoraires" : $"la note n° {note.Number}";
        return Result.Failure(
            $"La fiche de soins de {what} est facturée sur {document}. {note.Remedy} "
            + $"Vous pourrez ensuite détacher {what} et refacturer la séance.");
    }

    /// <summary>
    /// Whether a séance's own arithmetic holds: « Montant payé » may not exceed what the acts come to.
    ///
    /// <para><b>Why this is separate from <see cref="Check"/>, and why both write paths call it.</b> `Check` only
    /// runs once a note d'honoraires exists — <see cref="LoadAsync"/> returns null otherwise — so it never saw the
    /// unbilled fiche, which is the ordinary case. The only place the rule lived was
    /// <c>BillDentalRecordCommand.ResolvePayment</c>, which runs <b>post-commit</b> on both fiche paths and returns
    /// an un-coded failure that <c>DentalRecordAutoBilling</c> demotes to a warning inside an HTTP 200: the fiche
    /// saved with 999 DT « payé » against a 40 DT act, no invoice was raised, nothing reached la caisse, and the
    /// patient's file then displayed the money as collected. « Refusé » has to mean the save did not happen.</para>
    /// </summary>
    public static Result CheckPaymentWithinCost(decimal cost, decimal amountPaid)
    {
        if (InvoiceCalculator.RoundMoney(amountPaid) > InvoiceCalculator.RoundMoney(cost))
        {
            return Result.Failure(
                DentalRecordBillingRefusals.PaymentExceedsCost(amountPaid, cost),
                DentalRecordBillingRefusals.PaymentExceedsCostCode);
        }

        return Result.Success();
    }

    /// <summary>
    /// Whether a fiche billed by <paramref name="invoice"/> may be saved with <paramref name="proposedCost"/> and
    /// <paramref name="proposedAmountPaid"/>.
    ///
    /// <para>Success does <b>not</b> mean « there is money to collect » — that is the billing command's own
    /// arithmetic. It means « nothing about this edit contradicts the note d'honoraires ».</para>
    /// </summary>
    public static Result Check(Snapshot invoice, decimal proposedCost, decimal proposedAmountPaid)
    {
        if (invoice.IsSpent)
        {
            return Result.Failure(
                DentalRecordBillingRefusals.InvoiceNotLive(invoice.Number),
                DentalRecordBillingRefusals.InvoiceNotLiveCode);
        }

        // An issued note's lines are frozen, so a fiche whose acts moved would stop describing what was billed.
        // Compared on the money rather than on the act list: `SetActs` regenerates every act id on every save, so
        // there is no before/after identity to diff — and it is the *price* that has to agree with the document.
        if (InvoiceCalculator.RoundMoney(proposedCost) != invoice.BilledTotalHt)
        {
            return Result.Failure(
                DentalRecordBillingRefusals.ActsChanged(invoice.Number),
                DentalRecordBillingRefusals.ActsChangedCode);
        }

        // Lowering it would ask the till to un-receive money that is on a numbered document. Raising it is the
        // ordinary « le patient a fini de payer » edit and is handled as a top-up, not here.
        if (InvoiceCalculator.RoundMoney(proposedAmountPaid) < invoice.AmountCollected)
        {
            return Result.Failure(
                DentalRecordBillingRefusals.PaymentLowered(invoice.Number, invoice.AmountCollected),
                DentalRecordBillingRefusals.PaymentLoweredCode);
        }

        return Result.Success();
    }
}
