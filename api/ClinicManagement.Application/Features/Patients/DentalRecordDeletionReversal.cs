using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Everything a fiche de soins produced, undone when the fiche is deleted — <b>and the one place that says what
/// will be undone before it happens</b>.
///
/// <para>
/// <b>The defect this exists to remove, measured on the live database 2026-09-21.</b> A séance was recorded,
/// the fiche deleted and the séance re-recorded, three times inside nine minutes. Each save collected 80,000 DT;
/// each deletion left that money exactly where it was. The patient paid <b>160,000 DT for an 80,000 DT
/// extraction</b>, plus a third 80,000 DT on a note d'honoraires whose fiche was also gone — and nothing
/// errored, on any of the six writes.
/// </para>
/// <para>
/// ⚠️ <b>The mechanism is not the money, it is the identity.</b>
/// <c>InstallmentPayment.DentalRecordId</c> is the idempotence key of
/// <see cref="TreatmentPlan.CollectedOnRecord"/> — the thing that makes re-saving a fiche take the money once.
/// Delete the fiche and the next recording gets a <i>new</i> id, so the key resets and the same séance is
/// collected again while the first payment stays. The deletion handler cleaned three of the fiche's six FK-less
/// links and its own comment said « the two soft links to this fiche ».
/// </para>
///
/// <para>
/// <b>One class for the preview and for the act</b>, because a warning that can disagree with what follows is
/// worse than no warning: <see cref="InspectAsync"/> decides, <see cref="ApplyAsync"/> carries out exactly what
/// it decided, and the confirmation the user reads is rendered from the same <see cref="DentalRecordReversal"/>.
/// This repository's most-recorded defect shape is a correct rule wired to one of its call sites.
/// </para>
/// </summary>
public static class DentalRecordDeletionReversal
{
    /// <summary>
    /// What deleting this fiche will undo, or why it must be refused. <b>Reads only</b> — nothing here mutates,
    /// so the same call backs the confirmation dialog and the deletion itself.
    /// </summary>
    /// <remarks>
    /// The loaded aggregates travel on the result so <see cref="ApplyAsync"/> works on the copies that were
    /// inspected. Re-loading them there would open a window in which the money changed between the decision and
    /// the act — and it is the same DbContext either way, so it would also be two tracked copies of one row.
    /// </remarks>
    public static async Task<DentalRecordReversal> InspectAsync(
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        Guid clinicId,
        DentalRecord record,
        CancellationToken cancellationToken)
    {
        var collections = new List<PlanCollectionReversal>();
        var plans = await planRepository.GetByCollectedDentalRecordAsync(clinicId, record.Id, cancellationToken);

        foreach (var plan in plans)
        {
            var rows = plan.Installments
                .SelectMany(i => i.Payments.Select(p => (Installment: i, Payment: p)))
                .Where(x => !x.Payment.IsVoided && x.Payment.DentalRecordId == record.Id)
                .ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            /*
             * ⚠️ R1 — a cheque already banked. Neither `Installment.VoidPayment` nor `Invoice.VoidPayment`
             * refuses one, so without this the cascade would quietly un-collect a cheque that has physically
             * cleared at the bank. `SetPaymentBanked` already treats the two states as incompatible in the
             * other direction (« ce paiement est annulé : il ne détient plus de chèque à encaisser »); this is
             * the same incompatibility met from the side where money is at stake.
             */
            var banked = rows.FirstOrDefault(x => x.Payment.ChequeBankedOn.HasValue);
            if (banked.Payment is not null)
            {
                return DentalRecordReversal.Refused(
                    $"Cette séance a encaissé un chèque déjà marqué encaissé en banque "
                    + $"({banked.Payment.Amount:0.000} DT sur {DevisLabel(plan)}). "
                    + "Un chèque passé en banque ne peut pas être annulé par une suppression : "
                    + "corrigez-le depuis l'échéancier du devis, puis supprimez la fiche.");
            }

            collections.Add(new PlanCollectionReversal(
                plan,
                rows.Select(x => (x.Installment.Id, x.Payment.Id, x.Payment.Amount, x.Payment.PaidOn)).ToList()));
        }

        var notes = await invoiceRepository.GetByDentalRecordAsync(clinicId, record.Id, cancellationToken);

        /*
         * ⚠️ R10 — a note that also bills another fiche. There is none in the product today (a note raised from
         * a fiche takes all its lines from that fiche) and none in the live database, which is exactly why the
         * guard is cheap and worth having: the day a second source of lines appears, cancelling the whole note
         * to undo one séance would erase billing for work that did happen.
         */
        var shared = notes.FirstOrDefault(n =>
            n.Lines.Any(l => l.DentalRecordId.HasValue && l.DentalRecordId.Value != record.Id));
        if (shared is not null)
        {
            return DentalRecordReversal.Refused(
                $"La note {NoteLabel(shared)} facture aussi d'autres séances : elle ne peut pas être annulée "
                + "en supprimant celle-ci. Établissez un avoir sur la ligne concernée.");
        }

        var noteReversals = new List<NoteReversal>();
        foreach (var note in notes)
        {
            if (note.Status == InvoiceStatus.Cancelled)
            {
                // Already annulled: nothing of its money is claimed, so there is nothing to undo. It keeps its
                // number and its trail, as a cancelled document must.
                continue;
            }

            var live = note.Payments.Where(p => !p.IsVoided).ToList();

            var bankedOnNote = live.FirstOrDefault(p => p.ChequeBankedOn.HasValue);
            if (bankedOnNote is not null)
            {
                return DentalRecordReversal.Refused(
                    $"La note {NoteLabel(note)} a encaissé un chèque déjà marqué encaissé en banque "
                    + $"({bankedOnNote.Amount:0.000} DT). Corrigez-le sur la note, puis supprimez la fiche.");
            }

            /*
             * ⚠️ R2 — an avoir already established. `Invoice.VoidPayment` refuses to take the collected total
             * below what the practice has already refunded on paper, and it refuses PER PAYMENT, part-way
             * through. Asking the same question here, of the whole set, is what turns a cascade that dies
             * half-applied into a refusal that names the remedy before anything moves.
             */
            var credited = await creditNoteRepository.GetTotalForInvoiceAsync(note.Id, cancellationToken);
            if (InvoiceCalculator.RoundMoney(credited) > 0m
                && InvoiceCalculator.RoundMoney(note.AmountCollected - live.Sum(p => p.Amount)) < InvoiceCalculator.RoundMoney(credited))
            {
                return DentalRecordReversal.Refused(
                    $"Un avoir de {credited:0.000} DT a déjà été établi sur la note {NoteLabel(note)} : "
                    + "ses encaissements ne peuvent plus être annulés. Complétez l'avoir, puis supprimez la fiche.");
            }

            noteReversals.Add(new NoteReversal(
                note,
                live.Select(p => (p.Id, p.Amount, p.PaidOn)).ToList(),
                credited));
        }

        return new DentalRecordReversal(collections, noteReversals, null);
    }

    /// <summary>
    /// Carry out exactly what <see cref="InspectAsync"/> decided. Stages every write on its repository; the
    /// caller's transaction and single <c>SaveChangesAsync</c> commit them together.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Nothing here is sent through MediatR</b>, deliberately. The void and cancel commands each call
    /// <c>SaveChangesAsync</c> on this same scoped DbContext, so routing through them would commit the deletion's
    /// half-applied state — the trap <c>AmendTreatmentPlanCommand</c> documents at length. The aggregates'
    /// own methods are called directly and the one transaction stays one transaction.
    /// </remarks>
    public static async Task ApplyAsync(
        DentalRecordReversal reversal,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        DateTime interventionDate,
        string? actorUserId,
        string? actorName,
        CancellationToken cancellationToken)
    {
        var reason = ReasonFor(interventionDate);

        foreach (var collection in reversal.PlanCollections)
        {
            foreach (var (installmentId, paymentId, _, _) in collection.Payments)
            {
                collection.Plan.VoidInstallmentPayment(installmentId, paymentId, reason, actorUserId, actorName);
            }

            await planRepository.UpdateAsync(collection.Plan, cancellationToken);
        }

        foreach (var note in reversal.Notes)
        {
            /*
             * ⚠️ R3 — THE BRIDGE, and it has to be released before the note is annulled, on BOTH sides.
             *
             * `PlanBillingRules.BilledPlanIds` drops a whole devis from « Solde patient », « Créances », la
             * caisse and the dashboard the moment a live note names it. Annulling such a note without releasing
             * it would put the devis' ENTIRE total back into every one of those reads — while the money the
             * note carried does not travel back, because `Invoice.Cancel` says so itself: « pour une facture
             * issue d'un devis, les encaissements du devis y ont été reportés à l'émission et ne repartent pas
             * en arrière ». The patient would appear to owe the whole treatment again.
             *
             * `TreatmentPlanBridgeRelease` is the invoice half and `TreatmentPlan.DetachNote` is the plan half;
             * both are called, because releasing one side and not the other is how the link came to be
             * write-once in the first place. The freed acts deliberately stay priced at 0 — `DetachNote` says
             * why: nothing here knows what they are worth, and inventing a price is the dentist's call.
             */
            if (note.Invoice.TreatmentPlanId is { } bridgedPlanId)
            {
                var plan = reversal.PlanCollections.FirstOrDefault(c => c.Plan.Id == bridgedPlanId)?.Plan
                    ?? await planRepository.GetByIdAsync(bridgedPlanId, cancellationToken);

                if (plan is not null)
                {
                    plan.DetachNote(note.Invoice.Id);
                    await planRepository.UpdateAsync(plan, cancellationToken);
                }

                note.Invoice.DetachFromTreatmentPlan(bridgedPlanId);
            }

            if (note.Invoice.Status == InvoiceStatus.Draft)
            {
                // A draft holds no number, so there is no sequence to leave a hole in and nothing was ever
                // claimed from the patient. `Invoice.Cancel` refuses it in as many words — « un brouillon se
                // supprime, il ne s'annule pas » — and this is that sentence obeyed.
                await invoiceRepository.DeleteAsync(note.Invoice.Id, cancellationToken);
                continue;
            }

            foreach (var (paymentId, _, _) in note.Payments)
            {
                note.Invoice.VoidPayment(paymentId, reason, note.CreditedTotal, actorUserId, actorName);
            }

            /*
             * Now legitimate, and `Invoice.Cancel`'s own comment is the authority: « Voided payments do not
             * count: a note whose only payments were data-entry errors was never really paid, so cancelling it
             * is legitimate. » The number is kept and the document reads « Annulée » — the fiscal sequence
             * never gains a hole, which is the one thing that genuinely cannot be undone.
             */
            note.Invoice.Cancel(reason);
            await invoiceRepository.UpdateAsync(note.Invoice, cancellationToken);
        }
    }

    /// <summary>
    /// The motif written on every row this reversal annuls.
    /// <para>
    /// ⚠️ It is for a human to read and <b>nothing may branch on it</b> — « never recover an outcome by matching
    /// French prose » is a rule this codebase learned by having a <c>Contains("déjà facturée")</c> change
    /// behaviour when a sentence was reworded. Anything that needs to recognise this case gets a field.
    /// </para>
    /// </summary>
    public static string ReasonFor(DateTime interventionDate) =>
        $"Fiche de soins du {interventionDate:dd/MM/yyyy} supprimée";

    private static string DevisLabel(TreatmentPlan plan) =>
        plan.Number is null ? "ce traitement" : $"le devis {plan.Number}";

    private static string NoteLabel(Invoice note) =>
        note.Number is null ? "brouillon" : $"n° {note.Number}";
}

/// <summary>What deleting a fiche will undo — the whole decision, taken before anything moves.</summary>
/// <param name="PlanCollections">The chairside money this fiche put on a devis échéancier.</param>
/// <param name="Notes">The note d'honoraires it raised, if any.</param>
/// <param name="Refusal">
/// Non-null when the deletion must be refused, already phrased for the user. A refusal is always preferred to a
/// partial reversal: money half-undone is the one outcome nobody can read.
/// </param>
public sealed record DentalRecordReversal(
    IReadOnlyList<PlanCollectionReversal> PlanCollections,
    IReadOnlyList<NoteReversal> Notes,
    string? Refusal)
{
    public static DentalRecordReversal Refused(string message) =>
        new(Array.Empty<PlanCollectionReversal>(), Array.Empty<NoteReversal>(), message);

    public bool IsRefused => Refusal is not null;

    /// <summary>True when this deletion moves money — what makes the confirmation a warning rather than a nicety.</summary>
    public bool TouchesMoney => PlanCollections.Count > 0 || Notes.Any(n => n.Payments.Count > 0);

    /// <summary>Everything that will leave la caisse, devis and note together.</summary>
    public decimal TotalReversed => InvoiceCalculator.RoundMoney(
        PlanCollections.Sum(c => c.Payments.Sum(p => p.Amount))
        + Notes.Sum(n => n.Payments.Sum(p => p.Amount)));

    /// <summary>
    /// The days whose caisse figures move, earliest first. Named in the warning because a void lands on the day
    /// the money was RECEIVED, not today — so a deletion silently rewrites an extrait somebody may already have
    /// printed.
    /// </summary>
    public IReadOnlyList<DateTime> AffectedCaisseDays => PlanCollections
        .SelectMany(c => c.Payments.Select(p => p.PaidOn.Date))
        .Concat(Notes.SelectMany(n => n.Payments.Select(p => p.PaidOn.Date)))
        .Distinct()
        .OrderBy(d => d)
        .ToList();
}

/// <summary>One devis, and the payments this fiche put on its échéancier.</summary>
public sealed record PlanCollectionReversal(
    TreatmentPlan Plan,
    IReadOnlyList<(Guid InstallmentId, Guid PaymentId, decimal Amount, DateTime PaidOn)> Payments);

/// <summary>One note d'honoraires raised from this fiche, and the live payments on it.</summary>
/// <param name="CreditedTotal">Σ avoirs already issued — <c>Invoice.VoidPayment</c> requires it (R2).</param>
public sealed record NoteReversal(
    Invoice Invoice,
    IReadOnlyList<(Guid PaymentId, decimal Amount, DateTime PaidOn)> Payments,
    decimal CreditedTotal);
